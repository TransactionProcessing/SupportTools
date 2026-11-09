using System.Security.Cryptography;
using System.Text;
using CentralLogging.Api.Data;
using CentralLogging.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

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
WebApplication app = builder.Build();

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
