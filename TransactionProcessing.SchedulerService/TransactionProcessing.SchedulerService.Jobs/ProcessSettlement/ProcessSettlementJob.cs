using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TransactionProcessing.SchedulerService.Jobs
{
    using System.Threading;
    using Microsoft.Extensions.DependencyInjection;
    using Quartz;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using TransactionProcessor.Client;

    public class ProcessSettlementJob : IJob
    {
        private readonly IBootstrapper Bootstrapper;

        public ProcessSettlementJob(Func<String, IBootstrapper> bootstrapperResolver)
        {
            this.Bootstrapper = bootstrapperResolver(nameof(GenerateTransactionsJob));
        }

        /// <summary>
        /// The security service client
        /// </summary>
        private ISecurityServiceClient SecurityServiceClient;

        private ITransactionProcessorClient TransactionProcessorClient;

        /// <summary>
        /// Gets the token.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns></returns>
        private async Task<String> GetToken(CancellationToken cancellationToken)
        {
            // Get a token to talk to the estate service
            String clientId = "serviceClient";
            String clientSecret = "d192cbc46d834d0da90e8a9d50ded543";

            TokenResponse token = await this.SecurityServiceClient.GetToken(clientId, clientSecret, cancellationToken);

            return token.AccessToken;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                this.Bootstrapper.ConfigureServices(context);

                Guid estateId = context.MergedJobDataMap.GetGuidValueFromString("EstateId");

                this.SecurityServiceClient = this.Bootstrapper.GetService<ISecurityServiceClient>();
                this.TransactionProcessorClient = this.Bootstrapper.GetService<ITransactionProcessorClient>();
                String token = await this.GetToken(context.CancellationToken);
                await this.TransactionProcessorClient.ProcessSettlement(token, DateTime.Now.Date, estateId, context.CancellationToken);
            }
            catch (Exception e)
            {
                context.Result = new
                                 {
                                     ErrorMessage = e.Message
                                 };
            }
        }
    }

    public class ProcessSettlementBootstrapper : BaseBoostrapper
    {
        #region Methods

        /// <summary>
        /// Configures the service additional.
        /// </summary>
        /// <param name="jobExecutionContext">The job execution context.</param>
        public override void ConfigureServiceAdditional(IJobExecutionContext jobExecutionContext)
        {
            this.Services.AddSingleton<ISecurityServiceClient, SecurityServiceClient>();
            this.Services.AddSingleton<ITransactionProcessorClient, TransactionProcessorClient>();

            this.Services.AddSingleton<Func<String, String>>(container => serviceName => { return jobExecutionContext.MergedJobDataMap.GetString(serviceName); });
        }

        #endregion
    }
}
