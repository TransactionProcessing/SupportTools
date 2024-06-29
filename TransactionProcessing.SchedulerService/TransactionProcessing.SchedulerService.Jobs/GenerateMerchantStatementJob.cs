namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Threading.Tasks;
using DataGeneration;
using Quartz;
using Shared.Logger;

public class GenerateMerchantStatementJob : BaseJob
{
    public override async Task ExecuteJob(IJobExecutionContext context)
    {
        MerchantStatementJobConfig configuration = Helpers.LoadJobConfig<MerchantStatementJobConfig>(context.MergedJobDataMap);

        ITransactionDataGenerator t = this.CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
        t.TraceGenerated += TraceGenerated;
        await Jobs.GenerateMerchantStatements(t, configuration, context.CancellationToken);
    }
}