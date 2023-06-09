namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using DataGeneration;
using EstateManagement.Client;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using Shared.Logger;
using TransactionProcessor.Client;

public abstract class BaseJob{
    public String JobName { get; set; }

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
}