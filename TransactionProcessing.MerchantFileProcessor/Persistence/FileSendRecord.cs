namespace TransactionProcessing.MerchantFileProcessor.Persistence;

public sealed class FileSendRecord
{
    public long Id { get; set; }

    public Guid RunId { get; set; }

    public string MerchantId { get; set; } = string.Empty;

    public string? EstateId { get; set; }

    public string? MerchantName { get; set; }

    public string ContractId { get; set; } = string.Empty;

    public string? ContractName { get; set; }

    public string? FileName { get; set; }

    public string? FileProfileId { get; set; }

    public string? Format { get; set; }

    public string? FileProcessorFileId { get; set; }

    public DateTimeOffset ScheduledRunUtc { get; set; }

    public string? FileContent { get; set; }

    public int? RecordCount { get; set; }

    public decimal? TotalAmount { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }

    public bool ProcessingCompleted { get; set; }

    public DateTimeOffset? LastStatusCheckUtc { get; set; }

    public DateTimeOffset ProcessedUtc { get; set; }

    public List<FileSendRecordLineStatus> LineStatuses { get; set; } = [];
}