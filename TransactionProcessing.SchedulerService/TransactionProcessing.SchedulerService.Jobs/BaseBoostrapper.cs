namespace TransactionProcessing.SchedulerService.Jobs
{
    using System.Net.Http;
    using Microsoft.Extensions.DependencyInjection;
    using Quartz;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="TransactionProcessing.SchedulerService.Jobs.IBootstrapper" />
    public abstract class BaseBoostrapper : IBootstrapper
    {
        #region Fields

        /// <summary>
        /// The job execution context
        /// </summary>
        protected IJobExecutionContext JobExecutionContext;

        /// <summary>
        /// The service provider
        /// </summary>
        protected ServiceProvider ServiceProvider;

        /// <summary>
        /// The services
        /// </summary>
        protected IServiceCollection Services;

        #endregion

        #region Methods

        /// <summary>
        /// Configures the service additional.
        /// </summary>
        /// <param name="jobExecutionContext">The job execution context.</param>
        public virtual void ConfigureServiceAdditional(IJobExecutionContext jobExecutionContext)
        {
            // Nothing here
        }

        /// <summary>
        /// Configures the services.
        /// </summary>
        /// <param name="jobExecutionContext">The job execution context.</param>
        public virtual void ConfigureServices(IJobExecutionContext jobExecutionContext)
        {
            this.Services = new ServiceCollection();
            this.JobExecutionContext = jobExecutionContext;

            HttpClientHandler httpClientHandler = new HttpClientHandler
                                                  {
                                                      ServerCertificateCustomValidationCallback = (message,
                                                                                                   cert,
                                                                                                   chain,
                                                                                                   errors) =>
                                                                                                  {
                                                                                                      return true;
                                                                                                  }
                                                  };
            HttpClient httpClient = new HttpClient(httpClientHandler);
            this.Services.AddSingleton(httpClient);

            this.ConfigureServiceAdditional(jobExecutionContext);

            this.ServiceProvider = this.Services.BuildServiceProvider();
        }

        /// <summary>
        /// Gets the service.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public virtual T GetService<T>()
        {
            return this.ServiceProvider.GetService<T>();
        }

        #endregion
    }
}