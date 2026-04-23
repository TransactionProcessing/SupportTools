namespace TransactionProcessing.MerchantFileProcessor.Reporting;

public static class ReportingEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Redirect("/status"));

        endpoints.MapGet("/api/status", async (IFileStatusReportService reportService, CancellationToken cancellationToken) =>
            Results.Json(await reportService.GetReportAsync(cancellationToken)));

        endpoints.MapGet("/status", async (IFileStatusReportService reportService, CancellationToken cancellationToken) =>
            Results.Content(await reportService.RenderHtmlAsync(cancellationToken), "text/html"));

        endpoints.MapGet("/status/{merchantId}", async (string merchantId, IFileStatusReportService reportService, CancellationToken cancellationToken) =>
        {
            var html = await reportService.RenderMerchantHtmlAsync(merchantId, cancellationToken);
            return html is null
                ? Results.NotFound($"Merchant '{merchantId}' was not found.")
                : Results.Content(html, "text/html");
        });

        endpoints.MapGet("/status/{merchantId}/files/{fileId:long}", async (string merchantId, long fileId, IFileStatusReportService reportService, CancellationToken cancellationToken) =>
        {
            var html = await reportService.RenderFileHtmlAsync(merchantId, fileId, cancellationToken);
            return html is null
                ? Results.NotFound($"File '{fileId}' was not found for merchant '{merchantId}'.")
                : Results.Content(html, "text/html");
        });

        return endpoints;
    }
}
