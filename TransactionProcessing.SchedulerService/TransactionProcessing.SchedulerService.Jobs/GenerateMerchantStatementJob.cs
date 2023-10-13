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
        Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");

        Logger.LogInformation($"Estate Id: [{estateId}]");
        
        ITransactionDataGenerator t = this.CreateTransactionDataGenerator(this.ClientId, this.ClientSecret, RunningMode.Live);
        t.TraceGenerated += TraceGenerated;
        await Jobs.GenerateMerchantStatements(t, estateId, context.CancellationToken);
    }
}