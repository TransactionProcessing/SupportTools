namespace TransactionProcessing.MerchantFileProcessor.Persistence;

public sealed class FileSendRecordLineStatus
{
    public long Id { get; set; }

    public long FileSendRecordId { get; set; }

    public int LineNumber { get; set; }

    public string? LineData { get; set; }

    public string ProcessingStatus { get; set; } = string.Empty;

    public string? RejectionReason { get; set; }

    public string? TransactionId { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}