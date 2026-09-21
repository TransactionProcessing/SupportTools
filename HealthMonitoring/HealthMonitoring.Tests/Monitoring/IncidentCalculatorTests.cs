using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class IncidentCalculatorTests
{
    [Fact]
    public async Task Degraded_opens_and_healthy_closes_one_incident()
    {
        var repository = new InMemoryMonitoringRepository();
        var calculator = new IncidentCalculator(repository, new FixedClock(DateTimeOffset.Parse("2026-09-18T08:00:00Z")), new NullAlertDispatcher());
        var service = new MonitoredService { Id = Guid.NewGuid(), ServiceId = "service" };

        await calculator.ApplyAsync(service, Observation(service.Id, HealthStatus.Degraded, DateTimeOffset.Parse("2026-09-18T08:00:00Z")), CancellationToken.None);
        await calculator.ApplyAsync(service, Observation(service.Id, HealthStatus.Healthy, DateTimeOffset.Parse("2026-09-18T08:05:00Z")), CancellationToken.None);

        var incident = Assert.Single(repository.Incidents);
        Assert.False(incident.IsOpen);
        Assert.Equal(TimeSpan.FromMinutes(5), incident.Duration);
    }

    private static HealthObservation Observation(Guid serviceId, HealthStatus status, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(), MonitoredServiceId = serviceId, Status = status, ObservedAtUtc = at, AggregateStatus = status.ToString()
    };
}

internal sealed class InMemoryMonitoringRepository : IMonitoringStateRepository
{
    public List<ServiceIncident> Incidents { get; } = [];
    public ServiceStatusSnapshot? Snapshot { get; private set; }
    public ServiceIncident? OpenIncident => Incidents.SingleOrDefault(item => item.IsOpen);
    public Task<ServiceStatusSnapshot?> GetSnapshotAsync(Guid serviceId, CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    public Task SetSnapshotAsync(ServiceStatusSnapshot snapshot, CancellationToken cancellationToken) { Snapshot = snapshot; return Task.CompletedTask; }
    public Task<ServiceIncident?> GetOpenIncidentAsync(Guid serviceId, CancellationToken cancellationToken) => Task.FromResult(OpenIncident);
    public Task AddIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken) { Incidents.Add(incident); return Task.CompletedTask; }
    public Task UpdateIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
internal sealed class NullAlertDispatcher : IAlertDispatcher { public Task DispatchAsync(AlertEvent alertEvent, CancellationToken cancellationToken) => Task.CompletedTask; }
