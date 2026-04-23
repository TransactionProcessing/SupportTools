namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class ProductOptions {
    public string ProductCode { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool IsFixedValue { get; init; }

    public int Quantity { get; init; } = 1;

    public decimal UnitAmount { get; init; }

    public string Currency { get; init; } = "GBP";
}