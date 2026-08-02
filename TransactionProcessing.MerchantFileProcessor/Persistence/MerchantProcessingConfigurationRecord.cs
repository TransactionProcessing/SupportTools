namespace TransactionProcessing.MerchantFileProcessor.Persistence;

public sealed class MerchantProcessingConfigurationRecord
{
    public int Id { get; set; }

    public string ConfigurationJson { get; set; } = string.Empty;

    public DateTimeOffset UpdatedUtc { get; set; }
}
