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
            SettlementJobConfig configuration = Helpers.LoadJobConfig<SettlementJobConfig>(context.MergedJobDataMap);

            ITransactionDataGenerator t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;
            await Jobs.PerformSettlement(t, DateTime.Now, configuration, context.CancellationToken);
        }
    }
}
