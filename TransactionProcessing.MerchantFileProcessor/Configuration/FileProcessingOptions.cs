namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class FileProcessingOptions {
    public string UserId { get; init; } = string.Empty;

    public Guid GetUserGuid() => Guid.Parse(this.UserId);
}