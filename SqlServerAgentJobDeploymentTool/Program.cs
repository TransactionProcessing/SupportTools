namespace SqlServerAgentJobDeploymentTool;

using System.Text.Json;
using System.Text.Json.Serialization;

internal static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (ShouldLaunchUi(args))
            {
                ApplicationConfiguration.Initialize();
                Application.Run(new MainForm());
                return 0;
            }

            DeploymentOptions options = DeploymentOptions.Parse(args);

            if (options.ShowHelp)
            {
                PrintUsage();
                return 0;
            }

            DeploymentManifest manifest = await DeploymentManifestLoader.LoadAsync(options.ManifestPath, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                manifest.ConnectionString = options.ConnectionString;
            }

            if (string.IsNullOrWhiteSpace(manifest.ConnectionString))
            {
                Console.Error.WriteLine("A connection string must be supplied in the manifest or with --connection-string.");
                return 1;
            }

            ManifestValidator.Validate(manifest);

            if (options.WhatIf)
            {
                Console.WriteLine("Dry run. The following jobs would be deployed:");
                foreach (JobDefinition job in manifest.Jobs)
                {
                    Console.WriteLine($"- {job.Name}");
                }

                return 0;
            }

            await using var connection = new Microsoft.Data.SqlClient.SqlConnection(manifest.ConnectionString);
            await connection.OpenAsync(CancellationToken.None);

            var deployer = new SqlAgentDeploymentService(connection, Console.Out);
            await deployer.DeployAsync(manifest, options.DatabaseName, CancellationToken.None);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
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
        string json = await new StreamReader(stream).ReadToEndAsync(cancellationToken);
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
            if (string.IsNullOrWhiteSpace(job.Name))
            {
                throw new InvalidOperationException("Each job must have a name.");
            }

            if (job.Steps.Count == 0)
            {
                throw new InvalidOperationException($"Job '{job.Name}' does not contain any steps.");
            }

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

                if (step.OnSuccessAction == JobStepAction.GoToStep && step.OnSuccessStepId is null)
                {
                    throw new InvalidOperationException($"Job '{job.Name}' step '{step.Name}' must define OnSuccessStepId when OnSuccessAction is GoToStep.");
                }

                if (step.OnFailAction == JobStepAction.GoToStep && step.OnFailStepId is null)
                {
                    throw new InvalidOperationException($"Job '{job.Name}' step '{step.Name}' must define OnFailStepId when OnFailAction is GoToStep.");
                }
            }

            foreach (JobScheduleDefinition schedule in job.Schedules)
            {
                if (string.IsNullOrWhiteSpace(schedule.Name))
                {
                    throw new InvalidOperationException($"Job '{job.Name}' contains a schedule with no name.");
                }
            }
        }
    }
}
