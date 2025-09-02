using EventStore.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
using SecurityService.Client;
using Shared.General;
using Shared.Logger;
using SimpleResults;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.TickerQ.Database;
using TransactionProcessor.Client;
using TransactionProcessor.DataTransferObjects;
using TransactionProcessor.DataTransferObjects.Responses.Contract;
using TransactionProcessor.DataTransferObjects.Responses.Merchant;

namespace TransactionProcessing.SchedulerService.TickerQ.Jobs
{
    public class TickerFunctions
    {
        private readonly ISecurityServiceClient SecurityServiceClient;
        private readonly ITransactionProcessorClient TransactionProcessorClient;
        private readonly Func<String, String> BaseAddressFunc;
        private readonly SchedulerContext SchedulerContext;
        private readonly ITransactionDataGeneratorService TransactionDataGeneratorService;
        private readonly ServiceConfiguration BaseConfiguration;

        public TickerFunctions(ISecurityServiceClient securityServiceClient,
                               ITransactionProcessorClient transactionProcessorClient,
                               Func<String, String> baseAddressFunc,
                               SchedulerContext schedulerContext) {
            this.SecurityServiceClient = securityServiceClient;
            this.TransactionProcessorClient = transactionProcessorClient;
            this.BaseAddressFunc = baseAddressFunc;
            this.SchedulerContext = schedulerContext;

            // Get the base configuration
            this.BaseConfiguration= BuildBaseConfiguration();
            this.TransactionDataGeneratorService = CreateTransactionDataGenerator(this.BaseConfiguration.ClientId, this.BaseConfiguration.ClientSecret);
        }

        protected ITransactionDataGeneratorService CreateTransactionDataGenerator(String clientId, String clientSecret) {
            var runningModeConfig = ConfigurationReader.GetValueOrDefault("AppSettings", "RunningMode", "WhatIf");
            if (Enum.TryParse<RunningMode>(runningModeConfig, true, out RunningMode runningMode) == false) {
                throw new ApplicationException("Running Mode invalid");
            }

            ITransactionDataGeneratorService g = new TransactionDataGeneratorService(this.SecurityServiceClient,
                this.TransactionProcessorClient,
                this.BaseAddressFunc("TransactionProcessorApi"),
                this.BaseAddressFunc("FileProcessorApi"),
                this.BaseAddressFunc("TestHostApi"),
                clientId,
                clientSecret,
                runningMode);
            return g;
        }

        internal static ServiceConfiguration BuildBaseConfiguration() {
            String? clientId = ConfigurationReader.GetValueOrDefault<String>("BaseConfiguration", "ClientId", "");
            String? clientSecret = ConfigurationReader.GetValueOrDefault<String>("BaseConfiguration", "ClientSecret", "");
            String? fileProcessorApi = ConfigurationReader.GetValueOrDefault<String>("BaseConfiguration", "FileProcessorApi", "");
            String? testHostApi = ConfigurationReader.GetValueOrDefault<String>("BaseConfiguration", "TestHostApi", "");
            String? securityServiceApi = ConfigurationReader.GetValueOrDefault<String>("BaseConfiguration", "SecurityServiceApi", "");
            String? transactionProcessorApi = ConfigurationReader.GetValueOrDefault<String>("BaseConfiguration", "TransactionProcessorApi", "");
            String? eventStoreAddress = ConfigurationReader.GetValueOrDefault<String>("BaseConfiguration", "EventStoreAddress", "");

            List<String> validationErrors = new();
            if (String.IsNullOrEmpty(clientId)) {
                validationErrors.Add("ClientId is not configured.");
            }
            if (String.IsNullOrEmpty(clientSecret)) {
                validationErrors.Add("ClientSecret is not configured.");
            }
            if (String.IsNullOrEmpty(fileProcessorApi)) {
                validationErrors.Add("FileProcessorApi is not configured.");
            }
            if (String.IsNullOrEmpty(testHostApi)) {
                validationErrors.Add("TestHostApi is not configured.");
            }
            if (String.IsNullOrEmpty(securityServiceApi)) {
                validationErrors.Add("SecurityServiceApi is not configured.");
            }
            if (String.IsNullOrEmpty(transactionProcessorApi)) {
                validationErrors.Add("TransactionProcessorApi is not configured.");
            }
            if (String.IsNullOrEmpty(eventStoreAddress)) {
                validationErrors.Add("EventStoreAddress is not configured.");
            }

            if (validationErrors.Any()) {
                throw new InvalidOperationException("Configuration validation failed: " + String.Join(", ", validationErrors));
            }

            return new ServiceConfiguration(
                clientId,
                clientSecret,
                fileProcessorApi,
                securityServiceApi,
                testHostApi,
                transactionProcessorApi, eventStoreAddress);
        }

