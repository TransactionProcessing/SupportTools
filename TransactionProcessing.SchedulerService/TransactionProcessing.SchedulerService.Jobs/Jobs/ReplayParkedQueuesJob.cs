using System.Threading.Tasks;
using Quartz;
using SimpleResults;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

public class ReplayParkedQueuesJob : BaseJob
{
    public override async Task<Result> ExecuteJob(IJobExecutionContext context)
    {
        ReplayParkedQueueJobConfig configuration = Helpers.LoadJobConfig<ReplayParkedQueueJobConfig>(context.MergedJobDataMap);

        return await Jobs.ReplayParkedQueues(configuration, context.CancellationToken);
    }
}