namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class ContractOptions {
    public string ContractId { get; init; } = string.Empty;

    public string ContractName { get; init; } = string.Empty;

    public string Issuer { get; init; } = string.Empty;

    public List<ProductOptions> Products { get; init; } = [];
}