        [TickerFunction(functionName: "Replay Parked Queue")]
        public async Task ReplayParkedQueue(TickerFunctionContext<ReplayParkedQueueJobConfiguration> tickerContext, CancellationToken ct) {
            if (this.IsJobEnabled(tickerContext.Request) == false) {
                Logger.LogWarning("Replay Parked Queue Job is not enabled");
                return;
            }

            Result result = await Jobs.ReplayParkedQueues(this.BaseConfiguration.EventStoreAddress, ct);

            if (result.IsFailed) {
                throw new ApplicationException(result.Message);
            }

            Logger.LogWarning("Running Replay Parked Queue");
        }

        private Boolean IsJobEnabled<TConfiguration>(TConfiguration configuration) where TConfiguration : BaseConfiguration {
            if (configuration == null)
                return true;
            return configuration.IsEnabled;
        }

        [TickerFunction(functionName: "Make Float Credits")]
        public async Task MakeFloatCredits(TickerFunctionContext<MakeFloatCreditsJobConfiguration> tickerContext, CancellationToken ct) {
            if (this.IsJobEnabled(tickerContext.Request) == false)
            {
                Logger.LogWarning("Make Float Credits is not enabled");
                return;
            }
            Result result = await Jobs.MakeFloatCredits(this.TransactionDataGeneratorService, tickerContext.Request, ct);

            if (result.IsFailed)
            {
                throw new ApplicationException(result.Message);
            }

            Logger.LogWarning($"Running Make Float Credits for Estate Id [{tickerContext.Request.EstateId}]");
        }

        [TickerFunction(functionName: "Upload Transaction File")]
        public async Task UploadTransactionFile(TickerFunctionContext<UploadTransactionFileJobConfiguration> tickerContext, CancellationToken ct)
        {
            if (this.IsJobEnabled(tickerContext.Request) == false)
            {
                Logger.LogWarning("Upload Transaction File is not enabled");
                return;
            }
            String name = tickerContext.Request.MerchantId.ToString() switch
            {
                "af4d7c7c-9b8d-4e58-a12a-28d3e0b89df6" => "Demo Merchant 2",
                "ab1c99fb-1c6c-4694-9a32-b71be5d1da33" => "Demo Merchant 1",
                "8bc8434d-41f9-4cc3-83bc-e73f20c02e1d" => "Demo Merchant 3",
                _ => "Unknown Merchant"
            };

            Result result = await Jobs.GenerateFileUploads(this.TransactionDataGeneratorService, tickerContext.Request.EstateId, tickerContext.Request.MerchantId, tickerContext.Request.UserId, tickerContext.Request.ContractsToInclude, ct);
            if (result.IsFailed)
            {
                throw new ApplicationException(result.Message);
            }
            Logger.LogWarning($"Running Upload Transaction File for Merchant [{name}]");
        }

        [TickerFunction(functionName: "Process Merchant Settlement")]
        public async Task ProcessMerchantSettlement(TickerFunctionContext<ProcessSettlementJobConfiguration> tickerContext, CancellationToken ct) {
            if (this.IsJobEnabled(tickerContext.Request) == false)
            {
                Logger.LogWarning("Process Merchant Settlement Job is not enabled");
                return;
            }

            String name = tickerContext.Request.MerchantId.ToString() switch
            {
                "af4d7c7c-9b8d-4e58-a12a-28d3e0b89df6" => "Demo Merchant 2",
                "ab1c99fb-1c6c-4694-9a32-b71be5d1da33" => "Demo Merchant 1",
                "8bc8434d-41f9-4cc3-83bc-e73f20c02e1d" => "Demo Merchant 3",
                _ => "Unknown Merchant"
            };

            Result result = await Jobs.PerformSettlement(this.TransactionDataGeneratorService, DateTime.Today.Date.AddDays(-1), tickerContext.Request.EstateId, tickerContext.Request.MerchantId, ct);
            if (result.IsFailed)
            {
                throw new ApplicationException(result.Message);
            }
            Logger.LogWarning($"Running process Merchant Settlement for Merchant Id [{name}]");
        }

