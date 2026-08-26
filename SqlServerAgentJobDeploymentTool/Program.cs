namespace SqlServerAgentJobDeploymentTool;

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        string contentRoot = AppContext.BaseDirectory;
        string nlogConfigPath = Path.Combine(contentRoot, "NLog.config");
        LogManager.Setup()
            .LoadConfigurationFromFile(nlogConfigPath)
            .GetCurrentClassLogger();

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
            builder.AddNLog(new NLogProviderOptions
            {
                RemoveLoggerFactoryFilter = false
            });
        });

        Microsoft.Extensions.Logging.ILogger logger = loggerFactory.CreateLogger(nameof(Program));

        try
        {
            return await RunAsync(args, loggerFactory, logger);
        }
        catch (Exception ex)
        {
            DeploymentErrorReporter.ReportCli(ex, logger, Console.Error, "starting", null);
            return 1;
        }
        finally
        {
            try
            {
                LogManager.Shutdown();
            }
            catch (Exception shutdownEx)
            {
                logger.LogWarning(shutdownEx, "NLog shutdown failed.");
            }
        }
    }

    private static async Task<int> RunAsync(
        string[] args,
        ILoggerFactory loggerFactory,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        logger.LogInformation("Starting SQL Server Agent Job Deployment Tool.");

        if (ShouldLaunchUi(args))
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm(loggerFactory));
            return 0;
        }

        DeploymentOptions options = DeploymentOptions.Parse(args);
        logger.LogInformation("Command-line mode selected. Manifest path: {ManifestPath}. Dry run: {DryRun}. Database override: {DatabaseOverride}.",
            options.ManifestPath,
            options.WhatIf,
            string.IsNullOrWhiteSpace(options.DatabaseName) ? "<none>" : options.DatabaseName);

        if (options.ShowHelp)
        {
            PrintUsage();
            logger.LogInformation("Program completed with exit code 0.");
            return 0;
        }

        string currentOperation = "loading manifest";
        string? currentContext = options.ManifestPath;
        DeploymentManifest manifest = await DeploymentManifestLoader.LoadAsync(options.ManifestPath, CancellationToken.None);
        logger.LogInformation("Loaded manifest with {JobCount} job(s).", manifest.Jobs.Count);

        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            manifest.ConnectionString = options.ConnectionString;
            logger.LogInformation("Using connection string override from command line.");
        }

        if (string.IsNullOrWhiteSpace(manifest.ConnectionString))
        {
            Console.Error.WriteLine("A connection string must be supplied in the manifest or with --connection-string.");
            return 1;
        }

        currentOperation = "validating manifest";
        ManifestValidator.Validate(manifest);
        logger.LogInformation("Manifest validation succeeded.");

        if (options.WhatIf)
        {
            logger.LogInformation("Dry run requested. The following jobs would be deployed:");
            foreach (JobDefinition job in manifest.Jobs)
            {
                logger.LogInformation("Dry run job: {JobName}", job.Name);
            }

            logger.LogInformation("Program completed with exit code 0.");
            return 0;
        }

        currentOperation = "opening SQL connection";
        currentContext = DeploymentErrorReporter.DescribeConnectionTarget(manifest.ConnectionString);
        Microsoft.Data.SqlClient.SqlConnection connection = new(manifest.ConnectionString);

        try
        {
            logger.LogInformation("Opening SQL connection to {ConnectionTarget}.", currentContext);
            await connection.OpenAsync(CancellationToken.None);
            logger.LogInformation("SQL connection opened to {ConnectionTarget}.", currentContext);

            currentOperation = "deploying SQL Agent jobs";
            var deployer = new SqlAgentDeploymentService(connection, Console.Out, loggerFactory.CreateLogger<SqlAgentDeploymentService>());
            await deployer.DeployAsync(manifest, options.DatabaseName, CancellationToken.None);
            logger.LogInformation("Deployment completed successfully.");
            logger.LogInformation("Program completed with exit code 0.");
            return 0;
        }
        finally
        {
            try
            {
                await connection.DisposeAsync();
            }
            catch (Exception disposeEx)
            {
                logger.LogWarning(disposeEx, "Failed to dispose SQL connection after {Operation}.", currentOperation);
            }
        }
    }

    private static bool ShouldLaunchUi(string[] args)
        => args.Length == 0 || args.Any(arg => arg.Equals("--ui", StringComparison.OrdinalIgnoreCase));

    private static void PrintUsage()
    {
            Console.WriteLine("""
            SQL Server Agent Job Deployment Tool

            Usage:
              SqlServerAgentJobDeploymentTool [--ui]
              SqlServerAgentJobDeploymentTool --manifest <path> [--connection-string <value>] [--database-name <value>] [--dry-run]

            Options:
              --ui                         Launch the desktop UI.
              --manifest <path>            Path to the deployment manifest JSON file.
              --connection-string <value>  Overrides the manifest connection string.
              --database-name <value>      Overrides the database used by SQL Agent T-SQL steps.
              --dry-run                    Validate the manifest and show the jobs without changing SQL Server.
              --help                       Show this help text.
            """);
    }
}

