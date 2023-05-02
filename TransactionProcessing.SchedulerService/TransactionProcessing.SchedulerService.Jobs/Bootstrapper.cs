namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;
using DataGeneration;
using EstateManagement.Client;
using EstateManagement.DataTransferObjects.Responses;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SecurityService.Client;
using SecurityService.DataTransferObjects.Responses;
using TransactionProcessor.Client;

public static class Bootstrapper{

    internal  static IJobExecutionContext JobExecutionContext;

    internal static ServiceProvider ServiceProvider;

    internal static IServiceCollection Services;

    public static void ConfigureServices(IJobExecutionContext jobExecutionContext){
        Bootstrapper.Services = new ServiceCollection();
        Bootstrapper.JobExecutionContext = jobExecutionContext;

        HttpClientHandler httpClientHandler = new HttpClientHandler{
                                                                       ServerCertificateCustomValidationCallback = (message,
                                                                                                                    cert,
                                                                                                                    chain,
                                                                                                                    errors) => {
                                                                                                                       return true;
                                                                                                                   }
                                                                   };
        HttpClient httpClient = new HttpClient(httpClientHandler);
            
        Bootstrapper.Services.AddSingleton(httpClient);
        Bootstrapper.Services.AddSingleton<ISecurityServiceClient, SecurityServiceClient>();
        Bootstrapper.Services.AddSingleton<IEstateClient, EstateClient>();
        Bootstrapper.Services.AddSingleton<ITransactionProcessorClient, TransactionProcessorClient>();
        Bootstrapper.Services.AddSingleton<Func<String, String>>(container => serviceName => { return jobExecutionContext.MergedJobDataMap.GetString(serviceName); });
            
        Bootstrapper.ServiceProvider = Bootstrapper.Services.BuildServiceProvider();
    }

    public static T GetService<T>()
    {
        return Bootstrapper.ServiceProvider.GetService<T>();
    }
}

public class BaseJob{
    protected ITransactionDataGenerator CreateTransactionDataGenerator(String clientId, String clientSecret, RunningMode runningMode){
        ISecurityServiceClient securityServiceClient = Bootstrapper.GetService<ISecurityServiceClient>();
        IEstateClient estateClient = Bootstrapper.GetService<IEstateClient>();
        TransactionProcessorClient transactionProcessorClient = Bootstrapper.GetService<TransactionProcessorClient>();
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

    protected async Task<MerchantResponse> GetMerchant(String accessToken, Guid estateId, Guid merchantId, CancellationToken cancellationToken){
        IEstateClient estateClient = Bootstrapper.GetService<IEstateClient>();
        return await estateClient.GetMerchant(accessToken, estateId, merchantId, cancellationToken);
    }
}