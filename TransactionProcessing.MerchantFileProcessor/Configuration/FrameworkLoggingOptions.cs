namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class FrameworkLoggingOptions {
    public const string SectionName = "FrameworkLogging";

    public bool EnableEfCoreCommandTrace { get; init; }

    public bool EnableHttpClientTrace { get; init; }
}
