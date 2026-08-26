using Microsoft.Extensions.Configuration;

namespace TransactionProcessing.MerchantPos.Runtime;

public sealed class MerchantPosSettingsSnapshot
{
    public WorkerSettings WorkerSettings { get; set; } = new();
    public ApiConfigurationSettings ApiConfiguration { get; set; } = new();
    public ConnectionStringsSettings ConnectionStrings { get; set; } = new();

    public static MerchantPosSettingsSnapshot FromConfiguration(IConfiguration configuration)
    {
        var snapshot = new MerchantPosSettingsSnapshot();
        configuration.Bind(snapshot);
        snapshot.EnsureDefaults();
        return snapshot;
    }

    public void EnsureDefaults()
    {
        EnsureWorkerDefaults();
        EnsureApiConfigurationDefaults();
        EnsureConnectionStringDefaults();
    }

    private void EnsureWorkerDefaults()
    {
        WorkerSettings ??= new WorkerSettings();
        WorkerSettings.ClientId ??= string.Empty;
        WorkerSettings.ClientSecret ??= string.Empty;
        WorkerSettings.ServiceClientId ??= string.Empty;
        WorkerSettings.ServiceClientSecret ??= string.Empty;
        WorkerSettings.MerchantScanIntervalSeconds = WorkerSettings.MerchantScanIntervalSeconds <= 0 ? 5 : WorkerSettings.MerchantScanIntervalSeconds;
        WorkerSettings.Merchants ??= new List<MerchantConfig>();
    }

    private void EnsureApiConfigurationDefaults()
    {
        ApiConfiguration ??= new ApiConfigurationSettings();
        ApiConfiguration.SecurityService ??= string.Empty;
        ApiConfiguration.TransactionProcessorACL ??= string.Empty;
        ApiConfiguration.TransactionProcessorApi ??= string.Empty;
        ApiConfiguration.TestHost ??= string.Empty;
    }

    private void EnsureConnectionStringDefaults()
    {
        ConnectionStrings ??= new ConnectionStringsSettings();
        ConnectionStrings.MerchantDb ??= string.Empty;
        ConnectionStrings.SettingsDb ??= string.Empty;
    }
}

public sealed class ApiConfigurationSettings
{
    public string SecurityService { get; set; } = string.Empty;
    public string TransactionProcessorACL { get; set; } = string.Empty;
    public string TransactionProcessorApi { get; set; } = string.Empty;
    public string TestHost { get; set; } = string.Empty;
}

public sealed class ConnectionStringsSettings
{
    public string MerchantDb { get; set; } = string.Empty;
    public string SettingsDb { get; set; } = string.Empty;
}
