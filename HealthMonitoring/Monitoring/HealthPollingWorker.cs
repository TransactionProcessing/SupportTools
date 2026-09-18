using System.Text.Json;
using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;

namespace HealthMonitoring.Monitoring;

public sealed class HealthPollingWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<HealthPollingWorker> logger) : BackgroundService
{
    private readonly Dictionary<Guid, DateTimeOffset> _nextPollAt = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollDueServicesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Health polling cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task PollDueServicesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IHealthMonitoringRepository>();
        var endpointClient = scope.ServiceProvider.GetRequiredService<IHealthEndpointClient>();
        var kurrentDbClient = scope.ServiceProvider.GetRequiredService<KurrentDbMonitorClient>();
        var sqlServerClient = scope.ServiceProvider.GetRequiredService<SqlServerMonitorClient>();
        var calculator = scope.ServiceProvider.GetRequiredService<IncidentCalculator>();
        var services = await repository.ListServicesAsync(false, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var service in services.Where(item => item.IsEnabled && item.ArchivedAtUtc is null))
        {
            if (_nextPollAt.TryGetValue(service.Id, out var nextPoll) && nextPoll > now)
            {
                continue;
            }

            _nextPollAt[service.Id] = now.Add(service.PollingInterval);
            var result = service.MonitorType switch
            {
                MonitorType.KurrentDb => await kurrentDbClient.CheckAsync(service, cancellationToken),
                MonitorType.SqlServer => await sqlServerClient.CheckAsync(service, cancellationToken),
                _ => await endpointClient.CheckAsync(service, cancellationToken)
            };
            var observation = ToObservation(service, result, now);
            await repository.AddObservationAsync(observation, cancellationToken);
            await calculator.ApplyAsync(service, observation, cancellationToken);
        }

        var activeIds = services.Select(service => service.Id).ToHashSet();
        foreach (var staleId in _nextPollAt.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            _nextPollAt.Remove(staleId);
        }
    }

    private static HealthObservation ToObservation(MonitoredService service, HealthEndpointResult result, DateTimeOffset observedAtUtc) => new()
    {
        Id = Guid.NewGuid(),
        MonitoredServiceId = service.Id,
        ObservedAtUtc = observedAtUtc,
        Status = result.Normalized.Status,
        AggregateStatus = result.Normalized.AggregateStatus,
        HttpStatusCode = (int?)result.HttpStatusCode,
        ResponseDuration = result.Duration,
        RawResponseJson = result.RawResponse,
        Error = result.Normalized.Error,
        Checks = result.Normalized.Checks.Select(check => new HealthCheckResult
        {
            Id = Guid.NewGuid(),
            Name = check.Name,
            Status = check.Status,
            Description = check.Description,
            Duration = check.Duration,
            Exception = check.Exception,
            DataJson = JsonSerializer.Serialize(check.Data)
        }).ToList()
    };
}
