namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using DataGeneration;
using EstateManagement.Client;
using Quartz;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using Shared.Logger;
using TransactionProcessor.Client;

public abstract class BaseJob : IJob{
    public String JobName { get; private set; }

    public String JobGroup{ get; private set; }

    public String ClientSecret{ get; private set; }

    public String ClientId{ get; private set; }

    public String SecurityService { get; private set; }

    public String EstateManagementApi { get; private set; }
    public String FileProcessorApi { get; private set; }
    public String TransactionProcessorApi { get; private set; }
    public String TestHostApi { get; private set; }

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
        this.LogConfiguration();

        Bootstrapper.ConfigureServices(context);

        await this.ExecuteJob(context);

        Logger.LogInformation($"Job Group: [{this.JobGroup}] Name: [{this.JobName}] completed");
    }

    private void LogConfiguration(){
        Logger.LogInformation($"Client Id: [{this.ClientId}]");
        Logger.LogInformation($"EstateManagementApi is: [{this.EstateManagementApi}]");
        Logger.LogInformation($"FileProcessorApi is: [{this.FileProcessorApi}]");
        Logger.LogInformation($"SecurityService is: [{this.SecurityService}]");
        Logger.LogInformation($"TestHostApi is: [{this.TestHostApi}]");
        Logger.LogInformation($"TransactionProcessorApi is: [{this.TransactionProcessorApi}]");
    }

    private void CacheConfiguration(IJobExecutionContext context){
        this.JobName = context.JobDetail.Key.Name;
        this.JobGroup = context.JobDetail.Key.Group;
        this.ClientId = context.MergedJobDataMap.GetString("ClientId");
        this.ClientSecret = context.MergedJobDataMap.GetString("ClientSecret");

        if (context.MergedJobDataMap.ContainsKey("EstateManagementApi")){
            this.EstateManagementApi = context.MergedJobDataMap.GetString("EstateManagementApi");
        }

        if (context.MergedJobDataMap.ContainsKey("FileProcessorApi")){
            this.FileProcessorApi = context.MergedJobDataMap.GetString("FileProcessorApi");
        }

        if (context.MergedJobDataMap.ContainsKey("SecurityService")){
            this.SecurityService = context.MergedJobDataMap.GetString("SecurityService");
        }

        if (context.MergedJobDataMap.ContainsKey("TestHostApi")){
            this.TestHostApi = context.MergedJobDataMap.GetString("TestHostApi");
        }

        if (context.MergedJobDataMap.ContainsKey("TransactionProcessorApi")){
            this.TransactionProcessorApi = context.MergedJobDataMap.GetString("TransactionProcessorApi");
        }
    }
}