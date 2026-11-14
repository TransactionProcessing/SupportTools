using TransactionProcessing.Shared.Logger;
using TransactionProcessing.UnionResults;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessor.Client;
using TransactionProcessor.DataTransferObjects.Responses.Contract;

namespace TransactionProcessing.MerchantFileProcessor.Clients;

public interface IMerchantContractDataClient
{
    Task<Result<IReadOnlyList<ContractOptions>>> GetContracts(
        MerchantOptions merchant,
        string accessToken,
        CancellationToken cancellationToken);
}

public sealed class MerchantContractDataClient(
    ITransactionProcessorClient transactionProcessorClient) : IMerchantContractDataClient
{
    public async Task<Result<IReadOnlyList<ContractOptions>>> GetContracts(
        MerchantOptions merchant,
        string accessToken,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation($"Requesting merchant contracts from TransactionProcessor for merchant {merchant.MerchantId} in estate {merchant.EstateId}");

        var result = await transactionProcessorClient.GetMerchantContracts(
            accessToken,
            merchant.GetEstateGuid(),
            merchant.GetMerchantGuid(),
            cancellationToken);

        if (result.IsFailed)
        {
            return Result<IReadOnlyList<ContractOptions>>.FromFailure(result);
        }

        if (result.Data is null || result.Data.Count == 0)
        {
            return Result<IReadOnlyList<ContractOptions>>.Fail(Error.Failure($"Transaction processor client did not return any contracts for merchant '{merchant.MerchantId}'."));
        }

        var contracts = MapContracts(result.Data);
        var validationResult = ValidateContracts(merchant.MerchantId, contracts);

        if (validationResult.IsFailed)
        {
            if (validationResult.Errors.Count == 1)
            {
                return Result<IReadOnlyList<ContractOptions>>.Fail(validationResult.Errors[0]);
            }

            return Result<IReadOnlyList<ContractOptions>>.Fail(new ErrorList(validationResult.Errors));
        }

        Logger.LogInformation($"Retrieved {contracts.Count} contracts from TransactionProcessor for merchant {merchant.MerchantId}");

        return Result.Success<IReadOnlyList<ContractOptions>>(contracts);
    }

    private static List<ContractOptions> MapContracts(IReadOnlyList<ContractResponse> sourceContracts)
    {
        return sourceContracts
            .Select(contract => new ContractOptions
            {
                ContractId = contract.ContractId.ToString(),
                ContractName = ResolveContractName(contract),
                Issuer = ResolveContractIssuer(contract),
                Products = contract.Products?
                    .Select(product => new ProductOptions
                    {
                        ProductCode = product.ProductId.ToString(),
                        Description = string.IsNullOrWhiteSpace(product.DisplayText) ? product.Name : product.DisplayText,
                        IsFixedValue = product.Value.HasValue,
                        Quantity = 1,
                        UnitAmount = product.Value ?? 0m,
                        Currency = "GBP"
                    })
                    .ToList() ?? []
            })
            .ToList();
    }

    private static string ResolveContractName(ContractResponse contract)
    {
        if (!string.IsNullOrWhiteSpace(contract.Description))
        {
            return contract.Description.Trim();
        }

        return contract.ContractId.ToString();
    }

    private static string ResolveContractIssuer(ContractResponse contract)
    {
        if (string.IsNullOrWhiteSpace(contract.Description))
        {
            return string.Empty;
        }

        const string contractSuffix = " Contract";

        return contract.Description.EndsWith(contractSuffix, StringComparison.OrdinalIgnoreCase)
            ? contract.Description[..^contractSuffix.Length].TrimEnd()
            : contract.Description.Trim();
    }

    private static Result ValidateContracts(string merchantId, IReadOnlyList<ContractOptions> contracts)
    {
        foreach (var contract in contracts)
        {
            if (string.IsNullOrWhiteSpace(contract.ContractId) || contract.Products.Count == 0)
            {
                return Result.Failure($"Contract data for merchant '{merchantId}' is missing a contract identifier or products.");
            }

            foreach (var product in contract.Products)
            {
                if (string.IsNullOrWhiteSpace(product.ProductCode) ||
                    product.Quantity <= 0 ||
                    product.UnitAmount < 0 ||
                    string.IsNullOrWhiteSpace(product.Currency))
                {
                    return Result.Failure($"Contract data for merchant '{merchantId}' contains an invalid product definition.");
                }
            }
        }

        return Result.Success();
    }
}
