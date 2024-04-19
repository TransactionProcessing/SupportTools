namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Threading.Tasks;
    using DataGeneration;
    using Quartz;
    using Shared.Logger;

    [DisallowConcurrentExecution]
    public class GenerateFileUploadsJob : BaseJob
    {
        #region Methods
        public override async Task ExecuteJob(IJobExecutionContext context)
        {
            Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
            Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
            Guid userId = context.MergedJobDataMap.GetGuidValueFromString("UserId");
            
            Logger.LogInformation($"Estate Id: [{estateId}]");
            Logger.LogInformation($"Merchant Id: [{merchantId}]");
            Logger.LogInformation($"User Id: [{userId}]");

            ITransactionDataGenerator t = CreateTransactionDataGenerator(this.ClientId, this.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated; 
            await Jobs.GenerateFileUploads(t, estateId, merchantId, userId, context.CancellationToken);
        }
        #endregion
    }
}