        [TickerFunction(functionName: "Generate Merchant Transactions")]
        public async Task GenerateMerchantTransactions(TickerFunctionContext<GenerateTransactionsJobConfiguration> tickerContext, CancellationToken ct) {
            if (this.IsJobEnabled(tickerContext.Request) == false)
            {
                Logger.LogWarning("Generate Merchant Transactions Job is not enabled");
                return;
            }
            String name = tickerContext.Request.MerchantId.ToString() switch {
                "af4d7c7c-9b8d-4e58-a12a-28d3e0b89df6" => "Demo Merchant 2",
                "ab1c99fb-1c6c-4694-9a32-b71be5d1da33" => "Demo Merchant 1",
                "8bc8434d-41f9-4cc3-83bc-e73f20c02e1d" => "Demo Merchant 3",
                _ => "Unknown Merchant"
            };

            Result result = await Jobs.GenerateSaleTransactions(this.TransactionDataGeneratorService, tickerContext.Request.EstateId, 
                tickerContext.Request.MerchantId, ct);
            if (result.IsFailed)
            {
                throw new ApplicationException(result.Message);
            }

            Logger.LogWarning($"Running Generate Merchant Transactions for Merchant [{name}]");
        }

        [TickerFunction(functionName: "Generate Merchant Logon")]
        public async Task GenerateMerchantLogon(TickerFunctionContext<GenerateTransactionsJobConfiguration> tickerContext, CancellationToken ct)
        {
            if (this.IsJobEnabled(tickerContext.Request) == false)
            {
                Logger.LogWarning("Generate Merchant Logon Job is not enabled");
                return;
            }
            String name = tickerContext.Request.MerchantId.ToString() switch
            {
                "af4d7c7c-9b8d-4e58-a12a-28d3e0b89df6" => "Demo Merchant 2",
                "ab1c99fb-1c6c-4694-9a32-b71be5d1da33" => "Demo Merchant 1",
                "8bc8434d-41f9-4cc3-83bc-e73f20c02e1d" => "Demo Merchant 3",
                _ => "Unknown Merchant"
            };

            Result result = await Jobs.GenerateLogonTransaction(this.TransactionDataGeneratorService, tickerContext.Request.EstateId, 
                tickerContext.Request.MerchantId, ct);
            if (result.IsFailed)
            {
                throw new ApplicationException(result.Message);
            }

            Logger.LogWarning($"Running Generate Merchant Logon for Merchant [{name}]");
        }

        //[TickerFunction(functionName: "Generate Merchant Statement")]
        //public Task GenerateMerchantStatement(TickerFunctionContext<MerchantStatementJobConfiguration> tickerContext, CancellationToken ct)
        //{
        //    return Task.CompletedTask;
        //}
    }

    public class Jobs {

        public static async Task<Result> PerformSettlement(ITransactionDataGeneratorService t,
                                                           DateTime dateTime,
                                                           Guid estateId, Guid merchantId,
                                                           CancellationToken cancellationToken)
        {
            Result result = await t.PerformMerchantSettlement(dateTime.Date, estateId, merchantId, cancellationToken);

            if (result.IsFailed)
            {
                return Result.Failure($"Error performing settlement for Estate Id [{estateId}] and Merchant Id [{merchantId}] and date [{dateTime:dd-MM-yyyy}]");
            }

            return Result.Success();
        }

