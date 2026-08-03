namespace TransactionProcessing.MerchantFileProcessor.Persistence;

public sealed class MerchantProcessingAuthenticationRecord
{
    public int Id { get; set; }

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string? Scope { get; set; }

    public string? Audience { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MerchantProcessingFileProcessingRecord
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MerchantProcessingTransactionGenerationRecord
{
    public int Id { get; set; }

    public int MinimumTransactionsPerContract { get; set; }

    public int MaximumTransactionsPerContract { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MerchantProcessingFileStatusPollingRecord
{
    public int Id { get; set; }

    public int PollIntervalSeconds { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MerchantProcessingMerchantScanRecord
{
    public int Id { get; set; }

    public int MerchantScanIntervalSeconds { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MerchantProcessingFileProfileRecord
{
    public int Id { get; set; }

    public int SortOrder { get; set; }

    public string FileProfileId { get; set; } = string.Empty;

    public string FileProcessorFileProfileId { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string FileExtension { get; set; } = string.Empty;

    public string? FileNamePattern { get; set; }

    public string? ContentType { get; set; }

    public string? Delimiter { get; set; }

    public bool IncludeHeader { get; set; }

    public bool WriteIndented { get; set; }

    public string? RootPropertyName { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

public abstract class MerchantProcessingFileProfileFieldRecordBase
{
    public int Id { get; set; }

    public int FileProfileRecordId { get; set; }

    public int SortOrder { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Source { get; set; }

    public string? Format { get; set; }

    public string? Value { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MerchantProcessingFileProfileFieldRecord : MerchantProcessingFileProfileFieldRecordBase;

public sealed class MerchantProcessingFileProfileHeaderFieldRecord : MerchantProcessingFileProfileFieldRecordBase;

public sealed class MerchantProcessingFileProfileTrailerFieldRecord : MerchantProcessingFileProfileFieldRecordBase;

public sealed class MerchantProcessingContractDefinitionRecord
{
    public int Id { get; set; }

    public int SortOrder { get; set; }

    public string ContractId { get; set; } = string.Empty;

    public string FileProfileId { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MerchantProcessingMerchantRecord
{
    public int Id { get; set; }

    public int SortOrder { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string EstateId { get; set; } = string.Empty;

    public string MerchantId { get; set; } = string.Empty;

    public string RunAtUtc { get; set; } = "02:00:00";

    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class MerchantProcessingMerchantRunTimeRecord
{
    public int Id { get; set; }

    public int MerchantRecordId { get; set; }

    public int SortOrder { get; set; }

    public string RunTimeUtc { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }
}

