namespace TransactionProcessing.SchedulerService.Jobs;

using System;
using System.Diagnostics.Eventing.Reader;
using System.Net.Http;
using EstateManagement.Client;
using EstateManagement.DataTransferObjects.Responses;
using MessagingService.Client;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using SecurityService.Client;
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
        Bootstrapper.Services.AddSingleton<IMessagingServiceClient, MessagingServiceClient>();
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