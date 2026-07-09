using TransactionProcessing.MerchantFileProcessor.Clients;
using TransactionProcessing.MerchantFileProcessor.Configuration;
using TransactionProcessing.MerchantFileProcessor.FileBuilding;

namespace TransactionProcessing.MerchantFileProcessor.Services;

public interface IMerchantProcessingService
{
    Task ProcessAsync(MerchantOptions merchant, DateTimeOffset scheduledRunUtc, Guid runId, CancellationToken cancellationToken);
}

public sealed class MerchantProcessingService(
    IAccessTokenProvider accessTokenProvider,
    IMerchantContractDataClient merchantContractDataClient,
    ITransactionFileGenerationService transactionFileGenerationService,
    IMerchantDepositClient merchantDepositClient,
    IFileProcessingClient fileProcessingClient,
    IFileStatusStore fileStatusStore) : IMerchantProcessingService
{
    public async Task ProcessAsync(MerchantOptions merchant, DateTimeOffset scheduledRunUtc, Guid runId, CancellationToken cancellationToken)
    {
        var processingTimestampUtc = DateTimeOffset.UtcNow;
        var logger = LocalLogger.For(merchant.MerchantId, runId);
        var runResultRecorded = false;

        logger.LogInformation($"Processing merchant for scheduled run {scheduledRunUtc:O}");

        try
        {
            logger.LogInformation("Requesting access token");
            var accessTokenResult = await accessTokenProvider.GetAccessToken(cancellationToken);
            if (accessTokenResult.IsFailed)
            {
                throw new InvalidOperationException(string.Join("; ", accessTokenResult.Errors));
            }

            var accessToken = accessTokenResult.Data.AccessToken;
            logger.LogInformation("Access token retrieved successfully");

            logger.LogInformation("Requesting contract data");
            var contractsResult = await merchantContractDataClient.GetContracts(merchant, accessToken, cancellationToken);
            if (contractsResult.IsFailed || contractsResult.Data is null)
            {
                throw new InvalidOperationException(string.Join("; ", contractsResult.Errors));
            }

            var contracts = contractsResult.Data;
            logger.LogInformation($"Retrieved {contracts.Count} contracts");

            var configuredContracts = transactionFileGenerationService.GetConfiguredContracts(contracts);
            var skippedUnconfiguredContracts = contracts.Count - configuredContracts.Count;

            if (skippedUnconfiguredContracts > 0)
            {
                logger.LogInformation($"Skipping {skippedUnconfiguredContracts} contracts because they do not have a configured file profile.");
            }

            var contractFailures = new List<Exception>();

            foreach (var contract in configuredContracts)
            {
                if (!contract.Products.Any(product => product.IsFixedValue))
                {
                    logger.LogWarning($"Skipping contract {contract.ContractId} because it does not contain any fixed-value products.");
                    continue;
                }

                var hasSuccessfulUpload = await fileStatusStore.HasSuccessfulUploadAsync(
                    merchant,
                    contract,
                    scheduledRunUtc,
                    cancellationToken);

                if (hasSuccessfulUpload)
                {
                    logger.LogInformation($"Skipping contract {contract.ContractId} because it was already uploaded for scheduled run {scheduledRunUtc:O}.");
                    continue;
                }

                GeneratedFile? generatedFile = null;
                var fileUploaded = false;

                try
                {
                    generatedFile = transactionFileGenerationService.BuildFile(merchant, contract, processingTimestampUtc);

                    logger.LogInformation($"Generated {generatedFile.Format} file {generatedFile.FileName} using profile {generatedFile.FileProfileId} for contract {contract.ContractId} with {generatedFile.RecordCount} records totaling {generatedFile.TotalAmount}");

                    var depositAmount = CalculateDepositAmount(generatedFile);
                    var depositReference = BuildDepositReference(runId, contract, generatedFile);
                    var depositResult = await merchantDepositClient.MakeDeposit(
                        merchant,
                        accessToken,
                        depositAmount,
                        depositReference,
                        processingTimestampUtc,
                        cancellationToken);

                    if (depositResult.IsFailed)
                    {
                        throw new InvalidOperationException(string.Join("; ", depositResult.Errors));
                    }

                    logger.LogInformation($"Made merchant deposit of {depositAmount:0.00} for contract {contract.ContractId}");

                    var uploadResult = await fileProcessingClient.Upload(
                        merchant,
                        contract,
                        accessToken,
                        generatedFile,
                        cancellationToken);
                    if (uploadResult.IsFailed || uploadResult.Data == Guid.Empty)
                    {
                        throw new InvalidOperationException(string.Join("; ", uploadResult.Errors));
                    }

                    var fileId = uploadResult.Data;
                    fileUploaded = true;

                    await fileStatusStore.RecordSuccessAsync(
                        runId,
                        merchant,
                        contract,
                        scheduledRunUtc,
                        fileId,
                        generatedFile,
                        cancellationToken);

                    logger.LogInformation($"Uploaded {generatedFile.Format} file {generatedFile.FileName} for contract {contract.ContractId} with file id {fileId}");
                }
                catch (Exception ex)
                {
                    if (!fileUploaded)
                    {
                        await fileStatusStore.RecordFailureAsync(
                            runId,
                            merchant,
                            contract,
                            scheduledRunUtc,
                            generatedFile,
                            ex.ToString(),
                            cancellationToken);
                    }

                    logger.LogError($"Contract processing failed for contract {contract.ContractId}", ex);
                    contractFailures.Add(ex);
                }
            }

            if (contractFailures.Count > 0)
            {
                var errorMessage = string.Join(Environment.NewLine + Environment.NewLine, contractFailures.Select(ex => ex.ToString()));
                await fileStatusStore.RecordMerchantRunResultAsync(
                    runId,
                    merchant,
                    scheduledRunUtc,
                    MerchantRunStatuses.Failed,
                    errorMessage,
                    cancellationToken);
                runResultRecorded = true;

                throw new AggregateException($"Scheduled run {scheduledRunUtc:O} failed for {contractFailures.Count} contract(s).", contractFailures);
            }

            await fileStatusStore.RecordMerchantRunResultAsync(
                runId,
                merchant,
                scheduledRunUtc,
                MerchantRunStatuses.Succeeded,
                null,
                cancellationToken);
            runResultRecorded = true;

            logger.LogInformation($"Completed merchant for scheduled run {scheduledRunUtc:O}");
        }
        catch (Exception ex)
        {
            if (!runResultRecorded && ex is not OperationCanceledException)
            {
                await fileStatusStore.RecordMerchantRunResultAsync(
                    runId,
                    merchant,
                    scheduledRunUtc,
                    MerchantRunStatuses.Failed,
                    ex.ToString(),
                    cancellationToken);
            }

            logger.LogError("Merchant processing failed", ex);

            throw;
        }
    }

    private static decimal CalculateDepositAmount(GeneratedFile generatedFile)
    {
        if (generatedFile.Transactions.Count < 2)
        {
            throw new InvalidOperationException(
                $"Generated file '{generatedFile.FileName}' must contain at least two transactions to support the deposit adjustment.");
        }

        return generatedFile.Transactions
            .Take(generatedFile.Transactions.Count - 1)
            .Sum(transaction => transaction.TotalAmount);
    }

    private static string BuildDepositReference(Guid runId, ContractOptions contract, GeneratedFile generatedFile)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(generatedFile.FileName);
        return $"{contract.ContractId}-{runId:N}-{fileNameWithoutExtension}";
    }
}
