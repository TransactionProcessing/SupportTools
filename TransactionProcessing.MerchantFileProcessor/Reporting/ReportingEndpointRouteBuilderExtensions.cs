using TransactionProcessing.MerchantFileProcessor.Configuration;

namespace TransactionProcessing.MerchantFileProcessor.Reporting;

public static class ReportingEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Redirect("/ops"));

        endpoints.MapGet("/api/status", async (IFileStatusReportService reportService, CancellationToken cancellationToken) =>
            Results.Json(await reportService.GetReportAsync(cancellationToken)));

        endpoints.MapGet("/api/configuration", async (IMerchantProcessingConfigurationStore configurationStore, CancellationToken cancellationToken) =>
        {
            var snapshot = await configurationStore.GetCurrentSnapshotAsync(cancellationToken);
            return Results.Content(snapshot.Json, "application/json");
        });

        endpoints.MapPost("/api/configuration", async (HttpRequest request, IMerchantProcessingConfigurationStore configurationStore, CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var configurationJson = form["configurationJson"].ToString();

            try
            {
                await configurationStore.SaveJsonAsync(configurationJson, cancellationToken);
                return Results.Redirect("/ops/config?saved=1");
            }
            catch (Exception)
            {
                return Results.Redirect($"/ops/config?error={Uri.EscapeDataString("Configuration save failed.")}");
            }
        });

        return endpoints;
    }
}
