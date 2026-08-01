using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TransactionProcessing.MerchantPos.Persistence;
using TransactionProcessing.MerchantPos.Runtime;

namespace TransactionProcessing.MerchantPos.Pages;

public sealed class MerchantsModel : PageModel
{
    private const string SecretMask = "*****";
    private readonly MerchantPosSettingsStore _settingsStore;

    public MerchantsModel(MerchantPosSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    [BindProperty(SupportsGet = true)]
    public int Selected { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    [BindProperty]
    public MerchantEditorInput Merchant { get; set; } = new();

    public IReadOnlyList<MerchantConfig> Merchants => _settingsStore.Current.WorkerSettings.Merchants;
    public bool IsDraft => Selected < 0 || Selected >= Merchants.Count;

    public void OnGet()
    {
        ViewData["ActiveNav"] = "merchants";
        ViewData["Title"] = "Merchant Management";
        Selected = Selected < 0 ? -1 : ClampSelected(Selected);
        LoadSelectedMerchant();
        Merchant.Password = MaskSecret(Merchant.Password);
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        return RedirectToPage("/Merchants", new { selected = -1, message = "New merchant ready" });
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var settings = _settingsStore.Current;
        Selected = Selected < 0 ? -1 : ClampSelected(Selected);

        if (Selected < 0 || Selected >= settings.WorkerSettings.Merchants.Count)
        {
            var merchant = CreateNewMerchant();
            ApplyTo(merchant, Merchant);
            settings.WorkerSettings.Merchants.Add(merchant);
            Selected = settings.WorkerSettings.Merchants.Count - 1;
        }
        else
        {
            ApplyTo(settings.WorkerSettings.Merchants[Selected], Merchant);
        }

        await _settingsStore.SaveAsync(settings);
        return RedirectToPage("/Merchants", new { selected = Selected, message = "Merchant saved" });
    }

    public async Task<IActionResult> OnPostRemoveAsync()
    {
        var settings = _settingsStore.Current;
        if (settings.WorkerSettings.Merchants.Count == 0)
        {
            return RedirectToPage("/Merchants", new { message = "No merchant to remove" });
        }

        Selected = ClampSelected(Selected);
        settings.WorkerSettings.Merchants.RemoveAt(Selected);
        await _settingsStore.SaveAsync(settings);

        var selected = Math.Clamp(Selected, 0, Math.Max(0, settings.WorkerSettings.Merchants.Count - 1));
        return RedirectToPage("/Merchants", new { selected, message = "Merchant removed" });
    }

    private void LoadSelectedMerchant()
    {
        if (IsDraft || Merchants.Count == 0)
        {
            Merchant = MerchantEditorInput.FromMerchant(CreateNewMerchant());
            return;
        }

        var merchant = Merchants[Selected];
        Merchant = MerchantEditorInput.FromMerchant(merchant);
    }

    private int ClampSelected(int selected)
        => Merchants.Count == 0 ? 0 : Math.Clamp(selected, 0, Merchants.Count - 1);

    private static MerchantConfig CreateNewMerchant()
        => new()
        {
            MerchantId = Guid.NewGuid(),
            EstateId = Guid.NewGuid(),
            MerchantName = "New merchant",
            Enabled = false,
            ApplicationVersion = "1.0.0",
            DeviceIdentifier = $"merchant-{Guid.NewGuid():N}".Substring(0, 16),
            Username = string.Empty,
            Password = string.Empty,
            SaleIntervalSeconds = 30,
            FailureInjectionProbability = 0.02,
            DepositThreshold = 100,
            DepositAmount = 500,
            OpeningTime = new TimeOnly(8, 0),
            ClosingTime = new TimeOnly(23, 50),
            Products = new List<Product>()
        };

    private static void ApplyTo(MerchantConfig merchant, MerchantEditorInput input)
    {
        merchant.MerchantName = input.MerchantName;
        merchant.MerchantId = input.MerchantId;
        merchant.EstateId = input.EstateId;
        merchant.ApplicationVersion = input.ApplicationVersion;
        merchant.DeviceIdentifier = input.DeviceIdentifier;
        merchant.Username = input.Username;
        merchant.Password = UnmaskSecret(input.Password, merchant.Password);
        merchant.SaleIntervalSeconds = input.SaleIntervalSeconds;
        merchant.FailureInjectionProbability = input.FailureInjectionProbability;
        merchant.DepositThreshold = input.DepositThreshold;
        merchant.DepositAmount = input.DepositAmount;
        merchant.OpeningTime = input.OpeningTime;
        merchant.ClosingTime = input.ClosingTime;
        merchant.Enabled = input.Enabled;
    }

    private static string MaskSecret(string? value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : SecretMask;

    private static string UnmaskSecret(string? value, string currentValue)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, SecretMask, StringComparison.Ordinal) ? currentValue : value;

    public sealed class MerchantEditorInput
    {
        public string MerchantName { get; set; } = string.Empty;
        public Guid MerchantId { get; set; }
        public Guid EstateId { get; set; }
        public string ApplicationVersion { get; set; } = string.Empty;
        public string DeviceIdentifier { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int SaleIntervalSeconds { get; set; }
        public double FailureInjectionProbability { get; set; }
        public decimal DepositThreshold { get; set; }
        public decimal DepositAmount { get; set; }
        public TimeOnly OpeningTime { get; set; }
        public TimeOnly ClosingTime { get; set; }
        public bool Enabled { get; set; }

        public static MerchantEditorInput FromMerchant(MerchantConfig merchant)
            => new()
            {
                MerchantName = merchant.MerchantName,
                MerchantId = merchant.MerchantId,
                EstateId = merchant.EstateId,
                ApplicationVersion = merchant.ApplicationVersion,
                DeviceIdentifier = merchant.DeviceIdentifier,
                Username = merchant.Username,
                Password = merchant.Password,
                SaleIntervalSeconds = merchant.SaleIntervalSeconds,
                FailureInjectionProbability = merchant.FailureInjectionProbability,
                DepositThreshold = merchant.DepositThreshold,
                DepositAmount = merchant.DepositAmount,
                OpeningTime = merchant.OpeningTime,
                ClosingTime = merchant.ClosingTime,
                Enabled = merchant.Enabled
            };
    }
}
