using HealthMonitoring.Domain;

namespace HealthMonitoring.Persistence;

public interface IHealthMonitoringRepository
{
    Task<MonitoredService?> GetServiceAsync(string serviceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<MonitoredService>> ListServicesAsync(bool includeArchived, CancellationToken cancellationToken);
    Task<MonitoredService> UpsertServiceAsync(MonitoredService service, CancellationToken cancellationToken);
    Task ArchiveServiceAsync(string serviceId, DateTimeOffset archivedAtUtc, CancellationToken cancellationToken);
    Task AddObservationAsync(HealthObservation observation, CancellationToken cancellationToken);
    Task<IReadOnlyList<HealthObservation>> GetObservationsAsync(Guid serviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);
    Task<ServiceStatusSnapshot?> GetSnapshotAsync(Guid serviceId, CancellationToken cancellationToken);
    Task UpsertSnapshotAsync(ServiceStatusSnapshot snapshot, CancellationToken cancellationToken);
    Task<ServiceIncident?> GetOpenIncidentAsync(Guid serviceId, CancellationToken cancellationToken);
    Task AddIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken);
    Task UpdateIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken);
    Task<int> DeleteObservationsBeforeAsync(Guid serviceId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceDependencyLink>> ListDependencyLinksAsync(Guid serviceId, CancellationToken cancellationToken);
    Task<ServiceDependencyLink> UpsertDependencyLinkAsync(ServiceDependencyLink link, CancellationToken cancellationToken);
    Task DeleteDependencyLinkAsync(Guid linkId, CancellationToken cancellationToken);
}
