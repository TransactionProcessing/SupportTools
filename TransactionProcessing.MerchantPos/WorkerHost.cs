using Shared.Logger;
using TransactionProcessing.MerchantPos.Runtime;

public class WorkerHost : BackgroundService
{
    private readonly IServiceProvider ServiceProvider;
    private readonly TransactionProcessing.MerchantPos.Persistence.MerchantPosSettingsStore SettingsStore;

    public WorkerHost(IServiceProvider serviceProvider, TransactionProcessing.MerchantPos.Persistence.MerchantPosSettingsStore settingsStore)
    {
        this.ServiceProvider = serviceProvider;
        SettingsStore = settingsStore;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = SettingsStore.Current.WorkerSettings;
        Logger.LogInformation($"WorkerHost starting; Merchant count: {settings.Merchants.Count}");

        foreach (MerchantConfig m in settings.Merchants)
        {
            _ = StartMerchantWorker((settings.ServiceClientId, settings.ServiceClientSecret), (settings.ClientId, settings.ClientSecret),
                m, stoppingToken);
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task StartMerchantWorker((String clientId, String clientSecret) serviceClient, (String clientId, String clientSecret) posClient, MerchantConfig merchant, CancellationToken token)
    {
        MerchantRuntime runtime = this.ServiceProvider
            .GetRequiredService<IMerchantRuntimeFactory>()
            .Create(merchant);

        _ = Task.Run(() => runtime.RunAsync(serviceClient, posClient, merchant, token), token);
    }
}
