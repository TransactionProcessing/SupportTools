using MerchantPos.EF.Persistence;
using TransactionProcessing.MerchantPos.Persistence;
using TransactionProcessing.MerchantPos.Web;

namespace TransactionProcessing.MerchantPos.Runtime;

public sealed class MerchantDashboardModelFactory
{
    private readonly IEfRepository _repo;
    private readonly MerchantMetrics _metrics;
    private readonly MerchantPosSettingsStore _settingsStore;

    public MerchantDashboardModelFactory(
        IEfRepository repo,
        MerchantMetrics metrics,
        MerchantPosSettingsStore settingsStore)
    {
        _repo = repo;
        _metrics = metrics;
        _settingsStore = settingsStore;
    }

    public async Task<MerchantDashboardViewModel> BuildAsync()
    {
        var merchantRows = await _repo.GetAllMerchants();
        var rows = merchantRows.Select(row =>
        {
            var snapshot = _metrics.Get(row.MerchantId);
            return new MerchantDashboardRow(
                row.MerchantId,
                row.MerchantName,
                snapshot.Balance,
                snapshot.SalesCount,
                snapshot.FailedSales,
                snapshot.LastSaleUtc,
                snapshot.LastEndOfDay);
        }).ToList();

        var settings = _settingsStore.Current;
        var summary = new MerchantDashboardSummary(
            Math.Max(settings.WorkerSettings.Merchants.Count, rows.Count),
            rows.Sum(x => x.Balance),
            rows.Sum(x => x.Sales),
            rows.Sum(x => x.FailedSales));

        return new MerchantDashboardViewModel(
            DateTime.UtcNow,
            _settingsStore.ResolvePath(settings.ConnectionStrings.SettingsDb),
            summary,
            rows);
    }
}
