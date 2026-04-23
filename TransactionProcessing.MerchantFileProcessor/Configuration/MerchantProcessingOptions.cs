namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class MerchantProcessingOptions {
    public const string SectionName = "MerchantProcessing";

    public AuthenticationOptions Authentication { get; init; } = new();

    public FileProcessingOptions FileProcessing { get; init; } = new();

    public TransactionGenerationOptions TransactionGeneration { get; init; } = new();

    public FileStatusPollingOptions FileStatusPolling { get; init; } = new();

    public List<ContractDefinitionOptions> ContractDefinitions { get; init; } = [];

    public List<FileProfileOptions> FileProfiles { get; init; } = [];

    public List<MerchantOptions> Merchants { get; init; } = [];
}