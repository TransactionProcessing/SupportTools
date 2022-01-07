namespace TransactionProcessing.SchedulerService.Jobs
{
    using Quartz;

    /// <summary>
    /// 
    /// </summary>
    public interface IBootstrapper
    {
        #region Methods

        /// <summary>
        /// Configures the services.
        /// </summary>
        /// <param name="jobExecutionContext">The job execution context.</param>
        void ConfigureServices(IJobExecutionContext jobExecutionContext);

        /// <summary>
        /// Gets the service.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        T GetService<T>();

        #endregion
    }
}