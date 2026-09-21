using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;
using HealthMonitoring.Persistence;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HealthMonitoring.Api;

public static class ServiceRegistrationEndpoints
{
    public static IEndpointRouteBuilder MapServiceRegistrationEndpoints(this IEndpointRouteBuilder endpoints, string dashboardEnvironment)
    {
        MapRegistrationEndpoint(endpoints, dashboardEnvironment);
        MapListEndpoint(endpoints);
        MapGetEndpoint(endpoints);
        MapUpdateEndpoint(endpoints, dashboardEnvironment);
        MapArchiveEndpoint(endpoints);

        return endpoints;
    }

    private static void MapRegistrationEndpoint(IEndpointRouteBuilder endpoints, string dashboardEnvironment) =>
        endpoints.MapPost("/api/services/register", async (ServiceRegistrationRequest request, IHealthMonitoringRepository repository, ILoggerFactory loggerFactory, SqlServerRegistrationVersionResolver versionResolver, CancellationToken cancellationToken) =>
        {
            loggerFactory.CreateLogger("HealthMonitoring.Api.ServiceRegistrationEndpoints").LogInformation(
                "POST /api/services/register body: {RequestBody}",
                ServiceRegistrationLogFormatter.Serialize(request));

            var errors = ServiceConfigurationValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors.ToDictionary(error => error, error => new[] { error }));

            var existing = await repository.GetServiceAsync(request.ServiceId, cancellationToken);
            var service = ServiceConfigurationValidator.ToService(request, dashboardEnvironment, existing, preserveManagedSettings: true);
            if (request.MonitorType == MonitorType.SqlServer)
                service.Version = await versionResolver.ResolveAsync(service, request.Version, cancellationToken);
            await repository.UpsertServiceAsync(service, cancellationToken);
            var action = existing is null ? "created" : "acknowledged";
            return existing is null
                ? Results.Created($"/api/services/{service.ServiceId}", new ServiceRegistrationResponse(service.ServiceId, action, service))
                : Results.Ok(new ServiceRegistrationResponse(service.ServiceId, action, service));
        });

    private static void MapListEndpoint(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/services", async (IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var services = await repository.ListServicesAsync(false, cancellationToken);
            var results = new List<ServiceSummary>();
            foreach (var service in services)
            {
                var snapshot = await repository.GetSnapshotAsync(service.Id, cancellationToken);
                results.Add(new ServiceSummary(service.Id, service.ServiceId, service.Name, service.Environment, service.MonitorType, service.HealthUrl?.ToString(), service.IsEnabled, snapshot?.Status ?? HealthStatus.Unknown, snapshot?.LastObservedAtUtc, snapshot?.LastResponseDuration, snapshot?.LastError));
            }

            return Results.Ok(results);
        });

    private static void MapGetEndpoint(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/services/{serviceId}", async (string serviceId, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var service = await repository.GetServiceAsync(serviceId, cancellationToken);
            return service is null || service.ArchivedAtUtc is not null ? Results.NotFound() : Results.Ok(service);
        });

    private static void MapUpdateEndpoint(IEndpointRouteBuilder endpoints, string dashboardEnvironment) =>
        endpoints.MapPut("/api/services/{serviceId}", async (string serviceId, ServiceRegistrationRequest request, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            if (!string.Equals(serviceId, request.ServiceId, StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Route serviceId must match request ServiceId.");
            var errors = ServiceConfigurationValidator.Validate(request);
            if (errors.Count > 0) return Results.ValidationProblem(errors.ToDictionary(error => error, error => new[] { error }));
            var existing = await repository.GetServiceAsync(serviceId, cancellationToken);
            if (existing is null) return Results.NotFound();
            var service = ServiceConfigurationValidator.ToService(request, dashboardEnvironment, existing, preserveManagedSettings: false);
            await repository.UpsertServiceAsync(service, cancellationToken);
            return Results.Ok(service);
        });

    private static void MapArchiveEndpoint(IEndpointRouteBuilder endpoints) =>
        endpoints.MapDelete("/api/services/{serviceId}", async (string serviceId, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            await repository.ArchiveServiceAsync(serviceId, DateTimeOffset.UtcNow, cancellationToken);
            return Results.NoContent();
        });
}

public static class ServiceRegistrationLogFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize(ServiceRegistrationRequest request) =>
        JsonSerializer.Serialize(new
        {
            request.ServiceId,
            request.Name,
            request.HealthUrl,
            request.MonitorType,
            ConnectionString = string.IsNullOrWhiteSpace(request.ConnectionString)
                ? request.ConnectionString
                : "[REDACTED]",
            request.IgnoreCertificateErrors,
            request.PollingIntervalSeconds,
            request.RequestTimeoutSeconds,
            request.RetentionDays,
            request.StatusPolicy,
            request.Group,
            request.Description,
            request.Version,
            request.Host
        }, JsonOptions);
}
