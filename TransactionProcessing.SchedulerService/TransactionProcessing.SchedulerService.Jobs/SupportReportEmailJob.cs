﻿namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Linq;
using System.Threading.Tasks;
using MessagingService.Client;
using Quartz;
using Shared.Logger;

public class SupportReportEmailJob : BaseJob, IJob
{
    #region Methods
    public async Task Execute(IJobExecutionContext context)
    {
        Bootstrapper.ConfigureServices(context);

        String clientId = context.MergedJobDataMap.GetString("ClientId");
        String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
        String eventStoreConnectionString = context.MergedJobDataMap.GetString("EventStoreConnectionString");
        String databaseConnectionString = context.MergedJobDataMap.GetString("DatabaseConnectionString");
        String estateIds = context.MergedJobDataMap.GetString("EstateIds");

        Logger.LogInformation($"Running Job {context.JobDetail.Description}");
        Logger.LogInformation($"Client Id: [{clientId}]");
        Logger.LogInformation($"Estate Id: [{estateIds}]");
        
        String token = await this.GetToken(clientId, clientSecret, context.CancellationToken);

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