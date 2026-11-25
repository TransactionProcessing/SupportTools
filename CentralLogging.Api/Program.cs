using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using CentralLogging.Api.Data;
using CentralLogging.Api.Endpoints;
using CentralLogging.Shared;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
    options.UseUtcTimestamp = true;
});
FileInfo assemblyFile = new(Assembly.GetExecutingAssembly().Location);
String assemblyDirectory = assemblyFile.Directory!.FullName;
String traceDirectory = Environment.GetEnvironmentVariable("CENTRAL_LOGGING_TRACE_DIRECTORY")
    ?? (OperatingSystem.IsWindows() ? @"C:\home\txnproc\trace" : "/home/txnproc/trace");
builder.Logging.AddProvider(new TraceFileLoggerProvider(traceDirectory, "central-logging-api"));
String externalConfigDirectory = OperatingSystem.IsWindows()
    ? @"C:\home\txnproc\config"
    : "/home/txnproc/config";
builder.Configuration
    .SetBasePath(assemblyDirectory)
    .AddJsonFile("hosting.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"hosting.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(externalConfigDirectory, "appsettings.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(externalConfigDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(externalConfigDirectory, "appsettings.local.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Central Logging API");
String listenUrls = builder.Configuration["Urls"] ?? "http://127.0.0.1:7100";
builder.WebHost.UseUrls(listenUrls);

String? configuredConnectionString = builder.Configuration.GetConnectionString("CentralLogs");
if (String.IsNullOrWhiteSpace(configuredConnectionString))
    throw new InvalidOperationException("Configure ConnectionStrings:CentralLogs before starting the log consumer.");
String connectionString = configuredConnectionString;
String? configuredApiKey = builder.Configuration["LogIngestion:ApiKey"];
if (String.IsNullOrWhiteSpace(configuredApiKey))
    throw new InvalidOperationException("Configure LogIngestion:ApiKey before starting the log consumer.");
String apiKey = configuredApiKey;

builder.Services.AddDbContext<CentralLogsDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddOptions<LogRetentionOptions>()
    .Bind(builder.Configuration.GetSection(LogRetentionOptions.SectionName))
    .Validate(options => options.RetentionDays > 0, "Log retention days must be greater than zero.")
    .Validate(options => options.BatchSize is > 0 and <= 5_000, "Log retention batch size must be between 1 and 5000.")
    .Validate(options => options.IntervalMinutes > 0, "Log retention interval must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddHostedService<LogRetentionWorker>();
builder.Services.AddOptions<LogIngestionOptions>()
    .Bind(builder.Configuration.GetSection(LogIngestionOptions.SectionName))
    .Validate(options => options.MaximumRequestBytes > 0 && options.MaximumUploadBytes > 0 && options.MaximumLineBytes > 0,
        "Log ingestion size limits must be greater than zero.")
    .ValidateOnStart();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = (builder.Configuration.GetValue<Int64?>("LogIngestion:MaximumUploadBytes") ?? 100 * 1024 * 1024) + 64 * 1024;
});
WebApplication app = builder.Build();
LogIngestionOptions ingestionOptions = app.Services.GetRequiredService<IOptions<LogIngestionOptions>>().Value;

app.Logger.LogInformation("Applying Central Logging database migrations.");
try
{
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    CentralLogsDbContext database = scope.ServiceProvider.GetRequiredService<CentralLogsDbContext>();
    await database.Database.MigrateAsync();
    app.Logger.LogInformation("Central Logging database migrations completed.");
}
catch (Exception exception)
{
    app.Logger.LogCritical(exception, "Central Logging database migrations failed; API startup is stopping.");
    throw;
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next(context);
        return;
    }

    Stopwatch stopwatch = Stopwatch.StartNew();
    try
    {
        await next(context);
    }
    catch (Exception exception)
    {
        stopwatch.Stop();
        app.Logger.LogError(exception,
            "API request {Method} {Path} failed after {ElapsedMilliseconds} ms. RequestId {RequestId}.",
            context.Request.Method,
            context.Request.Path,
            stopwatch.ElapsedMilliseconds,
            context.TraceIdentifier);
        throw;
    }

    stopwatch.Stop();
    if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
    {
        app.Logger.LogError(
            "API request {Method} {Path} returned {StatusCode} after {ElapsedMilliseconds} ms. RequestId {RequestId}.",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            context.TraceIdentifier);
    }
    else if (context.Response.StatusCode >= StatusCodes.Status400BadRequest)
    {
        app.Logger.LogWarning(
            "API request {Method} {Path} returned {StatusCode} after {ElapsedMilliseconds} ms. RequestId {RequestId}.",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            context.TraceIdentifier);
    }
    else
    {
        app.Logger.LogInformation(
            "API request {Method} {Path} returned {StatusCode} after {ElapsedMilliseconds} ms. RequestId {RequestId}.",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds,
            context.TraceIdentifier);
    }
});

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/logs") && !HasValidApiKey(context.Request.Headers, apiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    LogRequestSizeLimit.Apply(context, ingestionOptions);

    await next(context);
});

app.MapGet("/health/ready", async (CentralLogsDbContext database, CancellationToken cancellationToken) =>
    await database.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("CentralLogs database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable));
app.MapPost("/api/logs", LogEndpoints.IngestAsync);
app.MapPost("/api/logs/upload", LogEndpoints.UploadAsync).DisableAntiforgery();
app.MapGet("/api/logs", LogEndpoints.SearchAsync);
app.MapGet("/api/logs/page", LogEndpoints.SearchPageAsync);
app.MapGet("/api/logs/export", LogEndpoints.ExportAsync);
app.MapGet("/api/logs/aggregates", LogEndpoints.AggregateAsync);
app.MapGet("/api/logs/fields", LogEndpoints.GetStructuredFieldsAsync);
app.MapGet("/api/logs/services", LogEndpoints.GetServicesAsync);

app.Logger.LogInformation("Central Logging API started in {EnvironmentName}; listening on {ListenUrls}.",
    app.Environment.EnvironmentName,
    listenUrls);

app.Run();

static Boolean HasValidApiKey(IHeaderDictionary headers, String expectedKey)
{
    if (!headers.TryGetValue("X-Log-Api-Key", out Microsoft.Extensions.Primitives.StringValues supplied) || String.IsNullOrEmpty(supplied))
        return false;

    Byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedKey);
    Byte[] suppliedBytes = Encoding.UTF8.GetBytes(supplied.ToString());
    return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
}

public partial class Program
{
    protected Program() { }
}
