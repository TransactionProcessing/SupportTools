namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class TransactionGenerationOptions {
    public int MinimumTransactionsPerContract { get; init; } = 5;

    public int MaximumTransactionsPerContract { get; init; } = 25;
}