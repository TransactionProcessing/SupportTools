namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataGeneration;
using EstateManagement.DataTransferObjects.Responses;
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

        List<MerchantResponse> merchants = await t.GetMerchants(estateId, context.CancellationToken);
        foreach (MerchantResponse merchantResponse in merchants){
            await t.GenerateMerchantStatement(merchantResponse.EstateId, merchantResponse.MerchantId, DateTime.Now, context.CancellationToken);
        }
    }
}