        public static async Task<Result> MakeFloatCredits(ITransactionDataGeneratorService t,
                                                              MakeFloatCreditsJobConfiguration config,
                                                              CancellationToken cancellationToken)
        {
            List<string> results = new();

            foreach (DepositAmount configDepositAmount in config.DepositAmounts)
            {
                Result result = await t.MakeFloatDeposit(DateTime.Now, config.EstateId, configDepositAmount.ContractId, configDepositAmount.ProductId, configDepositAmount.Amount, cancellationToken);
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

        public static async Task<Result> GenerateFileUploads(ITransactionDataGeneratorService t,
                                                             Guid estateId, Guid merchantId,Guid userId, List<String> contractNames,
                                                             CancellationToken cancellationToken)
        {
            Result<MerchantResponse> merchantResult = await t.GetMerchant(estateId, merchantId, cancellationToken);

            if (merchantResult.IsFailed)
            {
                return Result.Failure($"No merchant returned for Estate Id [{estateId}] Merchant Id [{merchantId}]");
            }

            MerchantResponse merchant = merchantResult.Data;
            Result<List<ContractResponse>> getMerchantContractsResult = await t.GetMerchantContracts(merchant, cancellationToken);
            if (getMerchantContractsResult.IsFailed)
            {
                Console.WriteLine($"Failed to get merchant contracts: {getMerchantContractsResult.Message}");
                return Result.Failure();
            }

            DateTime fileDate = DateTime.Now.Date;
            List<string> results = new List<string>();
            foreach (ContractResponse contract in getMerchantContractsResult.Data)
            {
                if (contractNames.Contains(contract.Description) == false)
                    continue;

                // Generate a file and upload
                Result result = await t.SendUploadFile(fileDate, contract, merchant, userId, cancellationToken);

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

        public static async Task<Result> ReplayParkedQueues(String eventStoreAddress,
                                                        CancellationToken cancellationToken)
        {
            try
            {
                EventStoreClientSettings clientSettings = EventStoreClientSettings.Create(eventStoreAddress);
                EventStorePersistentSubscriptionsClient client = new(clientSettings);

                IEnumerable<PersistentSubscriptionInfo> subscriptions = await client.ListAllAsync(cancellationToken: cancellationToken);
                List<PersistentSubscriptionInfo> subscriptionList = subscriptions.ToList();
                if (subscriptionList.ToList().Any() == false)
                {
                    return Result.Success("No subscriptions found to replay parked messages.");
                }

                foreach (PersistentSubscriptionInfo persistentSubscriptionInfo in subscriptionList)
                {
                    Logger.LogInformation($"About to process subscription [{persistentSubscriptionInfo.GroupName}] on stream [{persistentSubscriptionInfo.EventSource}]");
                    if (persistentSubscriptionInfo.Stats.ParkedMessageCount > 0)
                    {
                        Logger.LogWarning($"[{persistentSubscriptionInfo.Stats.ParkedMessageCount}] parked messages to be replayed.");
                        await client.ReplayParkedMessagesToStreamAsync(persistentSubscriptionInfo.EventSource, persistentSubscriptionInfo.GroupName, cancellationToken: cancellationToken);
                        Logger.LogWarning($"{persistentSubscriptionInfo.Stats.ParkedMessageCount} Parked messages replayed for subscription [{persistentSubscriptionInfo.GroupName}] on stream [{persistentSubscriptionInfo.EventSource}].");
                    }
                    else
                    {
                        Logger.LogWarning($"{persistentSubscriptionInfo.Stats.ParkedMessageCount} Parked messages replayed for subscription [{persistentSubscriptionInfo.GroupName}] on stream [{persistentSubscriptionInfo.EventSource}].");
                    }
                }
                return Result.Success("Parked messages replayed successfully.");
            }
            catch (Exception ex)
            {
                return Result.Failure($"Error replaying parked messages: {ex.Message}");
            }
        }

        public static async Task<Result> GenerateSaleTransactions(ITransactionDataGeneratorService t,
                                                          Guid estateId, Guid merchantId,
                                                          CancellationToken cancellationToken)
        {
            // get the merchant
            Result<MerchantResponse> merchantResult = await t.GetMerchant(estateId, merchantId, cancellationToken);

            if (merchantResult.IsFailed)
            {
                return Result.Failure($"Error getting Merchant Id [{merchantId}] for Estate Id [{estateId}]");
            }

            MerchantResponse merchant = merchantResult.Data;

            DateTime transactionDate = DateTime.Now;

            // Get the merchants contracts
            Result<List<ContractResponse>> contractResult = await t.GetMerchantContracts(merchant, cancellationToken);

            if (contractResult.IsFailed)
            {
                return Result.Failure($"Error getting contracts for Merchant [{merchant.MerchantName}]");
            }

            List<ContractResponse> contracts = contractResult.Data;
            if (contracts.Any() == false)
            {
                return Result.Failure($"No contracts returned for Merchant [{merchant.MerchantName}]");
            }

            Random r = new Random();
            List<string> results = new List<string>();
            foreach (ContractResponse contract in contracts) {
                int numberOfSales = r.Next(1, 2);
                // Generate and send some sales
                Result saleResult = await t.SendSales(transactionDate, merchant, contract, numberOfSales, cancellationToken);

                if (saleResult.IsFailed)
                {
                    results.Add(contract.OperatorName);
                }
            }

            if (results.Any())
            {
                return Result.Failure($"Error sending sales for merchant [{merchant.MerchantName}] [{string.Join(",", results)}]");
            }

            return Result.Success();
        }

        public static async Task<Result> GenerateLogonTransaction(ITransactionDataGeneratorService t,
                                                                  Guid estateId,
                                                                  Guid merchantId,
                                                                  CancellationToken cancellationToken) {
            // get the merchant
            Result<MerchantResponse> merchantResult = await t.GetMerchant(estateId, merchantId, cancellationToken);

            if (merchantResult.IsFailed) {
                return Result.Failure($"Error getting Merchant Id [{merchantId}] for Estate Id [{estateId}]");
            }

            MerchantResponse merchant = merchantResult.Data;

            DateTime transactionDate = DateTime.Now;

            // Do a logon transaction for the merchant
            Result<SerialisedMessage> logonResult = await t.PerformMerchantLogon(transactionDate, merchant, cancellationToken);

            if (logonResult.IsFailed) {
                return Result.Failure($"Error performing logon for Merchant [{merchant.MerchantName}]");
            }

            return Result.Success();
        }
    }
}
