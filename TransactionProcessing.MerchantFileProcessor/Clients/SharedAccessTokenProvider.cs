using SecurityService.Client;
using Shared.Logger;
using SimpleResults;
using TransactionProcessing.MerchantFileProcessor.Configuration;

namespace TransactionProcessing.MerchantFileProcessor.Clients;

public interface IAccessTokenProvider
{
    Task<Result<SharedAccessTokenProvider.CachedToken>> GetAccessToken(CancellationToken cancellationToken);
}

public sealed class SharedAccessTokenProvider(
    ISecurityServiceClient securityServiceClient,
    MerchantProcessingOptions options) : IAccessTokenProvider
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private CachedToken? currentToken;

    public async Task<Result<CachedToken>> GetAccessToken(CancellationToken cancellationToken)
    {
        if (IsTokenValid(this.currentToken))
        {
            return Result.Success(this.currentToken);
        }

        await this.refreshLock.WaitAsync(cancellationToken);

        try
        {
            if (IsTokenValid(this.currentToken))
            {
                return Result.Success(this.currentToken);
            }

            var tokenResult = await this.RequestToken(cancellationToken);

            if (tokenResult.IsFailed || tokenResult.Data is null) {
                return Result.Failure(string.Join("; ", tokenResult.Errors));
            }

            this.currentToken = tokenResult.Data;
            Logger.LogInformation(
                $"Retrieved shared access token that expires at {this.currentToken.ExpiresUtc:O}");

            return Result.Success(this.currentToken);
        }
        finally
        {
            this.refreshLock.Release();
        }
    }

    private async Task<Result<CachedToken>> RequestToken(CancellationToken cancellationToken)
    {
        var authentication = options.Authentication;
        var tokenResult = await securityServiceClient.GetToken(
            authentication.ClientId,
            authentication.ClientSecret,
            cancellationToken);

        if (tokenResult.IsFailed)
        {
            return new Result<CachedToken>
            {
                IsSuccess = false,
                Status = ResultStatus.Failure,
                Message = "Security service client failed to retrieve an access token."
            };
        }

        var token = tokenResult.Data;

        if (string.IsNullOrWhiteSpace(token.AccessToken))
        {
            return new Result<CachedToken>
            {
                IsSuccess = false,
                Status = ResultStatus.Failure,
                Message = "Security service client returned an empty access token."
            };
        }

        return Result.Success(new CachedToken(token.AccessToken, DateTimeOffset.UtcNow.AddSeconds(Convert.ToInt32(token.ExpiresIn))));
    }

    private static bool IsTokenValid(CachedToken? token) =>
        token is not null && token.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(2);

    public sealed record CachedToken(string AccessToken, DateTimeOffset ExpiresUtc);
}
