using System;
using System.Threading;
using System.Threading.Tasks;
using EstateManagement.Client;
using Quartz;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using Shared.Logger;
using TransactionProcessing.DataGeneration;
using TransactionProcessing.SchedulerService.Jobs.Common;
using TransactionProcessing.SchedulerService.Jobs.Configuration;
using TransactionProcessor.Client;

namespace TransactionProcessing.SchedulerService.Jobs.Jobs;

public abstract class BaseJob : IJob{
    public String JobName { get; private set; }

    public String JobGroup{ get; private set; }

    public BaseConfiguration BaseConfiguration;

    protected ITransactionDataGenerator CreateTransactionDataGenerator(String clientId, String clientSecret, RunningMode runningMode){
        ISecurityServiceClient securityServiceClient = Bootstrapper.GetService<ISecurityServiceClient>();
        IEstateClient estateClient = Bootstrapper.GetService<IEstateClient>();
        ITransactionProcessorClient transactionProcessorClient = Bootstrapper.GetService<ITransactionProcessorClient>();
        Func<String, String> baseAddressFunc = Bootstrapper.GetService<Func<String, String>>();

        ITransactionDataGenerator g = new TransactionDataGenerator(securityServiceClient,
                                                                   estateClient,
                                                                   transactionProcessorClient,
                                                                   baseAddressFunc("EstateManagementApi"),
                                                                   baseAddressFunc("FileProcessorApi"),
                                                                   baseAddressFunc("TestHostApi"),
                                                                   clientId,
                                                                   clientSecret,
                                                                   runningMode);
        return g;
    }

    protected async Task<String> GetToken(String clientId, String clientSecret, CancellationToken cancellationToken)
    {
        ISecurityServiceClient securityServiceClient = Bootstrapper.GetService<ISecurityServiceClient>();
        TokenResponse token = await securityServiceClient.GetToken(clientId, clientSecret, cancellationToken);

        return token.AccessToken;
    }

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

    //private void LogConfiguration(){
    //    Logger.LogInformation($"Client Id: [{this.ClientId}]");
    //    Logger.LogInformation($"EstateManagementApi is: [{this.EstateManagementApi}]");
    //    Logger.LogInformation($"FileProcessorApi is: [{this.FileProcessorApi}]");
    //    Logger.LogInformation($"SecurityService is: [{this.SecurityService}]");
    //    Logger.LogInformation($"TestHostApi is: [{this.TestHostApi}]");
    //    Logger.LogInformation($"TransactionProcessorApi is: [{this.TransactionProcessorApi}]");
    //}

    private void CacheConfiguration(IJobExecutionContext context){
        this.JobName = context.JobDetail.Key.Name;
        this.JobGroup = context.JobDetail.Key.Group;
        
        BaseConfiguration configuration = Helpers.LoadJobConfig<BaseConfiguration>(context.MergedJobDataMap);
        this.BaseConfiguration = configuration;
    }
}