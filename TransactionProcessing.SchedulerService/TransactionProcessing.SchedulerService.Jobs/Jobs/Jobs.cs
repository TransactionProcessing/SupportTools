using SimpleResults;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Configuration;
using TransactionProcessor.DataTransferObjects;
using TransactionProcessor.DataTransferObjects.Responses.Contract;
using TransactionProcessor.DataTransferObjects.Responses.Merchant;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EventStore.Client;
using MessagingService.Client;
using MessagingService.DataTransferObjects;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Quartz;
using Shared.Logger;

public static class Jobs {
    public static async Task<Result> GenerateMerchantStatements(ITransactionDataGeneratorService t,
                                                                MerchantStatementJobConfig config,
                                                                CancellationToken cancellationToken) {
        var getMerchantsResult = await t.GetMerchants(config.EstateId, cancellationToken);

        if (getMerchantsResult.IsFailed) {
            return Result.Failure($"No merchants returned for Estate [{config.EstateId}]");
        }

        var merchants = getMerchantsResult.Data;
        List<string> results = new();
        foreach (MerchantResponse merchantResponse in merchants) {
            Result result = await t.GenerateMerchantStatement(merchantResponse.EstateId, merchantResponse.MerchantId, DateTime.Now, cancellationToken);
            if (result.IsFailed) {
                results.Add(merchantResponse.MerchantName);
            }
        }

        if (results.Any()) {
            return Result.Failure($"Error generating statements for merchants [{string.Join(",", results)}]");
        }

        return Result.Success();
    }

    public static async Task<Result> GenerateFileUploads(ITransactionDataGeneratorService t,
                                                         FileUploadJobConfig config,
                                                         CancellationToken cancellationToken) {
        Result<MerchantResponse> merchantResult = await t.GetMerchant(config.EstateId, config.MerchantId, cancellationToken);

        if (merchantResult.IsFailed) {
            return Result.Failure($"No merchant returned for Estate Id [{config.EstateId}] Merchant Id [{config.MerchantId}]");
        }

        MerchantResponse merchant = merchantResult.Data;
        Result<List<ContractResponse>> getMerchantContractsResult = await t.GetMerchantContracts(merchant, cancellationToken);
        if (getMerchantContractsResult.IsFailed) {
            Console.WriteLine($"Failed to get merchant contracts: {getMerchantContractsResult.Message}");
            return Result.Failure();
        }

        DateTime fileDate = DateTime.Now.Date;
        List<string> results = new List<string>();
        foreach (ContractResponse contract in getMerchantContractsResult.Data) {
            if (config.ContractNames.Contains(contract.Description) == false)
                continue;

            // Generate a file and upload
            Result result = await t.SendUploadFile(fileDate, contract, merchant, config.UserId, cancellationToken);

            if (result.IsFailed) {
                results.Add(contract.OperatorName);
            }
        }

        if (results.Any()) {
            return Result.Failure($"Error uploading files for merchant [{merchant.MerchantName}] [{string.Join(",", results)}]");
        }

        return Result.Success();
    }

    public static async Task<Result> GenerateFloatCredits(ITransactionDataGeneratorService t,
                                                          MakeFloatCreditsJobConfig config,
                                                          CancellationToken cancellationToken) {
        List<string> results = new();
        // Get all the contracts up front
        Result<List<ContractResponse>> contractsResult = await t.GetEstateContracts(config.EstateId, cancellationToken);
        if (contractsResult.IsFailed) {
            results.Add($"Error getting Contract List");
            return Result.Failure($"Error making float credits for [{string.Join(",", results)}]");
        }

        foreach (DepositAmount configDepositAmount in config.DepositAmounts) {
            // lookup the contract/product info
            ContractResponse contract = contractsResult.Data.SingleOrDefault(c => c.Description == configDepositAmount.ContractName);
            if (contract == null) {
                results.Add($"Contract Name {configDepositAmount.ContractName} not found");
                continue;
            }

            ContractProduct product = contract.Products.SingleOrDefault(p => p.Name == configDepositAmount.ProductName);
            if (product == null) {
                results.Add($"Contract Name {configDepositAmount.ContractName} Product {configDepositAmount.ProductName} not found");
                continue;
            }

            Result result = await t.MakeFloatDeposit(DateTime.Now, config.EstateId, contract.ContractId, product.ProductId, configDepositAmount.Amount, cancellationToken);
            if (result.IsFailed) {
                results.Add($"Contract Id {contract.ContractId} Product Id {product.ProductId}");
            }
        }

        if (results.Any()) {
            return Result.Failure($"Error making float credits for [{string.Join(",", results)}]");
        }

        return Result.Success();
    }

