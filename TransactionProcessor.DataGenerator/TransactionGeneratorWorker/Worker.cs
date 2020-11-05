using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TransactionGeneratorWorker
{
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Responses;
    using Newtonsoft.Json;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using Shared.General;
    using Shared.Logger;
    using TransactionProcessor.Client;
    using TransactionProcessor.DataTransferObjects;

    public class Worker : BackgroundService
    {
        private readonly IEstateClient EstateClient;

        private readonly ISecurityServiceClient SecurityServiceClient;

        private readonly ITransactionProcessorClient TransactionProcessorClient;

        public Worker(IEstateClient estateClient,
                      ISecurityServiceClient securityServiceClient,
                      ITransactionProcessorClient transactionProcessorClient)
        {
            this.EstateClient = estateClient;
            this.SecurityServiceClient = securityServiceClient;
            this.TransactionProcessorClient = transactionProcessorClient;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            // Setup what we need here
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            // clean up any resources here
            return base.StopAsync(cancellationToken);
        }

        private DateTime LastRunDateTime;

        private DateTime CurrentRunDateTime;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Int32 pollingTimeoutInMinutes = 0;
                try
                {
                    pollingTimeoutInMinutes = Int32.Parse(ConfigurationReader.GetValue("AppSettings", "PollingTimeoutInMinutes"));
                    Boolean requireLogon = false;
                    this.CurrentRunDateTime = DateTime.Now;
                    if (this.LastRunDateTime == DateTime.MinValue)
                    {
                        this.LastRunDateTime = DateTime.Now.AddMinutes(pollingTimeoutInMinutes * -1);
                        requireLogon = true;
                    }
                    else
                    {
                        // If We have changed date, need to fire a logon transaction
                        requireLogon = this.LastRunDateTime == DateTime.MinValue || (this.LastRunDateTime.Date < this.CurrentRunDateTime.Date);

                    }
                    

                    // Get the security token
                    await this.GetToken(stoppingToken);

                    // Get the estates to process
                    String estateList = ConfigurationReader.GetValue("AppSettings", "EstateList");

                    // Check if we are processing more than 1 estate
                    String[] estateIds = estateList.Split(",");
                    
                    foreach (String estateId in estateIds)
                    {
                        await this.ProcessEstate(Guid.Parse(estateId), requireLogon, stoppingToken);
                    }

                    // Store the last run date time
                    this.LastRunDateTime = DateTime.Now;
                }
                catch(Exception e)
                {
                    // Do the required logging on en exception but do not bring down the process
                    Logger.LogError(e);
                }

                await Task.Delay(TimeSpan.FromMinutes(pollingTimeoutInMinutes), stoppingToken);
            }
        }

        private async Task ProcessEstate(Guid estateId,
                                         Boolean requireLogon,
                                         CancellationToken cancellationToken)
        {
            // Get the the merchant list for the estate
            List<MerchantResponse> merchants = await this.EstateClient.GetMerchants(this.TokenResponse.AccessToken, estateId, CancellationToken.None);

            // Only use merchants that have a device
            merchants = merchants.Where(m => m.Devices != null && m.Devices.Any()).ToList();
            
            List<Task> tasks = new List<Task>();
            foreach (MerchantResponse merchantResponse in merchants)
            {
                tasks.Add(this.GenerateTransactions(merchantResponse,requireLogon));
            }

            Task.WaitAll(tasks.ToArray(), cancellationToken);
        }

        private async Task MakeMerchantDeposit(MerchantResponse merchant,
                                               Decimal depositAmount,
                                               DateTime dateTime)
        {
            try
            {
                await this.EstateClient.MakeMerchantDeposit(this.TokenResponse.AccessToken,
                                                            merchant.EstateId,
                                                            merchant.MerchantId,
                                                            new MakeMerchantDepositRequest
                                                            {
                                                                Amount = depositAmount,
                                                                DepositDateTime = dateTime,
                                                                Reference = "Test Data Gen Deposit",
                                                                Source = MerchantDepositSource.Manual
                                                            },
                                                            CancellationToken.None);
                Logger.LogInformation($"Deposit made for Merchant [{merchant.MerchantName}]");

            }
            catch(Exception e)
            {
                Logger.LogError(e);
            }
        }

        private async Task GenerateTransactions(MerchantResponse merchant,
                                                Boolean requireLogon)
        {
            
            if (requireLogon)
            {
                // Perform a logon transaction for the merchant
                await this.PerformLogonTransaction(merchant, this.LastRunDateTime.AddSeconds(30));
            }
            
            // Now generate some sales
            List<SaleTransactionRequest> saleRequests = await this.CreateSaleRequests(merchant, this.LastRunDateTime.AddSeconds(30), this.CurrentRunDateTime);

            // Work out how much of a deposit the merchant needs (minus 1 sale)
            IEnumerable<Dictionary<String, String>> metadata = saleRequests.Select(s => s.AdditionalTransactionMetadata);
            List<String> amounts = metadata.Select(m => m["Amount"]).ToList();

            Decimal depositAmount = amounts.TakeLast(amounts.Count - 1).Sum(a => Decimal.Parse(a));

            await this.MakeMerchantDeposit(merchant, depositAmount, this.LastRunDateTime.AddSeconds(15));

            // Now send the sales
            saleRequests = saleRequests.OrderBy(s => s.TransactionDateTime).ToList();
            foreach (SaleTransactionRequest saleTransactionRequest in saleRequests)
            {
                await this.SendSaleTransaction(merchant,saleTransactionRequest);
            }
        }

        private async Task SendSaleTransaction(MerchantResponse merchant, SaleTransactionRequest saleTransactionRequest)
        {
            try
            {
                SerialisedMessage requestSerialisedMessage = new SerialisedMessage();
                requestSerialisedMessage.Metadata.Add("EstateId", saleTransactionRequest.EstateId.ToString());
                requestSerialisedMessage.Metadata.Add("MerchantId", saleTransactionRequest.MerchantId.ToString());
                requestSerialisedMessage.SerialisedData = JsonConvert.SerializeObject(saleTransactionRequest,
                                                                                      new JsonSerializerSettings
                                                                                      {
                                                                                          TypeNameHandling = TypeNameHandling.All
                                                                                      });

                SerialisedMessage responseSerialisedMessage =
                    await this.TransactionProcessorClient.PerformTransaction(this.TokenResponse.AccessToken, requestSerialisedMessage, CancellationToken.None);

                SaleTransactionResponse saleTransactionResponse = JsonConvert.DeserializeObject<SaleTransactionResponse>(responseSerialisedMessage.SerialisedData);

                if (saleTransactionResponse.ResponseCode != "0000")
                {
                    Logger.LogWarning($"Sale failed for merchant {merchant.MerchantName} [{JsonConvert.SerializeObject(saleTransactionResponse)}]");
                }
                else
                {
                    Logger.LogInformation($"Sale successful for merchant {merchant.MerchantName} Response Message: [{saleTransactionResponse.ResponseMessage}]");
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }


        private async Task PerformLogonTransaction(MerchantResponse merchant, DateTime transactionDateTime)
        {
            try
            {
                String deviceIdentifier = merchant.Devices.Single().Value;
                LogonTransactionRequest logonTransactionRequest = new LogonTransactionRequest
                                                                  {
                                                                      DeviceIdentifier = deviceIdentifier,
                                                                      EstateId = merchant.EstateId,
                                                                      MerchantId = merchant.MerchantId,
                                                                      TransactionDateTime = transactionDateTime,
                                                                      TransactionNumber = "1",
                                                                      TransactionType = "Logon"
                                                                  };

                SerialisedMessage requestSerialisedMessage = new SerialisedMessage();
                requestSerialisedMessage.Metadata.Add("EstateId", merchant.EstateId.ToString());
                requestSerialisedMessage.Metadata.Add("MerchantId", merchant.MerchantId.ToString());
                requestSerialisedMessage.SerialisedData = JsonConvert.SerializeObject(logonTransactionRequest,
                                                                                      new JsonSerializerSettings
                                                                                      {
                                                                                          TypeNameHandling = TypeNameHandling.All
                                                                                      });

                SerialisedMessage responseSerialisedMessage =
                    await this.TransactionProcessorClient.PerformTransaction(this.TokenResponse.AccessToken, requestSerialisedMessage, CancellationToken.None);

                LogonTransactionResponse logonTransactionResponse = JsonConvert.DeserializeObject<LogonTransactionResponse>(responseSerialisedMessage.SerialisedData);

                if (logonTransactionResponse.ResponseCode != "0000")
                {
                    Logger.LogWarning($"Logon failed for merchant {merchant.MerchantName} [{JsonConvert.SerializeObject(logonTransactionResponse)}]");
                }
                else
                {
                    Logger.LogInformation($"Logon successful for merchant {merchant.MerchantName} Response Message: [{logonTransactionResponse.ResponseMessage}]");
                }
            }
            catch(Exception e)
            {
                Logger.LogError(e);
            }
        }

        private TokenResponse TokenResponse;

        private async Task GetToken(CancellationToken cancellationToken)
        {
            // Get a token to talk to the estate service
            String clientId = ConfigurationReader.GetValue("AppSettings", "ClientId");
            String clientSecret = ConfigurationReader.GetValue("AppSettings", "ClientSecret");

            if (this.TokenResponse == null)
            {
                TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);
                this.TokenResponse = token;
            }

            if (this.TokenResponse.Expires.UtcDateTime.Subtract(DateTime.UtcNow) < TimeSpan.FromMinutes(2))
            {
                TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);
                this.TokenResponse = token;
            }
        }

        private async Task<List<SaleTransactionRequest>> CreateSaleRequests(MerchantResponse merchant,
                                                                            DateTime startDateTime,
                                                                            DateTime endDateTime)
        {
            List<ContractResponse> contracts =
                await this.EstateClient.GetMerchantContracts(this.TokenResponse.AccessToken, merchant.EstateId, merchant.MerchantId, CancellationToken.None);

            List<SaleTransactionRequest> saleRequests = new List<SaleTransactionRequest>();

            Random r = new Random();
            Int32 transactionNumber = 1;

            // Get number of minutes difference
            TimeSpan dateTimeDiff = endDateTime.Subtract(startDateTime);

            // get a number of transactions to generate
            Int32 numberOfSales = r.Next(3, dateTimeDiff.Minutes);

            for (Int32 i = 0; i < numberOfSales; i++)
            {
                // Pick a contract
                ContractResponse contract = contracts[r.Next(0, contracts.Count)];

                // Pick a product
                ContractProduct product = contract.Products[r.Next(0, contract.Products.Count)];

                Decimal amount = 0;
                if (product.Value.HasValue)
                {
                    amount = product.Value.Value * 10;
                }
                else
                {
                    // generate an amount
                    amount = r.Next(1000, 10000);
                }
                
                DateTime transactionDateTime = new DateTime(this.CurrentRunDateTime.Year,
                                                            this.CurrentRunDateTime.Month,
                                                            this.CurrentRunDateTime.Day,
                                                            this.CurrentRunDateTime.Hour,
                                                            this.CurrentRunDateTime.Minute,
                                                            0);

                // Build the metadata
                Dictionary<String, String> requestMetaData = new Dictionary<String, String>();
                requestMetaData.Add("Amount", amount.ToString());
                requestMetaData.Add("CustomerAccountNumber", "1234567890");

                String deviceIdentifier = merchant.Devices.Single().Value;

                SaleTransactionRequest request = new SaleTransactionRequest
                {
                    AdditionalTransactionMetadata = requestMetaData,
                    ContractId = contract.ContractId,
                    CustomerEmailAddress = String.Empty,
                    DeviceIdentifier = deviceIdentifier,
                    MerchantId = merchant.MerchantId,
                    EstateId = merchant.EstateId,
                    TransactionType = "Sale",
                    TransactionDateTime = transactionDateTime.AddSeconds(r.Next(0, 59)),
                    TransactionNumber = transactionNumber.ToString(),
                    OperatorIdentifier = contract.OperatorName,
                    ProductId = product.ProductId
                };

                saleRequests.Add(request);
                transactionNumber++;
            }

            return saleRequests;
        }
    }
}
