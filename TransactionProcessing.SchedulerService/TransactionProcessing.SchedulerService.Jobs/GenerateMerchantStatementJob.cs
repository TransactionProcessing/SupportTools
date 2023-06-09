namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Threading.Tasks;
using DataGeneration;
using Quartz;
using Shared.Logger;

public class GenerateMerchantStatementJob : BaseJob, IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        Bootstrapper.ConfigureServices(context);
        String clientId = context.MergedJobDataMap.GetString("ClientId");
        String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
        Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");

        Logger.LogInformation($"Running Job {context.JobDetail.Description}");
        Logger.LogInformation($"Client Id: [{clientId}]");
        Logger.LogInformation($"Estate Id: [{estateId}]");
        
        ITransactionDataGenerator t = this.CreateTransactionDataGenerator(clientId, clientSecret, RunningMode.Live);
        t.TraceGenerated += TraceGenerated;
        await Jobs.GenerateMerchantStatements(t, estateId, context.CancellationToken);
    }
}