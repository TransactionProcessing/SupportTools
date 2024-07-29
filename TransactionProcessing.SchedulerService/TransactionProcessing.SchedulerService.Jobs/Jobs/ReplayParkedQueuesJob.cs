using System.Threading.Tasks;
using Quartz;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

public class ReplayParkedQueuesJob : BaseJob
{
    public override async Task ExecuteJob(IJobExecutionContext context)
    {
        ReplayParkedQueueJobConfig configuration = Helpers.LoadJobConfig<ReplayParkedQueueJobConfig>(context.MergedJobDataMap);

        await Jobs.ReplayParkedQueues(configuration, context.CancellationToken);
    }
}