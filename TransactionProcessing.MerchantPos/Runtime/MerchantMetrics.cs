using System.Collections.Concurrent;

namespace TransactionProcessing.MerchantPos.Runtime;

public sealed class MerchantMetrics
{
    private readonly ConcurrentDictionary<Guid, MerchantMetricSnapshot> _metrics = new();

    public MerchantMetricSnapshot Get(Guid merchantId)
        => _metrics.GetOrAdd(merchantId, _ => new MerchantMetricSnapshot());

    public void IncrementSales(Guid merchantId)
    {
        var metric = Get(merchantId);
        Interlocked.Increment(ref metric.SalesCount);
        metric.LastSaleUtc = DateTime.UtcNow;
    }

    public void IncrementFailedSales(Guid merchantId)
    {
        var metric = Get(merchantId);
        Interlocked.Increment(ref metric.FailedSales);
    }

    public void SetBalance(Guid merchantId, decimal balance)
    {
        var metric = Get(merchantId);
        metric.Balance = balance;
    }

    public void SetLastEndOfDay(Guid merchantId)
    {
        var metric = Get(merchantId);
        metric.LastEndOfDay = DateTime.UtcNow;
    }
}

public sealed class MerchantMetricSnapshot
{
    public int SalesCount;
    public int FailedSales;
    public decimal Balance;
    public DateTime? LastSaleUtc;
    public DateTime? LastEndOfDay;
}
