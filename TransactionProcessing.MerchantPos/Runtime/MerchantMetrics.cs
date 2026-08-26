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
        metric.IncrementSales();
    }

    public void IncrementFailedSales(Guid merchantId)
    {
        var metric = Get(merchantId);
        metric.IncrementFailedSales();
    }

    public void SetBalance(Guid merchantId, decimal balance)
    {
        var metric = Get(merchantId);
        metric.SetBalance(balance);
    }

    public void SetLastEndOfDay(Guid merchantId)
    {
        var metric = Get(merchantId);
        metric.SetLastEndOfDay(DateTime.UtcNow);
    }
}

public sealed class MerchantMetricSnapshot
{
    private int salesCount;
    private int failedSales;
    private decimal balance;
    private DateTime? lastSaleUtc;
    private DateTime? lastEndOfDay;

    public int SalesCount => salesCount;

    public int FailedSales => failedSales;

    public decimal Balance => balance;

    public DateTime? LastSaleUtc => lastSaleUtc;

    public DateTime? LastEndOfDay => lastEndOfDay;

    public void IncrementSales()
    {
        Interlocked.Increment(ref salesCount);
        lastSaleUtc = DateTime.UtcNow;
    }

    public void IncrementFailedSales()
    {
        Interlocked.Increment(ref failedSales);
    }

    public void SetBalance(decimal value)
    {
        balance = value;
    }

    public void SetLastEndOfDay(DateTime value)
    {
        lastEndOfDay = value;
    }
}
