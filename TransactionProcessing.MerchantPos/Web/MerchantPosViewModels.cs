namespace TransactionProcessing.MerchantPos.Web;

public sealed record MerchantDashboardViewModel(
    DateTime GeneratedUtc,
    string SettingsDbPath,
    MerchantDashboardSummary Summary,
    IReadOnlyList<MerchantDashboardRow> Merchants);

public sealed record MerchantDashboardSummary(
    int MerchantCount,
    decimal TotalBalance,
    int SalesCount,
    int FailedSales);

public sealed record MerchantDashboardRow(
    Guid MerchantId,
    string MerchantName,
    decimal Balance,
    int Sales,
    int FailedSales,
    DateTime? LastSaleUtc,
    DateTime? LastEndOfDayUtc);
