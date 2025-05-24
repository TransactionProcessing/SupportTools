using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

using Quartz;
using Shared.Logger;
using SimpleResults;
using System;
using System.Threading.Tasks;
using TransactionProcessing.SchedulerService.Jobs.Jobs;

public class GenerateMerchantStatementJob : BaseJob
{
    public override async Task<Result> ExecuteJob(IJobExecutionContext context)
    {
        MerchantStatementJobConfig configuration = Helpers.LoadJobConfig<MerchantStatementJobConfig>(context.MergedJobDataMap);

        ITransactionDataGeneratorService t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
        t.TraceGenerated += TraceGenerated;
        return await Jobs.GenerateMerchantStatements(t, configuration, context.CancellationToken);
    }
}