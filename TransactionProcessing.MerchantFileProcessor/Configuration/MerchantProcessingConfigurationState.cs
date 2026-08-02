namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed record MerchantProcessingConfigurationSnapshot(
    MerchantProcessingOptions Options,
    string Json,
    DateTimeOffset UpdatedUtc);

public interface IMerchantProcessingConfigurationState
{
    MerchantProcessingOptions Current { get; }

    string CurrentJson { get; }

    DateTimeOffset UpdatedUtc { get; }

    bool IsLoaded { get; }

    void Set(MerchantProcessingConfigurationSnapshot snapshot);
}

public sealed class MerchantProcessingConfigurationState : IMerchantProcessingConfigurationState
{
    private MerchantProcessingConfigurationSnapshot snapshot = new(new MerchantProcessingOptions(), string.Empty, DateTimeOffset.MinValue);

    public MerchantProcessingOptions Current => this.snapshot.Options;

    public string CurrentJson => this.snapshot.Json;

    public DateTimeOffset UpdatedUtc => this.snapshot.UpdatedUtc;

    public bool IsLoaded { get; private set; }

    public void Set(MerchantProcessingConfigurationSnapshot snapshot)
    {
        this.snapshot = snapshot;
        this.IsLoaded = true;
    }
}
