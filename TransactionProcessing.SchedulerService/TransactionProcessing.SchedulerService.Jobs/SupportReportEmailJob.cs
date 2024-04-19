namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Linq;
using System.Threading.Tasks;
using MessagingService.Client;
using Quartz;
using Shared.Logger;

public class SupportReportEmailJob : BaseJob
{
    #region Methods
    public override async Task ExecuteJob(IJobExecutionContext context)
    {
        String eventStoreConnectionString = context.MergedJobDataMap.GetString("EventStoreConnectionString");
        String databaseConnectionString = context.MergedJobDataMap.GetString("DatabaseConnectionString");
        String estateIds = context.MergedJobDataMap.GetString("EstateIds");

        Logger.LogInformation($"Estate Id: [{estateIds}]");
        
        String token = await this.GetToken(this.ClientId, this.ClientSecret, context.CancellationToken);

        IMessagingServiceClient messagingServiceClient =  Bootstrapper.GetService<IMessagingServiceClient>();

        await Jobs.SendSupportEmail(DateTime.Now,
                                    token,
                                    eventStoreConnectionString,
                                    databaseConnectionString,
                                    estateIds.Split(',').ToList(),
                                    messagingServiceClient,
                                    context.CancellationToken);
    }



    #endregion
}