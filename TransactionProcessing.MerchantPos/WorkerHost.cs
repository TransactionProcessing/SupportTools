using System.Collections.Concurrent;
using Shared.Logger;
using TransactionProcessing.MerchantPos.Runtime;

public class WorkerHost : BackgroundService
{
    private readonly IServiceProvider ServiceProvider;
    private readonly TransactionProcessing.MerchantPos.Persistence.MerchantPosSettingsStore SettingsStore;
    private readonly ConcurrentDictionary<Guid, MerchantWorkerState> RunningMerchantWorkers = new();

    public WorkerHost(IServiceProvider serviceProvider, TransactionProcessing.MerchantPos.Persistence.MerchantPosSettingsStore settingsStore)
    {
        this.ServiceProvider = serviceProvider;
        SettingsStore = settingsStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation($"WorkerHost starting; Merchant count: {SettingsStore.Current.WorkerSettings.Merchants.Count}");
        while (!stoppingToken.IsCancellationRequested)
        {
            SyncMerchantWorkers(stoppingToken);

            var scanIntervalSeconds = SettingsStore.Current.WorkerSettings.MerchantScanIntervalSeconds;
            var delay = TimeSpan.FromSeconds(Math.Max(1, scanIntervalSeconds));
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private void SyncMerchantWorkers(CancellationToken hostToken)
    {
        var settings = SettingsStore.Current.WorkerSettings;
        var merchantsById = settings.Merchants.ToDictionary(merchant => merchant.MerchantId);

        foreach (var entry in RunningMerchantWorkers)
        {
            if (!merchantsById.TryGetValue(entry.Key, out var merchant) || !merchant.Enabled)
            {
                StopMerchantWorker(entry.Key, entry.Value);
            }
        }

        foreach (MerchantConfig merchant in settings.Merchants)
        {
            if (!merchant.Enabled)
            {
                continue;
            }

            if (RunningMerchantWorkers.ContainsKey(merchant.MerchantId))
            {
                continue;
            }

            try
            {
                StartMerchantWorker(
                    (settings.ServiceClientId, settings.ServiceClientSecret),
                    (settings.ClientId, settings.ClientSecret),
                    merchant,
                    hostToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to start merchant worker for {merchant.MerchantName} ({merchant.MerchantId})", ex);
            }
        }
    }

    private void StartMerchantWorker((String clientId, String clientSecret) serviceClient, (String clientId, String clientSecret) posClient, MerchantConfig merchant, CancellationToken hostToken)
    {
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(hostToken);
        MerchantRuntime runtime = this.ServiceProvider
            .GetRequiredService<IMerchantRuntimeFactory>()
            .Create(merchant);

        var workerTask = runtime.RunAsync(serviceClient, posClient, merchant, cancellationTokenSource.Token);
        var workerState = new MerchantWorkerState(cancellationTokenSource, workerTask, merchant.MerchantName);

        if (!RunningMerchantWorkers.TryAdd(merchant.MerchantId, workerState))
        {
            cancellationTokenSource.Cancel();
            cancellationTokenSource.Dispose();
            return;
        }

        Logger.LogInformation($"Starting merchant worker for {merchant.MerchantName} ({merchant.MerchantId})");
        _ = MonitorMerchantWorkerAsync(merchant.MerchantId, workerState);
    }

    private void StopMerchantWorker(Guid merchantId, MerchantWorkerState workerState)
    {
        if (!RunningMerchantWorkers.TryRemove(merchantId, out _))
        {
            return;
        }

        Logger.LogInformation($"Stopping merchant worker for {workerState.MerchantName} ({merchantId})");
        workerState.CancellationTokenSource.Cancel();
    }

    private async Task MonitorMerchantWorkerAsync(Guid merchantId, MerchantWorkerState workerState)
    {
        try
        {
            await workerState.WorkerTask;
        }
        catch (OperationCanceledException) when (workerState.CancellationTokenSource.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogError($"Merchant worker for {workerState.MerchantName} ({merchantId}) failed.", ex);
        }
        finally
        {
            RunningMerchantWorkers.TryRemove(merchantId, out _);
            workerState.CancellationTokenSource.Dispose();
        }
    }

    private sealed record MerchantWorkerState(CancellationTokenSource CancellationTokenSource, Task WorkerTask, string MerchantName);
}
