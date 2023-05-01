namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using DataGeneration;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Responses;
    using Quartz;
    using Quartz.Impl;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using TransactionProcessor.Client;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    [DisallowConcurrentExecution]
    public class GenerateFileUploadsJob : IJob
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
        /// Initializes a new instance of the <see cref="GenerateFileUploadsJob" /> class.
        /// </summary>
        /// <param name="bootstrapperResolver">The bootstrapper resolver.</param>
        public GenerateFileUploadsJob(Func<String, IBootstrapper> bootstrapperResolver)
        {
            this.Bootstrapper = bootstrapperResolver(nameof(GenerateFileUploadsJob));
        }

        #endregion

        #region Methods
        public async Task Execute(IJobExecutionContext context)
        {
            this.Bootstrapper.ConfigureServices(context);
            String clientId = context.MergedJobDataMap.GetString("ClientId");
            String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
            Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
            Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
            
            this.SecurityServiceClient = this.Bootstrapper.GetService<ISecurityServiceClient>();
            this.EstateClient = this.Bootstrapper.GetService<IEstateClient>();
            this.TransactionProcessorClient = this.Bootstrapper.GetService<TransactionProcessorClient>();
            this.baseAddressFunc = this.Bootstrapper.GetService<Func<String, String>>();
            
            ITransactionDataGenerator g = new TransactionDataGenerator(this.SecurityServiceClient,
                                                                       this.EstateClient,
                                                                       this.TransactionProcessorClient,
                                                                       this.baseAddressFunc("FileProcessorApi"),
                                                                       this.baseAddressFunc("TestHostApi"),
                                                                       clientId,
                                                                       clientSecret,
                                                                       RunningMode.Live);
            String accessToken = await this.GetToken(clientId,
                                                     clientSecret,
                                                     context.CancellationToken);

            MerchantResponse merchant = await this.EstateClient.GetMerchant(accessToken, estateId, merchantId, context.CancellationToken);

            List<ContractResponse> contracts = await this.EstateClient.GetMerchantContracts(accessToken, merchant.EstateId, merchant.MerchantId, context.CancellationToken);
            DateTime fileDate = DateTime.Now;
            foreach (ContractResponse contract in contracts){
                // Generate a file and upload
                await g.SendUploadFile(fileDate, contract, merchant, context.CancellationToken);
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