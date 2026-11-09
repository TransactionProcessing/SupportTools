using System.Security.Cryptography;
using System.Text;
using CentralLogging.Api.Data;
using CentralLogging.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

String? configuredConnectionString = builder.Configuration.GetConnectionString("CentralLogs");
if (String.IsNullOrWhiteSpace(configuredConnectionString))
    throw new InvalidOperationException("Configure ConnectionStrings:CentralLogs before starting the log consumer.");
String connectionString = configuredConnectionString;
String? configuredApiKey = builder.Configuration["LogIngestion:ApiKey"];
if (String.IsNullOrWhiteSpace(configuredApiKey))
    throw new InvalidOperationException("Configure LogIngestion:ApiKey before starting the log consumer.");
String apiKey = configuredApiKey;

builder.Services.AddDbContext<CentralLogsDbContext>(options => options.UseSqlServer(connectionString));
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

await using (AsyncServiceScope scope = app.Services.CreateAsyncScope())
{
    CentralLogsDbContext database = scope.ServiceProvider.GetRequiredService<CentralLogsDbContext>();
    await database.Database.MigrateAsync();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/logs") && !HasValidApiKey(context.Request.Headers, apiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/logs"))
    {
        IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            Int64 maximumBytes = context.Request.Path.StartsWithSegments("/api/logs/upload")
                ? ingestionOptions.MaximumUploadBytes + 64 * 1024
                : ingestionOptions.MaximumRequestBytes;
            sizeFeature.MaxRequestBodySize = maximumBytes;
        }
    }

    await next(context);
});

app.MapGet("/health/ready", async (CentralLogsDbContext database, CancellationToken cancellationToken) =>
    await database.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.Problem("CentralLogs database is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable));
app.MapPost("/api/logs", LogEndpoints.IngestAsync);
app.MapPost("/api/logs/upload", LogEndpoints.UploadAsync).DisableAntiforgery();
app.MapGet("/api/logs", LogEndpoints.SearchAsync);
app.MapGet("/api/logs/fields", LogEndpoints.GetStructuredFieldsAsync);
app.MapGet("/api/logs/services", LogEndpoints.GetServicesAsync);

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
