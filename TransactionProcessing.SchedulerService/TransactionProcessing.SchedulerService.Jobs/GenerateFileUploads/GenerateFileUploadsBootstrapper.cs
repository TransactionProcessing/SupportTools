namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using EstateManagement.Client;
    using Microsoft.Extensions.DependencyInjection;
    using Quartz;
    using SecurityService.Client;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="TransactionProcessing.SchedulerService.Jobs.BaseBoostrapper" />
    public class GenerateFileUploadsJobBootstrapper : BaseBoostrapper
    {
        #region Methods

        /// <summary>
        /// Configures the service additional.
        /// </summary>
        /// <param name="jobExecutionContext">The job execution context.</param>
        public override void ConfigureServiceAdditional(IJobExecutionContext jobExecutionContext)
        {
            this.Services.AddSingleton<ISecurityServiceClient, SecurityServiceClient>();
            this.Services.AddSingleton<IEstateClient, EstateClient>();

            this.Services.AddSingleton<Func<String, String>>(container => serviceName => { return jobExecutionContext.MergedJobDataMap.GetString(serviceName); });
        }

        #endregion
    }
}