using System;
using System.Threading.Tasks;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs
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
