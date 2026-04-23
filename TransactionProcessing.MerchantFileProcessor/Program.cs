using ClientProxyBase;
using FileProcessor.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using SecurityService.Client;
using TransactionProcessing.MerchantFileProcessor;
using TransactionProcessing.MerchantFileProcessor.Clients;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessing.MerchantFileProcessor.FileBuilding;
using TransactionProcessing.MerchantFileProcessor.Persistence;
using TransactionProcessing.MerchantFileProcessor.Reporting;
using TransactionProcessing.MerchantFileProcessor.Services;
using SharedLogger = Shared.Logger.Logger;
using TransactionProcessor.Client;

var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                      ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                      ?? Environments.Production;
var contentRoot = AppContext.BaseDirectory;
var nlogConfigPath = Path.Combine(contentRoot, "NLog.config");

var bootstrapLogger = LogManager.Setup()
    .LoadConfigurationFromFile(nlogConfigPath)
    .GetCurrentClassLogger();

try
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = contentRoot,
        EnvironmentName = environmentName
    });

    builder.Configuration.Sources.Clear();
    builder.Configuration
        .SetBasePath(contentRoot)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddJsonFile("hosting.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args);

    builder.WebHost.UseConfiguration(builder.Configuration);

    var frameworkLoggingOptions = builder.Configuration
        .GetSection(FrameworkLoggingOptions.SectionName)
        .Get<FrameworkLoggingOptions>() ?? new FrameworkLoggingOptions();

    builder.Logging.ClearProviders();
    builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
    builder.Logging.AddFilter((category, level) => ShouldLogCategory(category, level, frameworkLoggingOptions));
    builder.Logging.AddNLog(new NLogProviderOptions
    {
        RemoveLoggerFactoryFilter = false
    });

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "Merchant File Processor";
    });

    var merchantProcessingOptions = builder.Configuration
        .GetSection(MerchantProcessingOptions.SectionName)
        .Get<MerchantProcessingOptions>() ?? new MerchantProcessingOptions();

    if (!MerchantProcessingOptionsValidator.Validate(merchantProcessingOptions))
    {
        throw new InvalidOperationException("MerchantProcessing configuration is invalid.");
    }

    builder.Services.AddSingleton(merchantProcessingOptions);
    builder.Services.AddSingleton(frameworkLoggingOptions);

    builder.Services.AddSingleton<Func<string, string>>(sp =>
    {
        var apiConfiguration = sp.GetRequiredService<IConfiguration>().GetSection("ApiConfiguration");

        return configSetting =>
        {
            if (string.IsNullOrWhiteSpace(configSetting))
            {
                return string.Empty;
            }

            var child = apiConfiguration.GetChildren()
                .FirstOrDefault(c => string.Equals(c.Key, configSetting, StringComparison.OrdinalIgnoreCase));

            return child?.Value ?? string.Empty;
        };
    });

    var connectionString = BuildConnectionString(
        builder.Configuration.GetConnectionString("MerchantFileProcessor"),
        contentRoot);

    builder.Services.AddHttpClient();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddDbContextFactory<MerchantFileProcessorDbContext>(options =>
        options.UseSqlite(connectionString));
    builder.Services.RegisterHttpClient<IFileProcessorClient, FileProcessorClient>();
    builder.Services.RegisterHttpClient<ISecurityServiceClient, SecurityServiceClient>();
    builder.Services.RegisterHttpClient<ITransactionProcessorClient, TransactionProcessorClient>();
    builder.Services.AddSingleton<IAccessTokenProvider, SharedAccessTokenProvider>();
    builder.Services.AddSingleton<ITransactionGenerator, RandomTransactionGenerator>();
    builder.Services.AddSingleton<IFileStatusStore, FileStatusStore>();
    builder.Services.AddSingleton<IFileStatusReportService, FileStatusReportService>();
    builder.Services.AddTransient<IMerchantContractDataClient, MerchantContractDataClient>();
    builder.Services.AddTransient<IMerchantDepositClient, MerchantDepositClient>();
    builder.Services.AddTransient<ITransactionFileBuilder, DelimitedTransactionFileBuilder>();
    builder.Services.AddTransient<ITransactionFileBuilder, JsonTransactionFileBuilder>();
    builder.Services.AddTransient<ITransactionFileGenerationService, TransactionFileGenerationService>();
    builder.Services.AddTransient<IFileProcessingClient, FileProcessingClient>();
    builder.Services.AddTransient<IMerchantProcessingService, MerchantProcessingService>();
    builder.Services.AddHostedService<Worker>();
    builder.Services.AddHostedService<FileStatusPollingWorker>();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var startupLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
        SharedLogger.Initialise(startupLogger);

        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MerchantFileProcessorDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync();
        await dbContext.Database.EnsureCreatedAsync();
        await EnsurePersistenceSchemaAsync(dbContext, CancellationToken.None);
    }

    var appSettingsPath = Path.Combine(contentRoot, "appsettings.json");
    if (!File.Exists(appSettingsPath))
    {
        SharedLogger.LogWarning($"appsettings.json was not found at {appSettingsPath}. Using environment-specific configuration and environment variables.");
    }

    app.MapReportingEndpoints();

    await app.RunAsync();
}
catch (Exception ex)
{
    bootstrapLogger.Error(ex, "Merchant file processor terminated unexpectedly.");
    throw;
}
finally
{
    LogManager.Shutdown();
}

