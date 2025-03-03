using System.Threading.Tasks;
using Quartz;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

public class MakeFloatCredits : BaseJob {
    public override async Task ExecuteJob(IJobExecutionContext context) {
        MakeFloatCreditsJobConfig configuration = Helpers.LoadJobConfig<MakeFloatCreditsJobConfig>(context.MergedJobDataMap);

        ITransactionDataGeneratorService t = this.CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
        t.TraceGenerated += this.TraceGenerated;

        var result = await Jobs.GenerateFloatCredits(t, configuration, context.CancellationToken);
        if (result.IsFailed)
            throw new JobExecutionException(result.Message);
    }
}