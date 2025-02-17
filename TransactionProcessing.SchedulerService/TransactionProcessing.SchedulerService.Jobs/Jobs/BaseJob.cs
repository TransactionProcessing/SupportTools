using System;
using System.Threading;
using System.Threading.Tasks;
using Quartz;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using Shared.Logger;
using TransactionProcessing.SchedulerService.DataGenerator;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;
using TransactionProcessor.Client;

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

    //protected async Task<String> GetToken(String clientId, String clientSecret, CancellationToken cancellationToken)
    //{
    //    ISecurityServiceClient securityServiceClient = Bootstrapper.GetService<ISecurityServiceClient>();
    //    TokenResponse token = await securityServiceClient.GetToken(clientId, clientSecret, cancellationToken);

    //    return token.AccessToken;
    //}

    protected void TraceGenerated(TraceEventArgs traceArguments)
    {
        if (traceArguments.TraceLevel == TraceEventArgs.Level.Error){
            Logger.LogError(new Exception(traceArguments.Message));
        }
        else if (traceArguments.TraceLevel == TraceEventArgs.Level.Warning){
            Logger.LogWarning(traceArguments.Message);
        }
        else{
            Logger.LogInformation(traceArguments.Message);
        }
    }

    public abstract Task ExecuteJob(IJobExecutionContext context);

    public async Task Execute(IJobExecutionContext context){

        this.CacheConfiguration(context);
        Logger.LogInformation($"Running Job Group: [{this.JobGroup}] Name: [{this.JobName}]");
        //this.LogConfiguration();

        Bootstrapper.ConfigureServices(context, this.BaseConfiguration);

        await this.ExecuteJob(context);

        Logger.LogInformation($"Job Group: [{this.JobGroup}] Name: [{this.JobName}] completed");
    }

    private void CacheConfiguration(IJobExecutionContext context){
        this.JobName = context.JobDetail.Key.Name;
        this.JobGroup = context.JobDetail.Key.Group;
        
        BaseConfiguration configuration = Helpers.LoadJobConfig<BaseConfiguration>(context.MergedJobDataMap);
        this.BaseConfiguration = configuration;
    }
}