static string BuildConnectionString(string? configuredConnectionString, string contentRoot)
{
    if (string.IsNullOrWhiteSpace(configuredConnectionString))
    {
        return $"Data Source={Path.Combine(contentRoot, "merchant-file-processor.db")}";
    }

    const string dataSourcePrefix = "Data Source=";

    if (!configuredConnectionString.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
    {
        return configuredConnectionString;
    }

    var filePath = configuredConnectionString[dataSourcePrefix.Length..].Trim();

    if (Path.IsPathRooted(filePath))
    {
        return configuredConnectionString;
    }

    return $"{dataSourcePrefix}{Path.Combine(contentRoot, filePath)}";
}

static bool ShouldLogCategory(
    string? category,
    Microsoft.Extensions.Logging.LogLevel level,
    FrameworkLoggingOptions frameworkLoggingOptions)
{
    if (level < Microsoft.Extensions.Logging.LogLevel.Information)
    {
        return false;
    }

    if (string.IsNullOrWhiteSpace(category))
    {
        return true;
    }

    if (category.StartsWith("Microsoft.EntityFrameworkCore.Database.Command", StringComparison.OrdinalIgnoreCase))
    {
        return frameworkLoggingOptions.EnableEfCoreCommandTrace ||
               level >= Microsoft.Extensions.Logging.LogLevel.Warning;
    }

    if (category.StartsWith("System.Net.Http.HttpClient", StringComparison.OrdinalIgnoreCase))
    {
        return frameworkLoggingOptions.EnableHttpClientTrace ||
               level >= Microsoft.Extensions.Logging.LogLevel.Warning;
    }

    return true;
}

static async Task EnsurePersistenceSchemaAsync(MerchantFileProcessorDbContext dbContext, CancellationToken cancellationToken)
{
    var existingColumns = await dbContext.Database
        .SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('FileSendRecords');")
        .ToListAsync(cancellationToken);

    if (!existingColumns.Contains("MerchantName", StringComparer.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FileSendRecords ADD COLUMN MerchantName TEXT NULL;",
            cancellationToken);
    }

    if (!existingColumns.Contains("ContractName", StringComparer.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FileSendRecords ADD COLUMN ContractName TEXT NULL;",
            cancellationToken);
    }

    if (!existingColumns.Contains("FileContent", StringComparer.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FileSendRecords ADD COLUMN FileContent TEXT NULL;",
            cancellationToken);
    }

    if (!existingColumns.Contains("EstateId", StringComparer.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FileSendRecords ADD COLUMN EstateId TEXT NULL;",
            cancellationToken);
    }

    if (!existingColumns.Contains("FileProcessorFileId", StringComparer.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FileSendRecords ADD COLUMN FileProcessorFileId TEXT NULL;",
            cancellationToken);
    }

    if (!existingColumns.Contains("ProcessingCompleted", StringComparer.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FileSendRecords ADD COLUMN ProcessingCompleted INTEGER NOT NULL DEFAULT 0;",
            cancellationToken);
    }

    if (!existingColumns.Contains("LastStatusCheckUtc", StringComparer.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FileSendRecords ADD COLUMN LastStatusCheckUtc TEXT NULL;",
            cancellationToken);
    }

    if (!existingColumns.Contains("ScheduledRunUtc", StringComparer.OrdinalIgnoreCase))
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            "ALTER TABLE FileSendRecords ADD COLUMN ScheduledRunUtc TEXT NOT NULL DEFAULT '0001-01-01T00:00:00+00:00';",
            cancellationToken);
    }

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE IF NOT EXISTS FileSendRecordLineStatuses (
            Id INTEGER NOT NULL CONSTRAINT PK_FileSendRecordLineStatuses PRIMARY KEY AUTOINCREMENT,
            FileSendRecordId INTEGER NOT NULL,
            LineNumber INTEGER NOT NULL,
            LineData TEXT NULL,
            ProcessingStatus TEXT NOT NULL,
            RejectionReason TEXT NULL,
            TransactionId TEXT NULL,
            UpdatedUtc TEXT NOT NULL,
            CONSTRAINT FK_FileSendRecordLineStatuses_FileSendRecords_FileSendRecordId
                FOREIGN KEY (FileSendRecordId) REFERENCES FileSendRecords (Id) ON DELETE CASCADE
        );
        """,
        cancellationToken);

    await dbContext.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_FileSendRecordLineStatuses_FileSendRecordId_LineNumber ON FileSendRecordLineStatuses (FileSendRecordId, LineNumber);",
        cancellationToken);

    await dbContext.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE IF NOT EXISTS MerchantRunRecords (
            Id INTEGER NOT NULL CONSTRAINT PK_MerchantRunRecords PRIMARY KEY AUTOINCREMENT,
            RunId TEXT NOT NULL,
            MerchantId TEXT NOT NULL,
            MerchantName TEXT NULL,
            ScheduledRunUtc TEXT NOT NULL,
            Status TEXT NOT NULL,
            ErrorMessage TEXT NULL,
            CompletedUtc TEXT NOT NULL
        );
        """,
        cancellationToken);

    await dbContext.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS IX_MerchantRunRecords_MerchantId_ScheduledRunUtc_CompletedUtc ON MerchantRunRecords (MerchantId, ScheduledRunUtc, CompletedUtc);",
        cancellationToken);
}
