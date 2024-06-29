using System.Threading.Tasks;
using Quartz;

namespace TransactionProcessing.SchedulerService.Jobs;

public class ReplayParkedQueuesJob : BaseJob
{
    public override async Task ExecuteJob(IJobExecutionContext context)
    {
        ReplayParkedQueueJobConfig configuration = Helpers.LoadJobConfig<ReplayParkedQueueJobConfig>(context.MergedJobDataMap);

        await Jobs.ReplayParkedQueues(configuration, context.CancellationToken);
    }
}