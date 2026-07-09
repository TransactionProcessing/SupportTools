namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class DelimitedFileProfileOptions {
    public string Delimiter { get; init; } = ",";

    public bool IncludeHeader { get; init; } = true;

    public List<FileFieldOptions> HeaderFields { get; init; } = [];

    public List<FileFieldOptions> TrailerFields { get; init; } = [];
}