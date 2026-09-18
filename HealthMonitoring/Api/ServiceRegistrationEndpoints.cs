using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;

namespace HealthMonitoring.Api;

public static class ServiceRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapServiceRegistrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/services/register", async (ServiceRegistrationRequest request, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var errors = ServiceConfigurationValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors.ToDictionary(error => error, error => new[] { error }));

            var existing = await repository.GetServiceAsync(request.ServiceId, cancellationToken);
            var service = ServiceConfigurationValidator.ToService(request, existing, preserveManagedSettings: true);
            await repository.UpsertServiceAsync(service, cancellationToken);
            var action = existing is null ? "created" : "acknowledged";
            return existing is null
                ? Results.Created($"/api/services/{service.ServiceId}", new ServiceRegistrationResponse(service.ServiceId, action, service))
                : Results.Ok(new ServiceRegistrationResponse(service.ServiceId, action, service));
        });

        endpoints.MapGet("/api/services", async (IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var services = await repository.ListServicesAsync(false, cancellationToken);
            var results = new List<ServiceSummary>();
            foreach (var service in services)
            {
                var snapshot = await repository.GetSnapshotAsync(service.Id, cancellationToken);
                results.Add(new ServiceSummary(service.ServiceId, service.Name, service.Environment, service.HealthUrl.ToString(), service.IsEnabled, snapshot?.Status ?? HealthStatus.Unknown, snapshot?.LastObservedAtUtc, snapshot?.LastResponseDuration, snapshot?.LastError));
            }

            return Results.Ok(results);
        });

        endpoints.MapGet("/api/services/{serviceId}", async (string serviceId, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var service = await repository.GetServiceAsync(serviceId, cancellationToken);
            if (service is null || service.ArchivedAtUtc is not null) return Results.NotFound();
            return Results.Ok(service);
        });

        endpoints.MapPut("/api/services/{serviceId}", async (string serviceId, ServiceRegistrationRequest request, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            if (!string.Equals(serviceId, request.ServiceId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route serviceId must match request ServiceId.");
            var errors = ServiceConfigurationValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors.ToDictionary(error => error, error => new[] { error }));
            var existing = await repository.GetServiceAsync(serviceId, cancellationToken);
            if (existing is null) return Results.NotFound();
            var service = ServiceConfigurationValidator.ToService(request, existing, preserveManagedSettings: false);
            await repository.UpsertServiceAsync(service, cancellationToken);
            return Results.Ok(service);
        });

        endpoints.MapDelete("/api/services/{serviceId}", async (string serviceId, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            await repository.ArchiveServiceAsync(serviceId, DateTimeOffset.UtcNow, cancellationToken);
            return Results.NoContent();
        });

        return endpoints;
    }
}
