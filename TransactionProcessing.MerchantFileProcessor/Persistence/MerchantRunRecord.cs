namespace TransactionProcessing.MerchantFileProcessor.Persistence;

public sealed class MerchantRunRecord
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public string MerchantId { get; set; } = string.Empty;

    public string? MerchantName { get; set; }

    public DateTimeOffset ScheduledRunUtc { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CompletedUtc { get; set; }
}