    public static async Task<Result> GenerateTransactions(ITransactionDataGeneratorService t,
                                                          TransactionJobConfig config,
                                                          CancellationToken cancellationToken) {
        // get the merchant
        Result<MerchantResponse> merchantResult = await t.GetMerchant(config.EstateId, config.MerchantId, cancellationToken);

        if (merchantResult.IsFailed) {
            return Result.Failure($"Error getting Merchant Id [{config.MerchantId}] for Estate Id [{config.EstateId}]");
        }

        MerchantResponse merchant = merchantResult.Data;

        DateTime transactionDate = DateTime.Now;

        // Get the merchants contracts
        Result<List<ContractResponse>> contractResult = await t.GetMerchantContracts(merchant, cancellationToken);

        if (contractResult.IsFailed) {
            return Result.Failure($"Error getting contracts for Merchant [{merchant.MerchantName}]");
        }

        List<ContractResponse> contracts = contractResult.Data;
        if (contracts.Any() == false) {
            return Result.Failure($"No contracts returned for Merchant [{merchant.MerchantName}]");
        }

        if (config.IsLogon) {
            // Do a logon transaction for the merchant
            Result<SerialisedMessage> logonResult = await t.PerformMerchantLogon(transactionDate, merchant, cancellationToken);

            if (logonResult.IsFailed) {
                return Result.Failure($"Error performing logon for Merchant [{merchant.MerchantName}]");
            }

            return Result.Success();
        }

        Random r = new Random();
        List<string> results = new();
        foreach (ContractResponse contract in contracts) {
            if (config.ContractNames.Contains(contract.Description) == false)
                continue;

            int numberOfSales = r.Next(2, 4);
            // Generate and send some sales
            Result saleResult = await t.SendSales(transactionDate, merchant, contract, numberOfSales, 0,cancellationToken);

            if (saleResult.IsFailed) {
                results.Add(contract.OperatorName);
            }
        }

        if (results.Any()) {
            return Result.Failure($"Error sending sales files for merchant [{merchant.MerchantName}] [{String.Join(",", results)}]");
        }

        return Result.Success();
    }

    public static async Task<Result> PerformSettlement(ITransactionDataGeneratorService t,
                                                       DateTime dateTime,
                                                       SettlementJobConfig config,
                                                       CancellationToken cancellationToken) {
        Result result = await t.PerformSettlement(dateTime.Date, config.EstateId, cancellationToken);

        if (result.IsFailed) {
            return Result.Failure($"Error performing settlement for Estate Id [{config.EstateId}] and date [{dateTime:dd-MM-yyyy}]");
        }

        return Result.Success();
    }

    public static async Task<Result> ReplayParkedQueues(ReplayParkedQueueJobConfig config,
                                                        CancellationToken cancellationToken) {
        try {
            EventStoreClientSettings clientSettings = EventStoreClientSettings.Create(config.EventStoreAddress);
            EventStorePersistentSubscriptionsClient client = new EventStorePersistentSubscriptionsClient(clientSettings);

            IEnumerable<PersistentSubscriptionInfo> subscriptions = await client.ListAllAsync(cancellationToken: cancellationToken);

            if (subscriptions.Any() == false) {
                return Result.Success("No subscriptions found to replay parked messages.");
            }

            foreach (PersistentSubscriptionInfo persistentSubscriptionInfo in subscriptions) {
                Logger.LogInformation($"About to process subscription [{persistentSubscriptionInfo.GroupName}] on stream [{persistentSubscriptionInfo.EventSource}]");
                if (persistentSubscriptionInfo.Stats.ParkedMessageCount > 0) {
                    Logger.LogWarning($"[{persistentSubscriptionInfo.Stats.ParkedMessageCount}] parked messages to be replayed.");
                    await client.ReplayParkedMessagesToStreamAsync(persistentSubscriptionInfo.EventSource, persistentSubscriptionInfo.GroupName, cancellationToken: cancellationToken);
                    Logger.LogWarning($"{persistentSubscriptionInfo.Stats.ParkedMessageCount} Parked messages replayed for subscription [{persistentSubscriptionInfo.GroupName}] on stream [{persistentSubscriptionInfo.EventSource}].");
                }
                else {
                    Logger.LogWarning($"{persistentSubscriptionInfo.Stats.ParkedMessageCount} Parked messages replayed for subscription [{persistentSubscriptionInfo.GroupName}] on stream [{persistentSubscriptionInfo.EventSource}].");
                }
            }
            return Result.Success("Parked messages replayed successfully.");
        }
        catch (Exception ex) {
            return Result.Failure($"Error replaying parked messages: {ex.Message}");
        }
    }
}
