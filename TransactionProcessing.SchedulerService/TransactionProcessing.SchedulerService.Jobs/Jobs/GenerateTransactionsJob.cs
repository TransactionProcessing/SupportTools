using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs
{
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using DataGeneration;
    using Quartz;
    using Shared.Logger;
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

            ITransactionDataGenerator t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;

            await Jobs.GenerateTransactions(t, configuration, context.CancellationToken);
        }
        #endregion
    }

    public class MakeFloatCredits : BaseJob {
        public override async Task ExecuteJob(IJobExecutionContext context) {
            MakeFloatCreditsJobConfig configuration = Helpers.LoadJobConfig<MakeFloatCreditsJobConfig>(context.MergedJobDataMap);

            ITransactionDataGenerator t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;

            await Jobs.GenerateFloatCredits(t, configuration, context.CancellationToken);
        }
    }
}