namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using DataGeneration;
    using EstateManagement.DataTransferObjects.Responses;
    using Quartz;
    using Shared.Logger;

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
            Guid userId = context.MergedJobDataMap.GetGuidValueFromString("UserId");

            Logger.LogInformation($"Running Job {context.JobDetail.Description}");
            Logger.LogInformation($"Client Id: [{clientId}]");
            Logger.LogInformation($"Estate Id: [{estateId}]");
            Logger.LogInformation($"Merchant Id: [{merchantId}]");

            ITransactionDataGenerator t = CreateTransactionDataGenerator(clientId, clientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated; 
            await Jobs.GenerateFileUploads(t, estateId, merchantId, userId, context.CancellationToken);
        }
        #endregion
    }
}