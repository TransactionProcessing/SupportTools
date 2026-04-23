using Shared.Logger;
using SimpleResults;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessor.Client;
using TransactionProcessor.DataTransferObjects.Requests.Merchant;

namespace TransactionProcessing.MerchantFileProcessor.Clients;

public interface IMerchantDepositClient
{
    Task<Result> MakeDeposit(
        MerchantOptions merchant,
        string accessToken,
        decimal amount,
        string reference,
        DateTimeOffset depositTimestampUtc,
        CancellationToken cancellationToken);
}

public sealed class MerchantDepositClient(
    ITransactionProcessorClient transactionProcessorClient) : IMerchantDepositClient
{
    public async Task<Result> MakeDeposit(
        MerchantOptions merchant,
        string accessToken,
        decimal amount,
        string reference,
        DateTimeOffset depositTimestampUtc,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
        {
            return Result.Failure($"Deposit amount for merchant '{merchant.MerchantId}' must be greater than zero.");
        }

        var request = new MakeMerchantDepositRequest
        {
            Amount = amount,
            DepositDateTime = depositTimestampUtc.UtcDateTime,
            Reference = reference
        };

        Logger.LogInformation($"Making merchant deposit of {amount:0.00} for merchant {merchant.MerchantId} using reference {reference}");

        var result = await transactionProcessorClient.MakeMerchantDeposit(
            accessToken,
            merchant.GetEstateGuid(),
            merchant.GetMerchantGuid(),
            request,
            cancellationToken);

        return result.IsFailed
            ? Result.Failure($"Transaction processor client failed to make a deposit for merchant '{merchant.MerchantId}'.")
            : Result.Success();
    }
}
