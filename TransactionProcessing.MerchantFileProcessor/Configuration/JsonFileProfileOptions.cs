namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class JsonFileProfileOptions {
    public bool WriteIndented { get; init; }

    public string? RootPropertyName { get; init; }
}