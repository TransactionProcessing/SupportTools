using Microsoft.EntityFrameworkCore.Metadata;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs
{
    using Quartz;
    using Shared.Logger;
    using SimpleResults;
    using System;
    using System.Threading.Tasks;
    using TransactionProcessing.SchedulerService.Jobs.Jobs;

    [DisallowConcurrentExecution]
    public class GenerateFileUploadsJob : BaseJob
    {
        #region Methods
        public override async Task<Result> ExecuteJob(IJobExecutionContext context)
        {
            FileUploadJobConfig configuration = Helpers.LoadJobConfig<FileUploadJobConfig>(context.MergedJobDataMap);

            ITransactionDataGeneratorService t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;
            return await Jobs.GenerateFileUploads(t, configuration, context.CancellationToken);
        }
        #endregion
    }
}