namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Threading.Tasks;
    using DataGeneration;
    using Quartz;
    using Shared.Logger;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    public class GenerateTransactionsJob : BaseJob{

        #region Methods

        public override async Task ExecuteJob(IJobExecutionContext context){

            Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
            Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
            Boolean requireLogon = context.MergedJobDataMap.GetBooleanValueFromString("requireLogon");
            
            Logger.LogInformation($"Estate Id: [{estateId}]");
            Logger.LogInformation($"Merchant Id: [{merchantId}]");
            Logger.LogInformation($"Require Logon: [{requireLogon}]");

            ITransactionDataGenerator t = CreateTransactionDataGenerator(this.ClientId, this.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;
            await Jobs.GenerateTransactions(t, estateId, merchantId, requireLogon, context.CancellationToken);
        }
        #endregion
    }
}