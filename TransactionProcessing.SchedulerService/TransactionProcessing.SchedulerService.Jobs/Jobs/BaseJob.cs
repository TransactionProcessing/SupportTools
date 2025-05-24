using System;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Quartz;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using SimpleResults;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;
using TransactionProcessor.Client;
using Logger = Shared.Logger.Logger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

public abstract class BaseJob : IJob{
    public String JobName { get; private set; }

    public String JobGroup{ get; private set; }

    public BaseConfiguration BaseConfiguration;

    protected ITransactionDataGeneratorService CreateTransactionDataGenerator(String clientId, String clientSecret, RunningMode runningMode){
        ISecurityServiceClient securityServiceClient = Bootstrapper.GetService<ISecurityServiceClient>();
        ITransactionProcessorClient transactionProcessorClient = Bootstrapper.GetService<ITransactionProcessorClient>();
        Func<String, String> baseAddressFunc = Bootstrapper.GetService<Func<String, String>>();

        ITransactionDataGeneratorService g = new TransactionDataGeneratorService(securityServiceClient,
                                                                   transactionProcessorClient,
                                                                   baseAddressFunc("TransactionProcessorApi"),
                                                                   baseAddressFunc("FileProcessorApi"),
                                                                   baseAddressFunc("TestHostApi"),
                                                                   clientId,
                                                                   clientSecret,
                                                                   runningMode);
        return g;
    }
    
    protected void TraceGenerated(TraceEventArgs traceArguments)
    {
        if (traceArguments.TraceLevel == TraceEventArgs.Level.Error){
            Logger.LogError(traceArguments.Message, null);
        }
        else if (traceArguments.TraceLevel == TraceEventArgs.Level.Warning){
            Logger.LogWarning(traceArguments.Message);
        }
        else{
            Logger.LogInformation(traceArguments.Message);
        }
    }

    public abstract Task<Result> ExecuteJob(IJobExecutionContext context);

    public async Task Execute(IJobExecutionContext context){

        this.CacheConfiguration(context);
        Logger.LogWarning($"Running Job Group: [{this.JobGroup}] Name: [{this.JobName}]");
        
        Bootstrapper.ConfigureServices(context, this.BaseConfiguration);
        Result result = await this.ExecuteJob(context);
        if (result.IsFailed){
            Logger.LogWarning($"Job Group: [{this.JobGroup}] Name: [{this.JobName}] failed. Exception {result.Message}");
            throw new JobExecutionException(result.Message);
        }

        Logger.LogWarning($"Job Group: [{this.JobGroup}] Name: [{this.JobName}] completed");
    }

    private void CacheConfiguration(IJobExecutionContext context){
        this.JobName = context.JobDetail.Key.Name;
        this.JobGroup = context.JobDetail.Key.Group;
        
        BaseConfiguration configuration = Helpers.LoadJobConfig<BaseConfiguration>(context.MergedJobDataMap);
        this.BaseConfiguration = configuration;
    }
}