namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class FrameworkLoggingOptions {
    public static string SectionName { get; } = "FrameworkLogging";

    public bool EnableEfCoreCommandTrace { get; init; }

    public bool EnableHttpClientTrace { get; init; }
}
