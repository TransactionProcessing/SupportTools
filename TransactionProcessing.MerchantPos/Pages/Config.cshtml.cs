using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransactionProcessing.MerchantPos.Persistence;
using TransactionProcessing.MerchantPos.Runtime;

namespace TransactionProcessing.MerchantPos.Pages;

public sealed class ConfigModel : PageModel
{
    private const string SecretMask = "*****";
    private readonly MerchantPosSettingsStore _settingsStore;

    public ConfigModel(MerchantPosSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    [BindProperty]
    public ConfigInput Config { get; set; } = new();

    public void OnGet()
    {
        ViewData["ActiveNav"] = "config";
        ViewData["Title"] = "Configuration";
        Config = ConfigInput.FromSnapshot(_settingsStore.Current);
        Config.ClientSecret = MaskSecret(Config.ClientSecret);
        Config.ServiceClientSecret = MaskSecret(Config.ServiceClientSecret);
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var settings = _settingsStore.Current;
        settings.ApiConfiguration.SecurityService = Config.SecurityService;
        settings.ApiConfiguration.TransactionProcessorACL = Config.TransactionProcessorACL;
        settings.ApiConfiguration.TransactionProcessorApi = Config.TransactionProcessorApi;
        settings.ApiConfiguration.TestHost = Config.TestHost;

        settings.WorkerSettings.ClientId = Config.ClientId;
        settings.WorkerSettings.ClientSecret = UnmaskSecret(Config.ClientSecret, settings.WorkerSettings.ClientSecret);
        settings.WorkerSettings.ServiceClientId = Config.ServiceClientId;
        settings.WorkerSettings.ServiceClientSecret = UnmaskSecret(Config.ServiceClientSecret, settings.WorkerSettings.ServiceClientSecret);
        settings.WorkerSettings.MerchantScanIntervalSeconds = Math.Max(1, Config.MerchantScanIntervalSeconds);

        settings.ConnectionStrings.MerchantDb = MerchantPosSettingsStore.NormalizePath(Config.MerchantDb);
        settings.ConnectionStrings.SettingsDb = MerchantPosSettingsStore.NormalizePath(Config.SettingsDb);

        await _settingsStore.SaveAsync(settings);
        return RedirectToPage("/Config", new { message = "Configuration saved" });
    }

    private static string MaskSecret(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : SecretMask;

    private static string UnmaskSecret(string? value, string currentValue)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, SecretMask, StringComparison.Ordinal) ? currentValue : value;

    public sealed class ConfigInput
    {
        public string SecurityService { get; set; } = string.Empty;
        public string TransactionProcessorACL { get; set; } = string.Empty;
        public string TransactionProcessorApi { get; set; } = string.Empty;
        public string TestHost { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string ServiceClientId { get; set; } = string.Empty;
        public string ServiceClientSecret { get; set; } = string.Empty;
        public int MerchantScanIntervalSeconds { get; set; } = 5;
        public string MerchantDb { get; set; } = string.Empty;
        public string SettingsDb { get; set; } = string.Empty;

        public static ConfigInput FromSnapshot(MerchantPosSettingsSnapshot snapshot)
            => new()
            {
                SecurityService = snapshot.ApiConfiguration.SecurityService,
                TransactionProcessorACL = snapshot.ApiConfiguration.TransactionProcessorACL,
                TransactionProcessorApi = snapshot.ApiConfiguration.TransactionProcessorApi,
                TestHost = snapshot.ApiConfiguration.TestHost,
                ClientId = snapshot.WorkerSettings.ClientId,
                ClientSecret = snapshot.WorkerSettings.ClientSecret,
                ServiceClientId = snapshot.WorkerSettings.ServiceClientId,
                ServiceClientSecret = snapshot.WorkerSettings.ServiceClientSecret,
                MerchantScanIntervalSeconds = snapshot.WorkerSettings.MerchantScanIntervalSeconds,
                MerchantDb = MerchantPosSettingsStore.NormalizePath(snapshot.ConnectionStrings.MerchantDb),
                SettingsDb = MerchantPosSettingsStore.NormalizePath(snapshot.ConnectionStrings.SettingsDb)
            };
    }
}
