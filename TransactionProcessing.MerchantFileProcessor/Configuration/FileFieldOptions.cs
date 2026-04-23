namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class FileFieldOptions {
    public string Name { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string? Format { get; init; }

    public string? Value { get; init; }
}