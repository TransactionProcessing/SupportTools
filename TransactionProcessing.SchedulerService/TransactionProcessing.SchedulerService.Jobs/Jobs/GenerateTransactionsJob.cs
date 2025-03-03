using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs
{
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Quartz;
    using Shared.Logger;
    using TransactionProcessing.SchedulerService.DataGenerator;
    using TransactionProcessing.SchedulerService.Jobs.Jobs;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="IJob" />
    public class GenerateTransactionsJob : BaseJob
    {

        #region Methods

        public override async Task ExecuteJob(IJobExecutionContext context)
        {
            TransactionJobConfig configuration = Helpers.LoadJobConfig<TransactionJobConfig>(context.MergedJobDataMap);

            ITransactionDataGeneratorService t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;

            var result = await Jobs.GenerateTransactions(t, configuration, context.CancellationToken);
            if (result.IsFailed)
                throw new JobExecutionException(result.Message);
        }
        #endregion
    }
}