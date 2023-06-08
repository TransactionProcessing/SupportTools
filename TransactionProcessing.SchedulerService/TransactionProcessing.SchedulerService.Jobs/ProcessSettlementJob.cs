﻿using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransactionProcessing.SchedulerService.Jobs
{
    using System.Threading;
    using EstateManagement.Client;
    using Quartz;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using TransactionProcessing.DataGeneration;
    using TransactionProcessor.Client;

    public class ProcessSettlementJob : BaseJob, IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            Bootstrapper.ConfigureServices(context);
            String clientId = context.MergedJobDataMap.GetString("ClientId");
            String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
            Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");

            ITransactionDataGenerator t = CreateTransactionDataGenerator(clientId, clientSecret, RunningMode.Live);

            await Jobs.PerformSettlement(t, DateTime.Now, estateId, context.CancellationToken);
        }
    }
}