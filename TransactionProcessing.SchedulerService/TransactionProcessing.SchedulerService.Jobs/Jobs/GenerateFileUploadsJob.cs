using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs
{
    using System;
    using System.Threading.Tasks;
    using DataGeneration;
    using Quartz;
    using Shared.Logger;
    using TransactionProcessing.SchedulerService.Jobs.Jobs;

    [DisallowConcurrentExecution]
    public class GenerateFileUploadsJob : BaseJob
    {
        #region Methods
        public override async Task ExecuteJob(IJobExecutionContext context)
        {
            FileUploadJobConfig configuration = Helpers.LoadJobConfig<FileUploadJobConfig>(context.MergedJobDataMap);

            ITransactionDataGenerator t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;
            await Jobs.GenerateFileUploads(t, configuration, context.CancellationToken);
        }
        #endregion
    }
}