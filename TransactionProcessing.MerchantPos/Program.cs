using ClientProxyBase;
using MerchantPos.EF.Persistence;
using Microsoft.EntityFrameworkCore;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;
using SecurityService.Client;
using Shared.Serialisation;
using System.Text.Json;
using TransactionProcessing.MerchantPos.Persistence;
using TransactionProcessing.MerchantPos.Runtime;
using TransactionProcessor.Client;

var logger = LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();

try
{
    logger.Info("Starting application initialization");

    var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environments.Production;

    var contentRoot = AppContext.BaseDirectory;

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = contentRoot,
        EnvironmentName = envName
    });

    builder.Configuration.AddJsonFile("hosting.json", optional: true, reloadOnChange: true);
    builder.Configuration
        .SetBasePath(builder.Environment.ContentRootPath)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args);

    builder.Host.UseNLog();
    builder.Host.UseWindowsService();

    builder.Logging.ClearProviders();
    builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
    builder.Logging.AddNLog();

    builder.Services.AddHostedService<WorkerHost>();
    builder.Services.AddSingleton<MerchantPosSettingsStore>();
    builder.Services.AddSingleton<MerchantMetrics>();
    builder.Services.AddSingleton<IStringSerialiser, SystemTextJsonSerializer>();
    builder.Services.AddSingleton<Func<Object, String>>(_ => obj => StringSerialiser.Serialise(obj));
    builder.Services.AddSingleton<Func<String, Type, Object>>(_ => (str, type) => StringSerialiser.DeserializeObject<Object>(str, type));
    builder.Services.AddSingleton(SystemTextJsonSerializer.GetDefaultJsonSerializerOptions());
    builder.Services.AddScoped<MerchantDashboardModelFactory>();
    builder.Services.AddRazorPages();
    builder.Services.AddHttpContextAccessor();
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

    builder.Services.RegisterHttpClient<ISecurityServiceClient, SecurityServiceClient>();
    builder.Services.RegisterHttpClient<ITransactionProcessorClient, TransactionProcessorClient>();
    builder.Services.AddScoped<IEfRepository, EfRepository>();
    builder.Services.AddScoped<MerchantRuntime>();
    builder.Services.RegisterHttpClient<IApiClient, ApiClient>();
    builder.Services.AddSingleton<IMerchantRuntimeFactory, MerchantRuntimeFactory>();

    builder.Services.AddDbContext<MerchantDbContext>((sp, options) =>
    {
        var settings = sp.GetRequiredService<MerchantPosSettingsStore>().Current;
        var connectionString = MerchantPosSettingsStore.BuildSqliteConnectionString(settings.ConnectionStrings.MerchantDb);
        options.UseSqlite(connectionString);
    });

    builder.Services.AddHealthChecks();

    var app = builder.Build();

    using (var scope = app.Services.CreateScope())
    {
        var settingsStore = scope.ServiceProvider.GetRequiredService<MerchantPosSettingsStore>();
        await settingsStore.InitialiseAsync();

        var db = scope.ServiceProvider.GetRequiredService<MerchantDbContext>();
        await db.Database.EnsureCreatedAsync();

        var diLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
        Shared.Logger.Logger.Initialise(diLogger);

        var serialiser = scope.ServiceProvider.GetRequiredService<IStringSerialiser>();
        StringSerialiser.Initialise(serialiser);
    }

    app.MapGet("/api/settings", (MerchantPosSettingsStore settingsStore) =>
        Results.Json(settingsStore.Current));

    app.MapGet("/api/dashboard", async (MerchantDashboardModelFactory factory) =>
        Results.Json(await factory.BuildAsync()));

    app.MapRazorPages();
    app.MapHealthChecks("/health");

    await app.RunAsync();
}
catch (Exception ex)
{
    logger.Error(ex, "Application stopped because of exception");
    throw;
}
finally
{
    LogManager.Shutdown();
}
