namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using DataGeneration;
    using EstateManagement.DataTransferObjects.Responses;
    using Quartz;
    using Quartz.Logging;

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

            ITransactionDataGenerator t = CreateTransactionDataGenerator(clientId, clientSecret, RunningMode.Live);

            await Jobs.GenerateFileUploads(t, estateId, merchantId, context.CancellationToken);
        }

        

        #endregion
    }
}