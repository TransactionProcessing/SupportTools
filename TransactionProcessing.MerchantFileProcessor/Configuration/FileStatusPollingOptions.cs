namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class FileStatusPollingOptions {
    public int PollIntervalSeconds { get; init; } = 30;

    public TimeSpan GetPollInterval() => TimeSpan.FromSeconds(this.PollIntervalSeconds);
}