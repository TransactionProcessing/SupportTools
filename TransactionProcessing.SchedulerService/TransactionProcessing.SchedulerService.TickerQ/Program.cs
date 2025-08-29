using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using Shared.General;
using System.Reflection;
using NLog.Extensions.Logging;
using NLog.Web;
using SecurityService.Client;
using TickerQ.Dashboard.DependencyInjection;
using TickerQ.DependencyInjection;
using TickerQ.EntityFrameworkCore.DependencyInjection;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;
using TransactionProcessing.SchedulerService.TickerQ.Database;
using TransactionProcessing.SchedulerService.TickerQ.Jobs;
using TransactionProcessor.Client;
using ILogger = Microsoft.Extensions.Logging.ILogger;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

ConfigurationReader.Initialise(builder.Configuration);

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddDbContext<SchedulerContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TickerQ"))
);
builder.Services.AddTickerQ(options =>
{
    options.AddDashboard(o => {
        o.BasePath = "/t";
        o.EnableBasicAuth = true;
    });
    
    options.AddOperationalStore<SchedulerContext>(efOpt =>
    {
        efOpt.UseModelCustomizerForMigrations(); // Applies custom model customization only during EF Core migrations
        efOpt.CancelMissedTickersOnAppStart();// Useful in distributed mode
    }); // Enables EF-backed storage
});

HttpClientHandler httpClientHandler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message,
                                                 cert,
                                                 chain,
                                                 errors) => {
        return true;
    }
};
HttpClient httpClient = new HttpClient(httpClientHandler);

ServiceConfiguration baseConfiguration = TickerFunctions.BuildBaseConfiguration();
builder.Services.AddSingleton(baseConfiguration);

builder.Services.AddSingleton(httpClient);
builder.Services.AddSingleton<ISecurityServiceClient, SecurityServiceClient>();
//builder.Services.AddSingleton<IMessagingServiceClient, MessagingServiceClient>();
builder.Services.AddSingleton<ITransactionProcessorClient, TransactionProcessorClient>();
builder.Services.AddSingleton<Func<String, String>>(container => (serviceName) =>
{
    return serviceName switch
    {
        "SecurityService" => baseConfiguration.SecurityService,
        "FileProcessorApi" => baseConfiguration.FileProcessorApi,
        "TestHostApi" => baseConfiguration.TestHostApi,
        "TransactionProcessorApi" => baseConfiguration.TransactionProcessorApi,
        _ => throw new NotSupportedException($"Service name {serviceName} not supported")
    };
});


// Clear default providers if you want
builder.Logging.ClearProviders();

// Add NLog
builder.Host.UseNLog().ConfigureAppConfiguration((hostingContext, config) =>
{
    config.AddJsonFile("hosting.json", optional: true, reloadOnChange: true);
});

String nlogConfigFilename = "nlog.config";

WebApplication app = builder.Build();

if (builder.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();

    string directoryPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    LogManager.AddHiddenAssembly(Assembly.LoadFrom(Path.Combine(directoryPath, "Shared.dll")));

    var developmentNlogConfigFilename = "nlog.development.config";
    var developmentConfigPath = Path.Combine(builder.Environment.ContentRootPath, developmentNlogConfigFilename);

    if (File.Exists(developmentConfigPath))
    {
        nlogConfigFilename = developmentNlogConfigFilename;
    }
}
else
{
    LogManager.AddHiddenAssembly(Assembly.LoadFrom(Path.Combine(builder.Environment.ContentRootPath, "Shared.dll")));
}


string configPath = Path.Combine(builder.Environment.ContentRootPath, nlogConfigFilename);

// Correct way to load config with auto-reload support
LogManager.Configuration = new XmlLoggingConfiguration(configPath);

// Get the ILoggerFactory from DI
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

ILogger logger = loggerFactory.CreateLogger("SchedulerService");

Shared.Logger.Logger.Initialise(logger);
//Shared.Logger.Logger.LogWarning("Test Log");

// Run migrations automatically
using IServiceScope scope = app.Services.CreateScope();
SchedulerContext db = scope.ServiceProvider.GetRequiredService<SchedulerContext>();
db.Database.Migrate();

app.UseTickerQ();

app.UseAuthorization();

app.MapControllers();

app.Run();

//public class JobConfig {
//    public String Name { get; set; }
//}

//public class NotificationJobs
//{
//    [TickerFunction(functionName: "SendWelcome")]
//    public Task SendWelcome(TickerFunctionContext<JobConfig> tickerContext, CancellationToken ct) {
//        var clientId = ConfigurationReader.GetValueOrDefault<String>("BaseConfiguration", "ClientId", "");
//        Console.WriteLine(clientId);
//        Console.WriteLine(tickerContext.Request); // Output: User123
//        return Task.CompletedTask;
//    }
//}