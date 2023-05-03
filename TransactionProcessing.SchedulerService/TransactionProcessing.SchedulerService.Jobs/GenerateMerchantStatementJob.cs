namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Threading.Tasks;
using DataGeneration;
using Quartz;

public class GenerateMerchantStatementJob : BaseJob, IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        Bootstrapper.ConfigureServices(context);
        String clientId = context.MergedJobDataMap.GetString("ClientId");
        String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
        Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");

        ITransactionDataGenerator t = this.CreateTransactionDataGenerator(clientId, clientSecret, RunningMode.Live);

        await Jobs.GenerateMerchantStatements(t, estateId, context.CancellationToken);
    }
}

public class SupportReportJob : BaseJob, IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        Bootstrapper.ConfigureServices(context);

        String eventStoreAddress = context.MergedJobDataMap.GetString("EventStoreAddress");
        String databaseConnectionString = context.MergedJobDataMap.GetString("DatabaseConnectionString");

        // Events in Parked Queues
        // Incomplete Files
    }
    
}