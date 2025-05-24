using Quartz;
using SimpleResults;
using System.Threading.Tasks;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

public class MakeFloatCredits : BaseJob {
    public override async Task<Result> ExecuteJob(IJobExecutionContext context) {
        MakeFloatCreditsJobConfig configuration = Helpers.LoadJobConfig<MakeFloatCreditsJobConfig>(context.MergedJobDataMap);

        ITransactionDataGeneratorService t = this.CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
        t.TraceGenerated += this.TraceGenerated;

        return await Jobs.GenerateFloatCredits(t, configuration, context.CancellationToken);
    }
}