using HealthMonitoring.Domain;

namespace HealthMonitoring.Monitoring;

public interface IMonitoringStateRepository
{
    Task<ServiceStatusSnapshot?> GetSnapshotAsync(Guid serviceId, CancellationToken cancellationToken);
    Task SetSnapshotAsync(ServiceStatusSnapshot snapshot, CancellationToken cancellationToken);
    Task<ServiceIncident?> GetOpenIncidentAsync(Guid serviceId, CancellationToken cancellationToken);
    Task AddIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken);
    Task UpdateIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken);
}

public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed record AlertEvent(Guid ServiceId, HealthStatus PreviousStatus, HealthStatus CurrentStatus, Guid IncidentId, bool Recovered);
public interface IAlertDispatcher { Task DispatchAsync(AlertEvent alertEvent, CancellationToken cancellationToken); }
public sealed class LoggingAlertDispatcher(ILogger<LoggingAlertDispatcher> logger) : IAlertDispatcher
{
    public Task DispatchAsync(AlertEvent alertEvent, CancellationToken cancellationToken)
    {
        logger.LogInformation("Health incident transition for {ServiceId}: {PreviousStatus} -> {CurrentStatus}; recovered={Recovered}", alertEvent.ServiceId, alertEvent.PreviousStatus, alertEvent.CurrentStatus, alertEvent.Recovered);
        return Task.CompletedTask;
    }
}

public sealed class IncidentCalculator(IMonitoringStateRepository repository, IClock clock, IAlertDispatcher alertDispatcher)
{
    public async Task ApplyAsync(MonitoredService service, HealthObservation observation, CancellationToken cancellationToken)
    {
        var snapshot = await repository.GetSnapshotAsync(service.Id, cancellationToken) ?? new ServiceStatusSnapshot { MonitoredServiceId = service.Id };
        var previousStatus = snapshot.Status;
        var openIncident = await repository.GetOpenIncidentAsync(service.Id, cancellationToken);

        if (observation.Status is HealthStatus.Degraded or HealthStatus.Unhealthy)
        {
            if (openIncident is null)
            {
                openIncident = new ServiceIncident
                {
                    Id = Guid.NewGuid(),
                    MonitoredServiceId = service.Id,
                    StartedAtUtc = observation.ObservedAtUtc == default ? clock.UtcNow : observation.ObservedAtUtc,
                    Severity = observation.Status,
                    FirstObservationId = observation.Id,
                    LastObservationId = observation.Id
                };
                await repository.AddIncidentAsync(openIncident, cancellationToken);
                await alertDispatcher.DispatchAsync(new AlertEvent(service.Id, previousStatus, observation.Status, openIncident.Id, false), cancellationToken);
            }
            else
            {
                openIncident.Severity = observation.Status > openIncident.Severity ? observation.Status : openIncident.Severity;
                openIncident.LastObservationId = observation.Id;
                await repository.UpdateIncidentAsync(openIncident, cancellationToken);
            }
        }
        else if (observation.Status == HealthStatus.Healthy && openIncident is not null)
        {
            openIncident.EndedAtUtc = observation.ObservedAtUtc == default ? clock.UtcNow : observation.ObservedAtUtc;
            openIncident.LastObservationId = observation.Id;
            await repository.UpdateIncidentAsync(openIncident, cancellationToken);
            await alertDispatcher.DispatchAsync(new AlertEvent(service.Id, previousStatus, observation.Status, openIncident.Id, true), cancellationToken);
        }

        snapshot.Status = observation.Status;
        snapshot.LastObservedAtUtc = observation.ObservedAtUtc;
        snapshot.LastResponseDuration = observation.ResponseDuration;
        snapshot.LastError = observation.Error;
        snapshot.LastObservationId = observation.Id;
        snapshot.CurrentIncidentId = openIncident?.IsOpen == true ? openIncident.Id : null;
        await repository.SetSnapshotAsync(snapshot, cancellationToken);
    }
}
