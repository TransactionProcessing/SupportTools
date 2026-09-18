using HealthMonitoring.Persistence;

namespace HealthMonitoring.Monitoring;

public sealed class RetentionCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<RetentionCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Health history retention cleanup failed."); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IHealthMonitoringRepository>();
        foreach (var service in await repository.ListServicesAsync(true, cancellationToken))
        {
            await repository.DeleteObservationsBeforeAsync(service.Id, DateTimeOffset.UtcNow.Subtract(service.RetentionPeriod), cancellationToken);
        }
    }
}
