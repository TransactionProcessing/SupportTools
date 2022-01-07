namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Responses;
    using Newtonsoft.Json;
    using Quartz;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using TransactionProcessor.Client;
    using TransactionProcessor.DataTransferObjects;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    public class GenerateTransactionsJob : IJob
    {
        #region Fields

        /// <summary>
        /// The base address function
        /// </summary>
        private static Func<String, String> baseAddressFunc;

        /// <summary>
        /// The bootstrapper
        /// </summary>
        private readonly IBootstrapper Bootstrapper;

        /// <summary>
        /// The estate client
        /// </summary>
        private IEstateClient EstateClient;

        /// <summary>
        /// The security service client
        /// </summary>
        private ISecurityServiceClient SecurityServiceClient;

        /// <summary>
        /// The transaction processor client
        /// </summary>
        private ITransactionProcessorClient TransactionProcessorClient;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="GenerateTransactionsJob" /> class.
        /// </summary>
        /// <param name="bootstrapperResolver">The bootstrapper resolver.</param>
        public GenerateTransactionsJob(Func<String, IBootstrapper> bootstrapperResolver)
        {
            this.Bootstrapper = bootstrapperResolver(nameof(GenerateTransactionsJob));
        }

        #endregion

        #region Methods

        /// <summary>
        /// Called by the <see cref="T:Quartz.IScheduler" /> when a <see cref="T:Quartz.ITrigger" />
        /// fires that is associated with the <see cref="T:Quartz.IJob" />.
        /// </summary>
        /// <param name="context">The execution context.</param>
        /// <remarks>
        /// The implementation may wish to set a  result object on the
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to
        /// <see cref="T:Quartz.IJobListener" />s or
        /// <see cref="T:Quartz.ITriggerListener" />s that are watching the job's
        /// execution.
        /// </remarks>
        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                this.Bootstrapper.ConfigureServices(context);

                Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
                Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
                Boolean requireLogon = context.MergedJobDataMap.GetBooleanValueFromString("requireLogon");

                this.SecurityServiceClient = this.Bootstrapper.GetService<ISecurityServiceClient>();
                this.TransactionProcessorClient = this.Bootstrapper.GetService<ITransactionProcessorClient>();
                this.EstateClient = this.Bootstrapper.GetService<IEstateClient>();

                await this.GenerateTransactions(estateId, merchantId, requireLogon, context.CancellationToken);
            }
            catch(Exception e)
            {
                // TODO: Log the error
            }
        }

        /// <summary>
        /// Creates the sale requests.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <param name="merchant">The merchant.</param>
        /// <param name="dateTime">The date time.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private async Task<List<SaleTransactionRequest>> CreateSaleRequests(String accessToken,
                                                                            MerchantResponse merchant,
                                                                            DateTime dateTime,
                                                                            CancellationToken cancellationToken)
        {
            List<ContractResponse> contracts = await this.EstateClient.GetMerchantContracts(accessToken, merchant.EstateId, merchant.MerchantId, cancellationToken);

            List<SaleTransactionRequest> saleRequests = new List<SaleTransactionRequest>();

            Random r = new Random();
            Int32 transactionNumber = 1;
            // get a number of transactions to generate
            Int32 numberOfSales = r.Next(2, 4);

            for (Int32 i = 0; i < numberOfSales; i++)
            {
                // Pick a contract
                ContractResponse contract = contracts[r.Next(0, contracts.Count)];

                // Pick a product
                ContractProduct product = contract.Products[r.Next(0, contract.Products.Count)];

                Decimal amount = 0;
                if (product.Value.HasValue)
                {
                    amount = product.Value.Value;
                }
                else
                {
                    // generate an amount
                    amount = r.Next(9, 250);
                }

                // Generate the time
                DateTime transactionDateTime = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, 0);

                // Build the metadata
                Dictionary<String, String> requestMetaData = new Dictionary<String, String>();
                requestMetaData.Add("Amount", amount.ToString());

                var productType = GenerateTransactionsJob.GetProductType(contract.OperatorName);
                String operatorName = GenerateTransactionsJob.GetOperatorName(contract);
                if (productType == ProductType.MobileTopup)
                {
                    requestMetaData.Add("CustomerAccountNumber", "1234567890");
                }
                else if (productType == ProductType.Voucher)
                {
                    requestMetaData.Add("RecipientMobile", "1234567890");
                }

                String deviceIdentifier = merchant.Devices.Single().Value;

                SaleTransactionRequest request = new SaleTransactionRequest
                                                 {
                                                     AdditionalTransactionMetadata = requestMetaData,
                                                     ContractId = contract.ContractId,
                                                     CustomerEmailAddress = string.Empty,
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

        /// <summary>
        /// Does the logon transaction.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <param name="merchant">The merchant.</param>
        /// <param name="date">The date.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async Task DoLogonTransaction(String accessToken,
                                              MerchantResponse merchant,
                                              DateTime date,
                                              CancellationToken cancellationToken)
        {
            String deviceIdentifier = merchant.Devices.Single().Value;
            LogonTransactionRequest logonTransactionRequest = new LogonTransactionRequest
                                                              {
                                                                  DeviceIdentifier = deviceIdentifier,
                                                                  EstateId = merchant.EstateId,
                                                                  MerchantId = merchant.MerchantId,
                                                                  TransactionDateTime = date.AddMinutes(1),
                                                                  TransactionNumber = "1",
                                                                  TransactionType = "Logon"
                                                              };

            SerialisedMessage requestSerialisedMessage = new SerialisedMessage();
            requestSerialisedMessage.Metadata.Add("estate_id", merchant.EstateId.ToString());
            requestSerialisedMessage.Metadata.Add("merchant_id", merchant.MerchantId.ToString());
            requestSerialisedMessage.SerialisedData = JsonConvert.SerializeObject(logonTransactionRequest,
                                                                                  new JsonSerializerSettings
                                                                                  {
                                                                                      TypeNameHandling = TypeNameHandling.All
                                                                                  });

            SerialisedMessage responseSerialisedMessage =
                await this.TransactionProcessorClient.PerformTransaction(accessToken, requestSerialisedMessage, cancellationToken);

            LogonTransactionResponse logonTransactionResponse = JsonConvert.DeserializeObject<LogonTransactionResponse>(responseSerialisedMessage.SerialisedData);
        }

        /// <summary>
        /// Does the sale transaction.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <param name="saleTransactionRequest">The sale transaction request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async Task DoSaleTransaction(String accessToken,
                                             SaleTransactionRequest saleTransactionRequest,
                                             CancellationToken cancellationToken)
        {
            try
            {
                SerialisedMessage requestSerialisedMessage = new SerialisedMessage();
                requestSerialisedMessage.Metadata.Add("estate_id", saleTransactionRequest.EstateId.ToString());
                requestSerialisedMessage.Metadata.Add("merchant_id", saleTransactionRequest.MerchantId.ToString());
                requestSerialisedMessage.SerialisedData = JsonConvert.SerializeObject(saleTransactionRequest,
                                                                                      new JsonSerializerSettings
                                                                                      {
                                                                                          TypeNameHandling = TypeNameHandling.All
                                                                                      });

                SerialisedMessage responseSerialisedMessage =
                    await this.TransactionProcessorClient.PerformTransaction(accessToken, requestSerialisedMessage, cancellationToken);

                SaleTransactionResponse saleTransactionResponse = JsonConvert.DeserializeObject<SaleTransactionResponse>(responseSerialisedMessage.SerialisedData);
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }
        }

        /// <summary>
        /// Generates the transactions.
        /// </summary>
        /// <param name="estateId">The estate identifier.</param>
        /// <param name="merchantId">The merchant identifier.</param>
        /// <param name="requiresLogon">if set to <c>true</c> [requires logon].</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private async Task GenerateTransactions(Guid estateId,
                                                                            Guid merchantId,
                                                                            Boolean requiresLogon,
                                                                            CancellationToken cancellationToken)
        {
            DateTime transactionDate = DateTime.Now;

            // get a token
            String accessToken = await this.GetToken(cancellationToken);

            // get the merchant
            MerchantResponse merchant = await this.EstateClient.GetMerchant(accessToken, estateId, merchantId, cancellationToken);

            Int32 transactionCount = 0;

            if (requiresLogon)
            {
                // Do a logon transaction for the merchant
                await this.DoLogonTransaction(accessToken, merchant, transactionDate, cancellationToken);

                Console.WriteLine($"Logon sent for Merchant [{merchant.MerchantName}]");
            }

            // Now generate some sales
            List<SaleTransactionRequest> saleRequests = await this.CreateSaleRequests(accessToken, merchant, transactionDate, cancellationToken);

            // Work out how much of a deposit the merchant needs (minus 1 sale)
            IEnumerable<Dictionary<String, String>> metadata = saleRequests.Select(s => s.AdditionalTransactionMetadata);
            List<String> amounts = metadata.Select(m => m["Amount"]).ToList();

            Decimal depositAmount = amounts.TakeLast(amounts.Count - 1).Sum(a => decimal.Parse(a));

            await this.MakeMerchantDeposit(accessToken, merchant, depositAmount, transactionDate, cancellationToken);

            // Now send the sales
            saleRequests = saleRequests.OrderBy(s => s.TransactionDateTime).ToList();
            foreach (SaleTransactionRequest saleTransactionRequest in saleRequests)
            {
                await this.DoSaleTransaction(accessToken, saleTransactionRequest, cancellationToken);
                Console.WriteLine($"Sale sent for Merchant [{merchant.MerchantName}]");
                transactionCount++;
            }
        }

        /// <summary>
        /// Gets the name of the operator.
        /// </summary>
        /// <param name="contractResponse">The contract response.</param>
        /// <returns></returns>
        private static String GetOperatorName(ContractResponse contractResponse)
        {
            String operatorName = null;
            ProductType productType = GenerateTransactionsJob.GetProductType(contractResponse.OperatorName);
            switch(productType)
            {
                case ProductType.Voucher:
                    operatorName = contractResponse.Description;
                    break;
                default:
                    operatorName = contractResponse.OperatorName;
                    break;
            }

            return operatorName;
        }

        /// <summary>
        /// Gets the type of the product.
        /// </summary>
        /// <param name="operatorName">Name of the operator.</param>
        /// <returns></returns>
        private static ProductType GetProductType(String operatorName)
        {
            ProductType productType = ProductType.NotSet;
            switch(operatorName)
            {
                case "Safaricom":
                    productType = ProductType.MobileTopup;
                    break;
                case "Voucher":
                    productType = ProductType.Voucher;
                    break;
            }

            return productType;
        }

        /// <summary>
        /// Gets the token.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private async Task<String> GetToken(CancellationToken cancellationToken)
        {
            // Get a token to talk to the estate service
            String clientId = "serviceClient";
            String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";

            TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);

            return token.AccessToken;
        }

        /// <summary>
        /// Makes the merchant deposit.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <param name="merchant">The merchant.</param>
        /// <param name="depositAmount">The deposit amount.</param>
        /// <param name="dateTime">The date time.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        private async Task MakeMerchantDeposit(String accessToken,
                                               MerchantResponse merchant,
                                               Decimal depositAmount,
                                               DateTime dateTime,
                                               CancellationToken cancellationToken)
        {
            if (depositAmount == 0)
                return;

            await this.EstateClient.MakeMerchantDeposit(accessToken,
                                                        merchant.EstateId,
                                                        merchant.MerchantId,
                                                        new MakeMerchantDepositRequest
                                                        {
                                                            Amount = depositAmount,
                                                            DepositDateTime = dateTime.AddSeconds(55),
                                                            Reference = "Test Data Gen Deposit"
                                                        },
                                                        cancellationToken);
            Console.WriteLine($"Deposit made for Merchant [{merchant.MerchantName}]");
        }

        #endregion
    }
}