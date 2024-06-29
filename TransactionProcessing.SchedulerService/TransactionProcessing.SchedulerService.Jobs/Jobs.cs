namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataGeneration;
using EstateManagement.DataTransferObjects.Responses.Contract;
using EstateManagement.DataTransferObjects.Responses.Merchant;
using EventStore.Client;
using MessagingService.Client;
using MessagingService.DataTransferObjects;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic;
using Newtonsoft.Json;
using Quartz;
using Shared.Logger;

public static class Jobs{
    public static async Task GenerateMerchantStatements(ITransactionDataGenerator t, MerchantStatementJobConfig config, CancellationToken cancellationToken){
        List<MerchantResponse> merchants = await t.GetMerchants(config.EstateId, cancellationToken);

        if (merchants.Any() == false){
            throw new JobExecutionException($"No merchants returned for Estate [{config.EstateId}]");
        }

        List<String> results = new List<String>();
        foreach (MerchantResponse merchantResponse in merchants)
        {
            Boolean success = await t.GenerateMerchantStatement(merchantResponse.EstateId, merchantResponse.MerchantId, DateTime.Now, cancellationToken);
            if (success == false){
                results.Add(merchantResponse.MerchantName);
            }
        }

        if (results.Any()){
            throw new JobExecutionException($"Error generating statements for merchants [{String.Join(",", results)}]");
        }
    }

    public static async Task GenerateFileUploads(ITransactionDataGenerator t, FileUploadJobConfig config, CancellationToken cancellationToken)
    {
        MerchantResponse merchant = await t.GetMerchant(config.EstateId, config.MerchantId, cancellationToken);

        if (merchant == default)
        {
            throw new JobExecutionException($"No merchant returned for Estate Id [{config.EstateId}] Merchant Id [{config.MerchantId}]");
        }

        List<ContractResponse> contracts = await t.GetMerchantContracts(merchant, cancellationToken);
        DateTime fileDate = DateTime.Now.Date;
        List<String> results = new List<String>();
        foreach (ContractResponse contract in contracts)
        {
            if (config.ContractNames.Contains(contract.Description) == false)
                continue;

            // Generate a file and upload
            Boolean success = await t.SendUploadFile(fileDate, contract, merchant,config.UserId, cancellationToken);

            if (success == false)
            {
                results.Add(contract.OperatorName);
            }
        }

        if (results.Any())
        {
            throw new JobExecutionException($"Error uploading files for merchant [{merchant.MerchantName}] [{String.Join(",", results)}]");
        }
    }

    public static async Task GenerateTransactions(ITransactionDataGenerator t, TransactionJobConfig config, CancellationToken cancellationToken){
        // get the merchant
        MerchantResponse merchant = await t.GetMerchant(config.EstateId, config.MerchantId, cancellationToken);

        if (merchant == default){
            throw new JobExecutionException($"Error getting Merchant Id [{config.MerchantId}] for Estate Id [{config.EstateId}]");
        }

        DateTime transactionDate = DateTime.Now;

        // Get the merchants contracts
        List<ContractResponse> contracts = await t.GetMerchantContracts(merchant, cancellationToken);

        if (contracts.Any() == false)
        {
            throw new JobExecutionException($"No contracts returned for Merchant [{merchant.MerchantName}]");
        }

        if (config.IsLogon)
        {
            // Do a logon transaction for the merchant
            Boolean logonSuccess = await t.PerformMerchantLogon(transactionDate, merchant, cancellationToken);

            if (logonSuccess == false)
            {
                throw new JobExecutionException($"Error performing logon for Merchant [{merchant.MerchantName}]");
            }
            return;
        }

        Random r = new Random();
        List<String> results = new List<String>();
        foreach (ContractResponse contract in contracts)
        {
            if (config.ContractNames.Contains(contract.Description) == false)
                continue;

            Int32 numberOfSales = r.Next(2, 4);
            // Generate and send some sales
            Boolean success = await t.SendSales(transactionDate, merchant, contract, numberOfSales, cancellationToken);

            if (success == false)
            {
                results.Add(contract.OperatorName);
            }
        }

        if (results.Any())
        {
            throw new JobExecutionException($"Error sending sales files for merchant [{merchant.MerchantName}] [{String.Join(",", results)}]");
        }
    }

    public static async Task PerformSettlement(ITransactionDataGenerator t, DateTime dateTime, SettlementJobConfig config, CancellationToken cancellationToken)
    {
        Boolean success = await t.PerformSettlement(dateTime.Date, config.EstateId, cancellationToken);

        if (success == false){
            throw new JobExecutionException($"Error performing settlement for Estate Id [{config.EstateId}] and date [{dateTime:dd-MM-yyyy}]");
        }
    }
    
    public static async Task ReplayParkedQueues(ReplayParkedQueueJobConfig config, CancellationToken cancellationToken)
    {
        EventStoreClientSettings clientSettings = EventStoreClientSettings.Create(config.EventStoreAddress);
        EventStore.Client.EventStorePersistentSubscriptionsClient client = new EventStorePersistentSubscriptionsClient(clientSettings);
        
        IEnumerable<PersistentSubscriptionInfo> subscriptions = await client.ListAllAsync(cancellationToken: cancellationToken);

        foreach (var persistentSubscriptionInfo in subscriptions)
        {
            if (persistentSubscriptionInfo.Stats.ParkedMessageCount > 0)
            {
                await client.ReplayParkedMessagesToStreamAsync(persistentSubscriptionInfo.EventSource,
                    persistentSubscriptionInfo.GroupName, cancellationToken: cancellationToken);
            }
        }
    }
}