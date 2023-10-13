using System;
using System.Threading.Tasks;

namespace TransactionProcessing.SchedulerService.Jobs
{
    using Quartz;
    using Shared.Logger;
    using TransactionProcessing.DataGeneration;

    public class ProcessSettlementJob : BaseJob
    {
        public override async Task ExecuteJob(IJobExecutionContext context)
        {
            Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");

            Logger.LogInformation($"Estate Id: [{estateId}]");

            ITransactionDataGenerator t = CreateTransactionDataGenerator(this.ClientId, this.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;
            await Jobs.PerformSettlement(t, DateTime.Now, estateId, context.CancellationToken);
        }
    }
}
