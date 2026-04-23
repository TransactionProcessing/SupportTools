using TransactionProcessing.MerchantFileProcessor.Configuration;

namespace TransactionProcessing.MerchantFileProcessor.FileBuilding;

public interface ITransactionFileGenerationService
{
    IReadOnlyList<ContractOptions> GetConfiguredContracts(IReadOnlyList<ContractOptions> contracts);

    GeneratedFile BuildFile(MerchantOptions merchant, ContractOptions contract, DateTimeOffset processingTimestampUtc);
}

public interface ITransactionFileBuilder
{
    string Format { get; }

    GeneratedFile Build(
        MerchantOptions merchant,
        ContractOptions contract,
        FileProfileOptions fileProfile,
        IReadOnlyList<GeneratedTransaction> transactions,
        DateTimeOffset processingTimestampUtc);
}

public sealed class TransactionFileGenerationService(
    MerchantProcessingOptions options,
    ITransactionGenerator transactionGenerator,
    IEnumerable<ITransactionFileBuilder> builders) : ITransactionFileGenerationService
{
    public IReadOnlyList<ContractOptions> GetConfiguredContracts(IReadOnlyList<ContractOptions> contracts)
    {
        return contracts
            .Where(contract => this.TryResolveFileProfile(contract.ContractId, out _))
            .ToArray();
    }

    public GeneratedFile BuildFile(MerchantOptions merchant, ContractOptions contract, DateTimeOffset processingTimestampUtc)
    {
        if (!this.TryResolveFileProfile(contract.ContractId, out var fileProfile))
        {
            throw new InvalidOperationException(
                $"Contract '{contract.ContractId}' for merchant '{merchant.MerchantId}' does not have a shared contract definition.");
        }

        var resolvedFileProfile = fileProfile ?? throw new InvalidOperationException(
            $"Contract '{contract.ContractId}' for merchant '{merchant.MerchantId}' could not resolve a file profile.");

        var builder = builders.FirstOrDefault(candidate =>
            candidate.Format.Equals(resolvedFileProfile.Format, StringComparison.OrdinalIgnoreCase));

        if (builder is null)
        {
            throw new InvalidOperationException(
                $"No transaction file builder is registered for format '{resolvedFileProfile.Format}'.");
        }

        var transactions = transactionGenerator.GenerateTransactions(merchant, contract, processingTimestampUtc);

        return builder.Build(merchant, contract, resolvedFileProfile, transactions, processingTimestampUtc);
    }

    private bool TryResolveFileProfile(string contractId, out FileProfileOptions? fileProfile)
    {
        var contractDefinition = options.ContractDefinitions.FirstOrDefault(definition =>
            definition.ContractId.Equals(contractId, StringComparison.OrdinalIgnoreCase));

        if (contractDefinition is null)
        {
            fileProfile = null;
            return false;
        }

        fileProfile = options.FileProfiles.FirstOrDefault(profile =>
            profile.FileProfileId.Equals(contractDefinition.FileProfileId, StringComparison.OrdinalIgnoreCase));

        if (fileProfile is null)
        {
            throw new InvalidOperationException(
                $"Contract '{contractId}' references unknown file profile '{contractDefinition.FileProfileId}'.");
        }

        return true;
    }
}