internal sealed record DeploymentOptions(string ManifestPath, string? ConnectionString, string? DatabaseName, bool WhatIf, bool ShowHelp)
{
    public static DeploymentOptions Parse(string[] args)
    {
        string manifestPath = "sql-agent-jobs.json";
        string? connectionString = null;
        string? databaseName = null;
        bool whatIf = false;
        bool showHelp = false;

        for (int index = 0; index < args.Length; index++)
        {
            string current = args[index];

            if (current is "--help" or "-h" or "/?")
            {
                showHelp = true;
                continue;
            }

            if (current is "--dry-run" or "--what-if")
            {
                whatIf = true;
                continue;
            }

            if (current.StartsWith("--manifest=", StringComparison.OrdinalIgnoreCase))
            {
                manifestPath = current["--manifest=".Length..];
                continue;
            }

            if (current.Equals("--manifest", StringComparison.OrdinalIgnoreCase))
            {
                manifestPath = ReadNext(args, ref index, "--manifest");
                continue;
            }

            if (current.StartsWith("--connection-string=", StringComparison.OrdinalIgnoreCase))
            {
                connectionString = current["--connection-string=".Length..];
                continue;
            }

            if (current.Equals("--connection-string", StringComparison.OrdinalIgnoreCase))
            {
                connectionString = ReadNext(args, ref index, "--connection-string");
                continue;
            }

            if (current.StartsWith("--database-name=", StringComparison.OrdinalIgnoreCase))
            {
                databaseName = current["--database-name=".Length..];
                continue;
            }

            if (current.Equals("--database-name", StringComparison.OrdinalIgnoreCase))
            {
                databaseName = ReadNext(args, ref index, "--database-name");
                continue;
            }

            throw new ArgumentException($"Unrecognised argument '{current}'. Use --help for usage.");
        }

        return new DeploymentOptions(manifestPath, connectionString, databaseName, whatIf, showHelp);
    }

    private static string ReadNext(string[] args, ref int index, string optionName)
    {
        int nextIndex = index + 1;
        if (nextIndex >= args.Length)
        {
            throw new ArgumentException($"Missing value for '{optionName}'.");
        }

        index = nextIndex;
        return args[nextIndex];
    }
}

internal static class DeploymentManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public static async Task<DeploymentManifest> LoadAsync(string manifestPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Manifest file '{manifestPath}' was not found.", manifestPath);
        }

        await using FileStream stream = File.OpenRead(manifestPath);
        using StreamReader reader = new(stream);
        string json = await reader.ReadToEndAsync(cancellationToken);
        DeploymentManifest manifest = Parse(json);

        return manifest;
    }

    public static string Format(string json)
    {
        DeploymentManifest manifest = Parse(json);
        return Serialize(manifest);
    }

    public static string Serialize(DeploymentManifest manifest)
    {
        JsonSerializerOptions options = new(JsonOptions)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        return JsonSerializer.Serialize(manifest, options);
    }

    public static DeploymentManifest Parse(string json)
    {
        DeploymentManifest? manifest = JsonSerializer.Deserialize<DeploymentManifest>(json, JsonOptions);

        if (manifest is null)
        {
            throw new InvalidOperationException("The manifest content did not contain a valid deployment manifest.");
        }

        return manifest;
    }
}

internal static class ManifestValidator
{
    public static void Validate(DeploymentManifest manifest)
    {
        if (manifest.Jobs.Count == 0)
        {
            throw new InvalidOperationException("The manifest does not contain any jobs.");
        }

        foreach (JobDefinition job in manifest.Jobs)
        {
            ValidateJob(job);
        }
    }

    private static void ValidateJob(JobDefinition job)
    {
        if (string.IsNullOrWhiteSpace(job.Name))
        {
            throw new InvalidOperationException("Each job must have a name.");
        }

        if (job.Steps.Count == 0)
        {
            throw new InvalidOperationException($"Job '{job.Name}' does not contain any steps.");
        }

        ValidateSteps(job);
        ValidateSchedules(job);
    }

    private static void ValidateSteps(JobDefinition job)
    {
        var stepIds = new HashSet<int>();
        int nextStepId = 1;

        foreach (JobStepDefinition step in job.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
            {
                throw new InvalidOperationException($"Job '{job.Name}' contains a step with no name.");
            }

            if (string.IsNullOrWhiteSpace(step.Command))
            {
                throw new InvalidOperationException($"Job '{job.Name}' step '{step.Name}' does not contain a command.");
            }

            int stepId = step.StepId ?? nextStepId;
            nextStepId = stepId + 1;

            if (!stepIds.Add(stepId))
            {
                throw new InvalidOperationException($"Job '{job.Name}' contains duplicate step id '{stepId}'.");
            }

            ValidateStepReferences(job, step);
        }
    }

    private static void ValidateStepReferences(JobDefinition job, JobStepDefinition step)
    {
        if (step.OnSuccessAction == JobStepAction.GoToStep && step.OnSuccessStepId is null)
        {
            throw new InvalidOperationException($"Job '{job.Name}' step '{step.Name}' must define OnSuccessStepId when OnSuccessAction is GoToStep.");
        }

        if (step.OnFailAction == JobStepAction.GoToStep && step.OnFailStepId is null)
        {
            throw new InvalidOperationException($"Job '{job.Name}' step '{step.Name}' must define OnFailStepId when OnFailAction is GoToStep.");
        }
    }

    private static void ValidateSchedules(JobDefinition job)
    {
        foreach (JobScheduleDefinition schedule in job.Schedules)
        {
            if (string.IsNullOrWhiteSpace(schedule.Name))
            {
                throw new InvalidOperationException($"Job '{job.Name}' contains a schedule with no name.");
            }
        }
    }
}
