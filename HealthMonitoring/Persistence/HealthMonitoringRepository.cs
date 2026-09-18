using System.Text.Json;
using HealthMonitoring.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthMonitoring.Persistence;

public sealed class HealthMonitoringRepository(HealthMonitoringDbContext dbContext) : IHealthMonitoringRepository, HealthMonitoring.Monitoring.IMonitoringStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MonitoredService?> GetServiceAsync(string serviceId, CancellationToken cancellationToken)
    {
        var service = await dbContext.MonitoredServices.SingleOrDefaultAsync(item => item.ServiceId == serviceId, cancellationToken);
        return HydratePolicy(service);
    }

    public async Task<IReadOnlyList<MonitoredService>> ListServicesAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var query = dbContext.MonitoredServices.AsNoTracking();
        if (!includeArchived)
        {
            query = query.Where(service => service.ArchivedAtUtc == null);
        }

        var services = await query.OrderBy(service => service.Name).ToListAsync(cancellationToken);
        return services.Select(HydratePolicy).ToArray()!;
    }

    public async Task<MonitoredService> UpsertServiceAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        service.StatusPolicyJson = JsonSerializer.Serialize(service.StatusPolicy, JsonOptions);
        var existing = await dbContext.MonitoredServices.SingleOrDefaultAsync(item => item.ServiceId == service.ServiceId, cancellationToken);
        if (existing is null)
        {
            dbContext.MonitoredServices.Add(service);
        }
        else
        {
            existing.Name = service.Name;
            existing.Environment = service.Environment;
            existing.Group = service.Group;
            existing.Description = service.Description;
            existing.HealthUrl = service.HealthUrl;
            existing.IsEnabled = service.IsEnabled;
            existing.PollingInterval = service.PollingInterval;
            existing.RequestTimeout = service.RequestTimeout;
            existing.RetentionPeriod = service.RetentionPeriod;
            existing.StatusPolicyJson = service.StatusPolicyJson;
            existing.Version = service.Version;
            existing.Host = service.Host;
            existing.LastRegisteredAtUtc = service.LastRegisteredAtUtc;
            existing.UpdatedAtUtc = service.UpdatedAtUtc;
            service = existing;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return HydratePolicy(service)!;
    }

    public async Task ArchiveServiceAsync(string serviceId, DateTimeOffset archivedAtUtc, CancellationToken cancellationToken)
    {
        var service = await dbContext.MonitoredServices.SingleOrDefaultAsync(item => item.ServiceId == serviceId, cancellationToken);
        if (service is null)
        {
            return;
        }

        service.IsEnabled = false;
        service.ArchivedAtUtc = archivedAtUtc;
        service.UpdatedAtUtc = archivedAtUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AddObservationAsync(HealthObservation observation, CancellationToken cancellationToken)
    {
        dbContext.HealthObservations.Add(observation);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HealthObservation>> GetObservationsAsync(Guid serviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        await dbContext.HealthObservations
            .AsNoTracking()
            .Include(observation => observation.Checks)
            .Where(observation => observation.MonitoredServiceId == serviceId && observation.ObservedAtUtc >= fromUtc && observation.ObservedAtUtc <= toUtc)
            .OrderByDescending(observation => observation.ObservedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<ServiceStatusSnapshot?> GetSnapshotAsync(Guid serviceId, CancellationToken cancellationToken) =>
        dbContext.ServiceStatusSnapshots.AsNoTracking().SingleOrDefaultAsync(snapshot => snapshot.MonitoredServiceId == serviceId, cancellationToken);

    public async Task UpsertSnapshotAsync(ServiceStatusSnapshot snapshot, CancellationToken cancellationToken)
    {
        var existing = await dbContext.ServiceStatusSnapshots.SingleOrDefaultAsync(item => item.MonitoredServiceId == snapshot.MonitoredServiceId, cancellationToken);
        if (existing is null)
        {
            dbContext.ServiceStatusSnapshots.Add(snapshot);
        }
        else
        {
            dbContext.Entry(existing).CurrentValues.SetValues(snapshot);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SetSnapshotAsync(ServiceStatusSnapshot snapshot, CancellationToken cancellationToken) => UpsertSnapshotAsync(snapshot, cancellationToken);

    public Task<ServiceIncident?> GetOpenIncidentAsync(Guid serviceId, CancellationToken cancellationToken) =>
        dbContext.ServiceIncidents.SingleOrDefaultAsync(incident => incident.MonitoredServiceId == serviceId && incident.EndedAtUtc == null, cancellationToken);

    public async Task AddIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken)
    {
        dbContext.ServiceIncidents.Add(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken)
    {
        dbContext.ServiceIncidents.Update(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteObservationsBeforeAsync(Guid serviceId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken) =>
        await dbContext.HealthObservations
            .Where(observation => observation.MonitoredServiceId == serviceId && observation.ObservedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

    private static MonitoredService? HydratePolicy(MonitoredService? service)
    {
        if (service is null)
        {
            return null;
        }

        service.StatusPolicy = JsonSerializer.Deserialize<StatusPolicy>(service.StatusPolicyJson, JsonOptions) ?? new StatusPolicy();
        return service;
    }
}
