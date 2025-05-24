using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs
{
    using Quartz;
    using Shared.Logger;
    using SimpleResults;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using TransactionProcessing.SchedulerService.DataGenerator;
    using TransactionProcessing.SchedulerService.Jobs.Jobs;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="IJob" />
    public class GenerateTransactionsJob : BaseJob
    {

        #region Methods

        public override async Task<Result> ExecuteJob(IJobExecutionContext context)
        {
            TransactionJobConfig configuration = Helpers.LoadJobConfig<TransactionJobConfig>(context.MergedJobDataMap);

            ITransactionDataGeneratorService t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;

            return await Jobs.GenerateTransactions(t, configuration, context.CancellationToken);
        }
        #endregion
    }
}