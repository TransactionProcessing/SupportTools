using System.Text.Json;
using HealthMonitoring.Persistence;

namespace HealthMonitoring.Api;

public static class ServerStatusEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapServerStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/servers/{serverName}/support-checks", async (
            string serverName,
            ServerStatusRequest request,
            IHealthMonitoringRepository repository,
            CancellationToken cancellationToken) =>
        {
            var validation = ServerStatusValidator.Create(serverName, request, DateTimeOffset.UtcNow);
            if (validation.Errors.Count > 0)
                return Results.ValidationProblem(validation.Errors
                    .GroupBy(error => error.Split('.')[0], StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase));

            var existing = await repository.GetServerStatusAsync(validation.Status!.ServerName, cancellationToken);
            var status = validation.Status!;
            status.Id = existing?.Id ?? status.Id;
            var saved = await repository.UpsertServerStatusAsync(status, cancellationToken);
            var checks = JsonSerializer.Deserialize<List<SupportCheckResultRequest>>(saved.ChecksJson, JsonOptions) ?? [];
            var response = new ServerStatusResponse(saved.ServerName, saved.OverallStatus, saved.ReceivedAtUtc, saved.UpdatedAtUtc, checks);

            return existing is null
                ? Results.Created($"/api/servers/{Uri.EscapeDataString(saved.ServerName)}/support-checks", response)
                : Results.Ok(response);
        });

        return endpoints;
    }
}
