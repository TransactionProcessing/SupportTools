namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using DataGeneration;
    using EstateManagement.Client;
    using EstateManagement.DataTransferObjects.Requests;
    using EstateManagement.DataTransferObjects.Responses;
    using Newtonsoft.Json;
    using Quartz;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using Shared.Logger;
    using TransactionProcessor.Client;
    using TransactionProcessor.DataTransferObjects;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    public class GenerateTransactionsJob : BaseJob, IJob{

        #region Methods

        public async Task Execute(IJobExecutionContext context){
                Bootstrapper.ConfigureServices(context);

                String clientId = context.MergedJobDataMap.GetString("ClientId");
                String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
                Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
                Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
                Boolean requireLogon = context.MergedJobDataMap.GetBooleanValueFromString("requireLogon");

                Logger.LogInformation($"Running Job {context.JobDetail.Description}");
                Logger.LogInformation($"Client Id: [{clientId}]");
                Logger.LogInformation($"Client Secret: [{clientSecret}]");
                Logger.LogInformation($"Estate Id: [{estateId}]");
                Logger.LogInformation($"Merchant Id: [{merchantId}]");
                Logger.LogInformation($"Require Logon: [{requireLogon}]");

                ITransactionDataGenerator t = CreateTransactionDataGenerator(clientId, clientSecret, RunningMode.Live);

                await Jobs.GenerateTransactions(t, estateId, merchantId, requireLogon, context.CancellationToken);
            }
        #endregion
    }
}