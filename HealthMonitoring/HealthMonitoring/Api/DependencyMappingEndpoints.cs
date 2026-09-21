using HealthMonitoring.Persistence;

namespace HealthMonitoring.Api;

public static class DependencyMappingEndpoints
{
    public static IEndpointRouteBuilder MapDependencyMappingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapListEndpoint(endpoints);
        MapCreateEndpoint(endpoints);
        MapUpdateEndpoint(endpoints);
        MapDeleteEndpoint(endpoints);

        return endpoints;
    }

    private static void MapListEndpoint(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/services/{serviceId}/dependency-links", async (string serviceId, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var service = await repository.GetServiceAsync(serviceId, cancellationToken);
            if (service is null || service.ArchivedAtUtc is not null) return Results.NotFound();

            var links = await repository.ListDependencyLinksAsync(service.Id, cancellationToken);
            var services = await repository.ListServicesAsync(true, cancellationToken);
            return Results.Ok(ToResponses(links, services));
        });

    private static void MapCreateEndpoint(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/services/{serviceId}/dependency-links", async (string serviceId, DependencyLinkRequest request, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var service = await repository.GetServiceAsync(serviceId, cancellationToken);
            if (service is null || service.ArchivedAtUtc is not null) return Results.NotFound();

            var validation = await ValidateAsync(service.Id, request, null, repository, cancellationToken);
            if (validation is not null) return validation;

            var link = new Domain.ServiceDependencyLink
            {
                Id = Guid.NewGuid(),
                MonitoredServiceId = service.Id,
                DependencyName = request.DependencyName.Trim(),
                TargetMonitoredServiceId = request.TargetMonitoredServiceId
            };
            await repository.UpsertDependencyLinkAsync(link, cancellationToken);
            return Results.Created($"/api/services/{serviceId}/dependency-links/{link.Id}", link);
        });

    private static void MapUpdateEndpoint(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPut("/api/services/{serviceId}/dependency-links/{linkId:guid}", async (string serviceId, Guid linkId, DependencyLinkRequest request, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var service = await repository.GetServiceAsync(serviceId, cancellationToken);
            if (service is null || service.ArchivedAtUtc is not null) return Results.NotFound();

            var links = await repository.ListDependencyLinksAsync(service.Id, cancellationToken);
            var existing = links.SingleOrDefault(link => link.Id == linkId);
            if (existing is null) return Results.NotFound();

            var validation = await ValidateAsync(service.Id, request, linkId, repository, cancellationToken);
            if (validation is not null) return validation;

            existing.DependencyName = request.DependencyName.Trim();
            existing.TargetMonitoredServiceId = request.TargetMonitoredServiceId;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await repository.UpsertDependencyLinkAsync(existing, cancellationToken);
            return Results.Ok(existing);
        });

    private static void MapDeleteEndpoint(IEndpointRouteBuilder endpoints) =>
        endpoints.MapDelete("/api/services/{serviceId}/dependency-links/{linkId:guid}", async (string serviceId, Guid linkId, IHealthMonitoringRepository repository, CancellationToken cancellationToken) =>
        {
            var service = await repository.GetServiceAsync(serviceId, cancellationToken);
            if (service is null || service.ArchivedAtUtc is not null) return Results.NotFound();

            var links = await repository.ListDependencyLinksAsync(service.Id, cancellationToken);
            if (links.All(link => link.Id != linkId)) return Results.NotFound();

            await repository.DeleteDependencyLinkAsync(linkId, cancellationToken);
            return Results.NoContent();
        });

    private static async Task<IResult?> ValidateAsync(Guid parentId, DependencyLinkRequest request, Guid? currentLinkId, IHealthMonitoringRepository repository, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(request.DependencyName)) errors["DependencyName"] = ["Dependency name is required."];
        if (request.TargetMonitoredServiceId == parentId) errors["TargetMonitoredServiceId"] = ["A service cannot link to itself."];

        var target = await repository.ListServicesAsync(false, cancellationToken);
        if (target.All(service => service.Id != request.TargetMonitoredServiceId)) errors["TargetMonitoredServiceId"] = ["Target service must be an active monitored service."];

        var links = await repository.ListDependencyLinksAsync(parentId, cancellationToken);
        if (links.Any(link => link.Id != currentLinkId && string.Equals(link.DependencyName.Trim(), request.DependencyName.Trim(), StringComparison.OrdinalIgnoreCase)))
            errors["DependencyName"] = ["A mapping for this dependency already exists."];

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static IReadOnlyList<DependencyLinkResponse> ToResponses(IReadOnlyList<Domain.ServiceDependencyLink> links, IReadOnlyList<Domain.MonitoredService> services) =>
        links.Join(services, link => link.TargetMonitoredServiceId, service => service.Id, (link, service) => new DependencyLinkResponse(link.Id, link.DependencyName, link.TargetMonitoredServiceId, service.ServiceId, service.Name)).ToArray();
}
