namespace TransactionProcessing.MerchantFileProcessor.Configuration;

public sealed class AuthenticationOptions {
    public string ClientId { get; init; } = string.Empty;

    public string ClientSecret { get; init; } = string.Empty;

    public string? Scope { get; init; }

    public string? Audience { get; init; }
}