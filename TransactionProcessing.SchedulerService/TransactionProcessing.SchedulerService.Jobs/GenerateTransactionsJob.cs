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
    using TransactionProcessor.Client;
    using TransactionProcessor.DataTransferObjects;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    public class GenerateTransactionsJob : BaseJob, IJob{

        #region Methods

        public async Task Execute(IJobExecutionContext context){
            try{
                Bootstrapper.ConfigureServices(context);

                String clientId = context.MergedJobDataMap.GetString("ClientId");
                String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
                Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");
                Guid merchantId = context.MergedJobDataMap.GetGuidValueFromString("MerchantId");
                Boolean requireLogon = context.MergedJobDataMap.GetBooleanValueFromString("requireLogon");

                ITransactionDataGenerator t = CreateTransactionDataGenerator(clientId, clientSecret, RunningMode.Live);

                // get a token
                String accessToken = await this.GetToken(clientId,
                                                         clientSecret,
                                                         context.CancellationToken);

                // get the merchant
                MerchantResponse merchant = await this.GetMerchant(accessToken, estateId, merchantId, context.CancellationToken);

                DateTime transactionDate = DateTime.Now;

                if (requireLogon){
                    // Do a logon transaction for the merchant
                    await t.PerformMerchantLogon(transactionDate, merchant, context.CancellationToken);
                }

                // Get the merchants contracts
                List<ContractResponse> contracts = await t.GetMerchantContracts(merchant, context.CancellationToken);

                foreach (ContractResponse contract in contracts){
                    // Generate and send some sales
                    await t.SendSales(transactionDate, merchant, contract, context.CancellationToken);

                    // Generate a file and upload
                    await t.SendUploadFile(transactionDate, contract, merchant, context.CancellationToken);
                }

                Console.WriteLine($"Logon sent for Merchant [{merchant.MerchantName}]");
            }
            catch(Exception e){
                // TODO: Log the error
            }
        }
        #endregion
    }
}