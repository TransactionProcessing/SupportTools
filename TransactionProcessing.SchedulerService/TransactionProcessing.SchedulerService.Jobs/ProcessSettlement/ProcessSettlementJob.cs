using System;
using System.Collections.Generic;
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

    public class ProcessSettlementJob : IJob
    {
        private Func<String, String> baseAddressFunc;

        private readonly IBootstrapper Bootstrapper;

        private IEstateClient EstateClient;

        private ISecurityServiceClient SecurityServiceClient;

        private ITransactionProcessorClient TransactionProcessorClient;

        public ProcessSettlementJob(Func<String, IBootstrapper> bootstrapperResolver)
        {
            this.Bootstrapper = bootstrapperResolver(nameof(ProcessSettlementJob));
        }
        
        private async Task<String> GetToken(String clientId,
                                            String clientSecret, 
                                            CancellationToken cancellationToken)
        {
           

            TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);

            return token.AccessToken;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            this.Bootstrapper.ConfigureServices(context);
            String clientId = context.MergedJobDataMap.GetString("ClientId");
            String clientSecret = context.MergedJobDataMap.GetString("ClientSecret");
            Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");

            this.SecurityServiceClient = this.Bootstrapper.GetService<ISecurityServiceClient>();
            this.TransactionProcessorClient = this.Bootstrapper.GetService<ITransactionProcessorClient>();
            this.EstateClient = this.Bootstrapper.GetService<IEstateClient>();
            this.baseAddressFunc = this.Bootstrapper.GetService<Func<String, String>>();
            
            ITransactionDataGenerator g = new TransactionDataGenerator(this.SecurityServiceClient,
                                                                       this.EstateClient,
                                                                       this.TransactionProcessorClient,
                                                                       this.baseAddressFunc("FileProcessorApi"),
                                                                       this.baseAddressFunc("TestHostApi"),
                                                                       clientId,
                                                                       clientSecret,
                                                                       RunningMode.Live);

            await g.PerformSettlement(DateTime.Now.Date, estateId, context.CancellationToken);
        }
    }
}
