namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using DataGeneration;
    using EstateManagement.DataTransferObjects.Responses;
    using Quartz;

    [DisallowConcurrentExecution]
    public class GenerateFileUploadsJob : BaseJob, IJob
    {
        #region Methods
        public async Task Execute(IJobExecutionContext context)
        {
            Bootstrapper.ConfigureServices(context);

            String clientId = context.MergedJobDataMap.GetString("ClientId");
            String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
            Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
            Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
            
            String accessToken = await this.GetToken(clientId,
                                                     clientSecret,
                                                     context.CancellationToken);

            ITransactionDataGenerator t = CreateTransactionDataGenerator(clientId, clientSecret, RunningMode.Live);

            MerchantResponse merchant = await this.GetMerchant(accessToken, estateId, merchantId, context.CancellationToken);

            List<ContractResponse> contracts = await t.GetMerchantContracts(merchant, context.CancellationToken);
            DateTime fileDate = DateTime.Now;
            foreach (ContractResponse contract in contracts){
                // Generate a file and upload
                await t.SendUploadFile(fileDate, contract, merchant, context.CancellationToken);
            }
        }

        

        #endregion
    }
}