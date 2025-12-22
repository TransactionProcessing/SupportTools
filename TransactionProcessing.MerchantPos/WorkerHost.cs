using Shared.Logger;
using TransactionProcessing.MerchantPos.Runtime;

public class WorkerHost : BackgroundService
{
    private readonly IServiceProvider ServiceProvider;
    private readonly WorkerSettings Settings;

    public WorkerHost(IServiceProvider serviceProvider, WorkerSettings settings)
    {
        this.ServiceProvider = serviceProvider;
        Settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger.LogInformation($"WorkerHost starting; Merchant count: {Settings.Merchants.Count}");

        List<Task> tasks = new List<Task>();
        foreach (MerchantConfig m in Settings.Merchants)
        {
            tasks.Add(StartMerchantWorker((this.Settings.ServiceClientId, this.Settings.ServiceClientSecret), (this.Settings.ClientId, this.Settings.ClientSecret),
                m, stoppingToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task StartMerchantWorker((String clientId, String clientSecret) serviceClient, (String clientId, String clientSecret) posClient, MerchantConfig merchant, CancellationToken token)
    {
        MerchantRuntime runtime = this.ServiceProvider
            .GetRequiredService<IMerchantRuntimeFactory>()
            .Create(merchant);

        _ = Task.Run(() => runtime.RunAsync(serviceClient, posClient, merchant, token), token);
    }
}