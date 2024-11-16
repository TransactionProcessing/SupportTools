using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

using System;
using System.Threading.Tasks;
using Quartz;
using Shared.Logger;
using TransactionProcessing.SchedulerService.Jobs.Jobs;

public class GenerateMerchantStatementJob : BaseJob
{
    public override async Task ExecuteJob(IJobExecutionContext context)
    {
        MerchantStatementJobConfig configuration = Helpers.LoadJobConfig<MerchantStatementJobConfig>(context.MergedJobDataMap);

        ITransactionDataGeneratorService t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
        t.TraceGenerated += TraceGenerated;
        var result = await Jobs.GenerateMerchantStatements(t, configuration, context.CancellationToken);
        if (result.IsFailed)
            throw new JobExecutionException(result.Message);
    }
}