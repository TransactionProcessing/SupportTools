using SecurityService.Client;
using SecurityService.DataTransferObjects;
using TransactionProcessing.Shared.Logger;
using TransactionProcessing.UnionResults;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using Results = TransactionProcessing.UnionResults.Results;

namespace TransactionProcessing.MerchantFileProcessor.Clients;

public interface IAccessTokenProvider
{
    Task<Result<SharedAccessTokenProvider.CachedToken>> GetAccessToken(CancellationToken cancellationToken);
}

public sealed class SharedAccessTokenProvider(
    ISecurityServiceClient securityServiceClient,
    IMerchantProcessingConfigurationState configurationState) : IAccessTokenProvider
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private CachedToken? currentToken;

    public async Task<Result<CachedToken>> GetAccessToken(CancellationToken cancellationToken)
    {
        if (IsTokenValid(this.currentToken))
        {
            return Results.Ok(this.currentToken!);
        }

        await this.refreshLock.WaitAsync(cancellationToken);

        try
        {
            if (IsTokenValid(this.currentToken))
            {
                return Results.Ok(this.currentToken!);
            }

            var tokenResult = await this.RequestToken(cancellationToken);

            if (tokenResult.Value is Success<CachedToken> { Value: var token })
            {
                this.currentToken = token;
                Logger.LogInformation($"Retrieved shared access token that expires at {token.ExpiresUtc:O}");
            }

            return tokenResult;
        }
        finally
        {
            this.refreshLock.Release();
        }
    }

    private async Task<Result<CachedToken>> RequestToken(CancellationToken cancellationToken)
    {
        var authentication = configurationState.Current.Authentication;
        var tokenResult = await securityServiceClient.GetToken(
            authentication.ClientId,
            authentication.ClientSecret,
            cancellationToken);

        if (tokenResult.Value is not Success<TokenResponse> { Value: { } token })
        {
            return new Error(ErrorType.Failure, "Security service client failed to retrieve an access token.");
        }

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return new Error(ErrorType.Failure, "Security service client returned an empty access token.");
        }

        return Results.Ok(new CachedToken(token.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Convert.ToInt32(token.ExpiresIn))));
    }

    private static bool IsTokenValid(CachedToken? token) =>
        token is not null && token.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2);

    public sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresUtc);
}
