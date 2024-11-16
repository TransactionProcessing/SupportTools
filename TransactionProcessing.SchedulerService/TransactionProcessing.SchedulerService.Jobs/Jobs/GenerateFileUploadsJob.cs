using Microsoft.EntityFrameworkCore.Metadata;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs
{
    using System;
    using System.Threading.Tasks;
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

            ITransactionDataGeneratorService t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;
            var result = await Jobs.GenerateFileUploads(t, configuration, context.CancellationToken);
            if (result.IsFailed)
                throw new JobExecutionException(result.Message);
        }
        #endregion
    }
}