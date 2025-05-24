using System;
using System.Threading.Tasks;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs
{
    using Quartz;
    using Shared.Logger;
    using SimpleResults;
    using TransactionProcessing.SchedulerService.DataGenerator;

    public class ProcessSettlementJob : BaseJob
    {
        public override async Task<Result> ExecuteJob(IJobExecutionContext context) {
            SettlementJobConfig configuration = Helpers.LoadJobConfig<SettlementJobConfig>(context.MergedJobDataMap);

            ITransactionDataGeneratorService t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;
            return await Jobs.PerformSettlement(t, DateTime.Now, configuration, context.CancellationToken);
        }
    }
}
