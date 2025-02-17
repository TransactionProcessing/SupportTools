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

public static class Jobs
{
    public static async Task<Result> GenerateMerchantStatements(ITransactionDataGeneratorService t, MerchantStatementJobConfig config, CancellationToken cancellationToken)
    {
        List<MerchantResponse> merchants = await t.GetMerchants(config.EstateId, cancellationToken);

        if (merchants.Any() == false)
        {
            return Result.Failure($"No merchants returned for Estate [{config.EstateId}]");
        }

        List<string> results = new();
        foreach (MerchantResponse merchantResponse in merchants)
        {
            Result result = await t.GenerateMerchantStatement(merchantResponse.EstateId, merchantResponse.MerchantId, DateTime.Now, cancellationToken);
            if (result.IsFailed)
            {
                results.Add(merchantResponse.MerchantName);
            }
        }

        if (results.Any())
        {
            return Result.Failure($"Error generating statements for merchants [{string.Join(",", results)}]");
        }
        return Result.Success();
    }

    public static async Task<Result> GenerateFileUploads(ITransactionDataGeneratorService t, FileUploadJobConfig config, CancellationToken cancellationToken)
    {
        MerchantResponse merchant = await t.GetMerchant(config.EstateId, config.MerchantId, cancellationToken);

        if (merchant == default)
        {
            return Result.Failure($"No merchant returned for Estate Id [{config.EstateId}] Merchant Id [{config.MerchantId}]");
        }

        List<ContractResponse> contracts = await t.GetMerchantContracts(merchant, cancellationToken);
        DateTime fileDate = DateTime.Now.Date;
        List<string> results = new List<string>();
        foreach (ContractResponse contract in contracts)
        {
            if (config.ContractNames.Contains(contract.Description) == false)
                continue;

            // Generate a file and upload
            Result result = await t.SendUploadFile(fileDate, contract, merchant, config.UserId, cancellationToken);

            if (result.IsFailed)
            {
                results.Add(contract.OperatorName);
            }
        }

        if (results.Any())
        {
            return Result.Failure($"Error uploading files for merchant [{merchant.MerchantName}] [{string.Join(",", results)}]");
        }
        return Result.Success();
    }

    public static async Task<Result> GenerateFloatCredits(ITransactionDataGeneratorService t,
                                                  MakeFloatCreditsJobConfig config,
                                                  CancellationToken cancellationToken) {

        List<string> results = new List<string>();
        foreach (DepositAmount configDepositAmount in config.DepositAmounts) {
            Result result = await t.MakeFloatDeposit(DateTime.Now, config.EstateId, configDepositAmount.ContractId,
                configDepositAmount.ProductId, configDepositAmount.Amount, cancellationToken);
            if (result.IsFailed)
            {
                results.Add($"Contract Id {configDepositAmount.ContractId} Product Id {configDepositAmount.ProductId}");
            }
        }
        if (results.Any())
        {
            return Result.Failure($"Error making float credits for [{string.Join(",", results)}]");
        }
        return Result.Success();
    }

    public static async Task<Result> GenerateTransactions(ITransactionDataGeneratorService t, TransactionJobConfig config, CancellationToken cancellationToken)
    {
        // get the merchant
        var merchantResult = await t.GetMerchant(config.EstateId, config.MerchantId, cancellationToken);

        if (merchantResult.IsFailed)
        {
            return Result.Failure($"Error getting Merchant Id [{config.MerchantId}] for Estate Id [{config.EstateId}]");
        }

        var merchant = merchantResult.Data;

        DateTime transactionDate = DateTime.Now;

        // Get the merchants contracts
        var contractResult = await t.GetMerchantContracts(merchant, cancellationToken);
        
        if (contractResult.IsFailed) {
            return Result.Failure($"Error getting contracts for Merchant [{merchant.MerchantName}]");
        }

        List<ContractResponse> contracts = contractResult.Data;
        if (contracts.Any() == false)
        {
            return Result.Failure($"No contracts returned for Merchant [{merchant.MerchantName}]");
        }

        if (config.IsLogon)
        {
            // Do a logon transaction for the merchant
            Result<SerialisedMessage> logonResult = await t.PerformMerchantLogon(transactionDate, merchant, cancellationToken);

            if (logonResult.IsFailed)
            {
                return Result.Failure($"Error performing logon for Merchant [{merchant.MerchantName}]");
            }
            return Result.Success();
        }

        Random r = new Random();
        List<string> results = new List<string>();
        foreach (ContractResponse contract in contracts)
        {
            if (config.ContractNames.Contains(contract.Description) == false)
                continue;

            int numberOfSales = r.Next(2, 4);
            // Generate and send some sales
            Result saleResult = await t.SendSales(transactionDate, merchant, contract, numberOfSales, cancellationToken);

            if (saleResult.IsFailed)
            {
                results.Add(contract.OperatorName);
            }
        }

        if (results.Any())
        {
            return Result.Failure($"Error sending sales files for merchant [{merchant.MerchantName}] [{string.Join(",", results)}]");
        }
        return Result.Success();
    }

    public static async Task<Result> PerformSettlement(ITransactionDataGeneratorService t, DateTime dateTime, SettlementJobConfig config, CancellationToken cancellationToken)
    {
        Result result = await t.PerformSettlement(dateTime.Date, config.EstateId, cancellationToken);

        if (result.IsFailed)
        {
            return Result.Failure($"Error performing settlement for Estate Id [{config.EstateId}] and date [{dateTime:dd-MM-yyyy}]");
        }
        return Result.Success();
    }

    public static async Task ReplayParkedQueues(ReplayParkedQueueJobConfig config, CancellationToken cancellationToken)
    {
        EventStoreClientSettings clientSettings = EventStoreClientSettings.Create(config.EventStoreAddress);
        EventStorePersistentSubscriptionsClient client = new EventStorePersistentSubscriptionsClient(clientSettings);

        IEnumerable<PersistentSubscriptionInfo> subscriptions = await client.ListAllAsync(cancellationToken: cancellationToken);

        foreach (PersistentSubscriptionInfo persistentSubscriptionInfo in subscriptions)
        {
            if (persistentSubscriptionInfo.Stats.ParkedMessageCount > 0)
            {
                await client.ReplayParkedMessagesToStreamAsync(persistentSubscriptionInfo.EventSource,
                    persistentSubscriptionInfo.GroupName, cancellationToken: cancellationToken);
            }
        }
    }
}