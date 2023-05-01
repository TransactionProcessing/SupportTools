namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using DataGeneration;
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
        
        private Func<String, String> baseAddressFunc;

        private readonly IBootstrapper Bootstrapper;

        private IEstateClient EstateClient;

        private ISecurityServiceClient SecurityServiceClient;

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
        
        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                this.Bootstrapper.ConfigureServices(context);

                String clientId = context.MergedJobDataMap.GetString("ClientId");
                String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
                Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
                Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
                Boolean requireLogon = context.MergedJobDataMap.GetBooleanValueFromString("requireLogon");
                
                this.SecurityServiceClient = this.Bootstrapper.GetService<ISecurityServiceClient>();
                this.TransactionProcessorClient = this.Bootstrapper.GetService<ITransactionProcessorClient>();
                this.EstateClient = this.Bootstrapper.GetService<IEstateClient>();
                this.baseAddressFunc = this.Bootstrapper.GetService<Func<String, String>>();

                ITransactionDataGenerator g = new TransactionDataGenerator(this.SecurityServiceClient,
                                                                           this.EstateClient,
                                                                           this.TransactionProcessorClient,
                                                                           this.baseAddressFunc("FileProcessorApi"),
                                                                           this.baseAddressFunc("TestHostApi"),
                                                                           clientId,
                                                                           clientSecret,
                                                                           RunningMode.Live);

                // get a token
                String accessToken = await this.GetToken(clientId,
                                                         clientSecret, context.CancellationToken);

                // get the merchant
                MerchantResponse merchant = await this.EstateClient.GetMerchant(accessToken, estateId, merchantId, context.CancellationToken);

                Int32 transactionCount = 0;
                DateTime transactionDate = DateTime.Now;

                if (requireLogon)
                {
                    // Do a logon transaction for the merchant
                    await g.PerformMerchantLogon(transactionDate, merchant, context.CancellationToken);

                    // Get the merchants contracts
                    List<ContractResponse> contracts = await g.GetMerchantContracts(merchant, context.CancellationToken);

                    foreach (ContractResponse contract in contracts)
                    {
                        // Generate and send some sales
                        await g.SendSales(transactionDate, merchant, contract, context.CancellationToken);

                        // Generate a file and upload
                        await g.SendUploadFile(transactionDate, contract, merchant, context.CancellationToken);
                    }

                    Console.WriteLine($"Logon sent for Merchant [{merchant.MerchantName}]");
                }
            }
            catch(Exception e)
            {
                // TODO: Log the error
            }
        }

        private async Task<String> GetToken(String clientId, String clientSecret, CancellationToken cancellationToken)
        {
            TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);

            return token.AccessToken;
        }

               #endregion
    }
}