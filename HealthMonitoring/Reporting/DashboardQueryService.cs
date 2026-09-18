using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthMonitoring.Reporting;

public interface IDashboardQueryService
{
    Task<DashboardOverview> GetOverviewAsync(TimeSpan range, CancellationToken cancellationToken);
    Task<ServiceDetailModel?> GetServiceDetailAsync(string serviceId, TimeSpan range, CancellationToken cancellationToken);
}

public sealed class DashboardQueryService(HealthMonitoringDbContext dbContext) : IDashboardQueryService
{
    public async Task<DashboardOverview> GetOverviewAsync(TimeSpan range, CancellationToken cancellationToken)
    {
        var services = await dbContext.MonitoredServices.AsNoTracking().Where(service => service.ArchivedAtUtc == null).ToListAsync(cancellationToken);
        var from = DateTimeOffset.UtcNow.Subtract(range);
        var rows = new List<ServiceDashboardRow>();
        foreach (var service in services)
        {
            var snapshot = await dbContext.ServiceStatusSnapshots.AsNoTracking().SingleOrDefaultAsync(item => item.MonitoredServiceId == service.Id, cancellationToken);
            var observations = await dbContext.HealthObservations.AsNoTracking().Where(item => item.MonitoredServiceId == service.Id && item.ObservedAtUtc >= from).ToListAsync(cancellationToken);
            rows.Add(ToRow(service, snapshot, observations));
        }

        var incidents = await dbContext.ServiceIncidents.AsNoTracking()
            .Where(incident => incident.EndedAtUtc == null)
            .CountAsync(incident => dbContext.MonitoredServices.Any(service => service.Id == incident.MonitoredServiceId && service.ArchivedAtUtc == null), cancellationToken);
        return new DashboardOverview(rows.OrderBy(row => row.Name).ToArray(), rows.Count(row => row.Status == HealthStatus.Healthy), rows.Count(row => row.Status == HealthStatus.Degraded), rows.Count(row => row.Status == HealthStatus.Unhealthy), incidents);
    }

    public async Task<ServiceDetailModel?> GetServiceDetailAsync(string serviceId, TimeSpan range, CancellationToken cancellationToken)
    {
        var service = await dbContext.MonitoredServices.AsNoTracking().SingleOrDefaultAsync(item => item.ServiceId == serviceId && item.ArchivedAtUtc == null, cancellationToken);
        if (service is null) return null;
        var from = DateTimeOffset.UtcNow.Subtract(range);
        var observations = await dbContext.HealthObservations.AsNoTracking().Include(item => item.Checks).Where(item => item.MonitoredServiceId == service.Id && item.ObservedAtUtc >= from).OrderByDescending(item => item.ObservedAtUtc).ToListAsync(cancellationToken);
        var incidents = await dbContext.ServiceIncidents.AsNoTracking().Where(item => item.MonitoredServiceId == service.Id && (item.EndedAtUtc == null || item.EndedAtUtc >= from)).OrderByDescending(item => item.StartedAtUtc).ToListAsync(cancellationToken);
        var snapshot = await dbContext.ServiceStatusSnapshots.AsNoTracking().SingleOrDefaultAsync(item => item.MonitoredServiceId == service.Id, cancellationToken);
        var checks = observations.SelectMany(item => item.Checks).ToArray();
        var dependencyLinks = await dbContext.ServiceDependencyLinks.AsNoTracking()
            .Where(link => link.MonitoredServiceId == service.Id)
            .Join(dbContext.MonitoredServices.AsNoTracking(), link => link.TargetMonitoredServiceId, target => target.Id, (link, target) => new ResolvedDependencyLink(link.DependencyName, target.ServiceId, target.Name))
            .ToListAsync(cancellationToken);
        return new ServiceDetailModel(service, ToRow(service, snapshot, observations), observations, incidents, checks, dependencyLinks);
    }

    private static ServiceDashboardRow ToRow(MonitoredService service, ServiceStatusSnapshot? snapshot, IReadOnlyList<HealthObservation> observations)
    {
        var total = observations.Count;
        var healthy = observations.Count(observation => observation.Status == HealthStatus.Healthy);
        var uptime = total == 0 ? 0 : healthy * 100d / total;
        return new ServiceDashboardRow(service.ServiceId, service.Name, service.Environment, snapshot?.Status ?? HealthStatus.Unknown, snapshot?.LastResponseDuration, uptime, snapshot?.LastObservedAtUtc, snapshot?.LastError);
    }
}
