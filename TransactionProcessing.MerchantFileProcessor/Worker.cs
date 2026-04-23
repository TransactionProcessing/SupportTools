using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessing.MerchantFileProcessor.Services;

namespace TransactionProcessing.MerchantFileProcessor;

public sealed class Worker(MerchantProcessingOptions options,
                           IMerchantProcessingService merchantProcessingService,
                           IFileStatusStore fileStatusStore) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        LocalLogger.Context logger = LocalLogger.For();
        logger.LogInformation($"Merchant processor started with {options.Merchants.Count} merchant schedules");

        while (!stoppingToken.IsCancellationRequested) {
            MerchantOptions[] enabledMerchants = options.Merchants.Where(merchant => merchant.Enabled).ToArray();

            if (enabledMerchants.Length == 0) {
                logger.LogWarning("No enabled merchants are configured for processing. Waiting before checking again.");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;
            ScheduledMerchant scheduledMerchant = await this.GetNextScheduledMerchantAsync(now, enabledMerchants, stoppingToken);
            DateTimeOffset nextRun = scheduledMerchant.TriggerUtc;
            TimeSpan delay = nextRun - now;

            LocalLogger.Context merchantLogger = LocalLogger.For(scheduledMerchant.Merchant.MerchantId);
            merchantLogger.LogInformation($"Next merchant processing run scheduled at {nextRun:O} for slot {scheduledMerchant.ScheduledRunUtc:O}");

            if (delay > TimeSpan.Zero) {
                await Task.Delay(delay, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested) {
                break;
            }

            Guid? runId = null;

            try {
                runId = Guid.NewGuid();
                Guid currentRunId = runId.Value;

                merchantLogger.WithRun(currentRunId).LogInformation("Starting merchant processing run");

                await merchantProcessingService.ProcessAsync(scheduledMerchant.Merchant, scheduledMerchant.ScheduledRunUtc, currentRunId, stoppingToken);

                merchantLogger.WithRun(currentRunId).LogInformation("Merchant processing run completed successfully");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                break;
            }
            catch (Exception ex) {
                merchantLogger.WithRun(runId).LogError(runId is null ? "Merchant processing run failed before a run identifier was assigned." : "Merchant processing run failed", ex);
            }
        }
    }

    private async Task<ScheduledMerchant> GetNextScheduledMerchantAsync(DateTimeOffset now,
                                                                        IReadOnlyList<MerchantOptions> merchants,
                                                                        CancellationToken cancellationToken) {
        List<ScheduledMerchant> scheduledMerchants = new List<ScheduledMerchant>(merchants.Count);

        foreach (MerchantOptions merchant in merchants) {
            ScheduledMerchant nextRun = await this.GetNextRunUtcAsync(now, merchant, cancellationToken);
            scheduledMerchants.Add(nextRun);
        }

        return scheduledMerchants.OrderBy(candidate => candidate.TriggerUtc).ThenBy(candidate => candidate.Merchant.MerchantId, StringComparer.OrdinalIgnoreCase).First();
    }

    private async Task<ScheduledMerchant> GetNextRunUtcAsync(DateTimeOffset now,
                                                             MerchantOptions merchant,
                                                             CancellationToken cancellationToken) {
        IReadOnlyList<TimeOnly> runTimes = merchant.GetDailyRunTimesUtc();
        DateOnly currentDate = DateOnly.FromDateTime(now.UtcDateTime);

        for (Int32 dayOffset = 0; dayOffset <= 1; dayOffset++) {
            DateOnly candidateDate = currentDate.AddDays(dayOffset);

            foreach (TimeOnly runTime in runTimes) {
                DateTimeOffset scheduledRunUtc = new DateTimeOffset(candidateDate.Year, candidateDate.Month, candidateDate.Day, runTime.Hour, runTime.Minute, runTime.Second, TimeSpan.Zero);

                if (scheduledRunUtc <= now) {
                    Boolean isComplete = await fileStatusStore.IsMerchantRunCompleteAsync(merchant, scheduledRunUtc, cancellationToken);
                    if (!isComplete) {
                        LocalLogger.For(merchant.MerchantId).LogInformation($"Missed scheduled run at {scheduledRunUtc:O}. Scheduling an immediate catch-up run.");
                        return new ScheduledMerchant(merchant, scheduledRunUtc, now);
                    }

                    continue;
                }

                return new ScheduledMerchant(merchant, scheduledRunUtc, scheduledRunUtc);
            }
        }

        TimeOnly firstRunTime = runTimes[0];
        DateOnly nextDate = currentDate.AddDays(1);
        DateTimeOffset nextScheduledRunUtc = new DateTimeOffset(nextDate.Year, nextDate.Month, nextDate.Day, firstRunTime.Hour, firstRunTime.Minute, firstRunTime.Second, TimeSpan.Zero);

        return new ScheduledMerchant(merchant, nextScheduledRunUtc, nextScheduledRunUtc);
    }

    private sealed record ScheduledMerchant(MerchantOptions Merchant, DateTimeOffset ScheduledRunUtc, DateTimeOffset TriggerUtc);
}
