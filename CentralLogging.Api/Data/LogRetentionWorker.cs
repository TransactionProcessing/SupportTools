using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CentralLogging.Api.Data;

public sealed class LogRetentionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<LogRetentionOptions> options,
    ILogger<LogRetentionWorker> logger) : BackgroundService
{
    private readonly LogRetentionOptions Settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (this.Settings.Enabled)
                await this.PurgeExpiredEventsAsync(stoppingToken);

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(this.Settings.IntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PurgeExpiredEventsAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow.AddDays(-this.Settings.RetentionDays);
        Int64 totalDeleted = 0;
        Int32 batchNumber = 0;

        logger.LogInformation(
            "Starting log retention run for events before {CutoffUtc} with batch size {BatchSize}.",
            cutoffUtc,
            this.Settings.BatchSize);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Int32 deleted;
                await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
                {
                    CentralLogsDbContext database = scope.ServiceProvider.GetRequiredService<CentralLogsDbContext>();
                    deleted = await database.Database.ExecuteSqlAsync(
                        $"DELETE TOP ({this.Settings.BatchSize}) FROM dbo.LogEvents WHERE TimestampUtc < {cutoffUtc};",
                        cancellationToken);
                }

                if (deleted == 0)
                    break;

                batchNumber++;
                totalDeleted += deleted;
                logger.LogInformation(
                    "Log retention batch {BatchNumber} deleted {DeletedCount} events; {TotalDeletedCount} deleted in this run.",
                    batchNumber,
                    deleted,
                    totalDeleted);
            }

            logger.LogInformation(
                "Completed log retention run. Deleted {TotalDeletedCount} events before {CutoffUtc} in {BatchCount} batches.",
                totalDeleted,
                cutoffUtc,
                batchNumber);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Log retention run stopped after deleting {TotalDeletedCount} events in {BatchCount} batches; the next run can resume from the remaining expired events.",
                totalDeleted,
                batchNumber);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Log retention run failed after deleting {TotalDeletedCount} events in {BatchCount} batches; the next run will retry remaining expired events.",
                totalDeleted,
                batchNumber);
        }
    }
}
