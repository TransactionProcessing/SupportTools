using System;
using Shared.Results;
using SimpleResults;

namespace TransactionProcessor.SystemSetupTool
{
    using System.IO;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Client;
    using DataTransferObjects;
    using estateconfig;
    using identityserverconfig;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using EventStore.Client;
    using Microsoft.Extensions.Configuration;
    using Newtonsoft.Json;
    using Shared.General;
    using Microsoft.AspNetCore.Http.HttpResults;
    using Microsoft.IdentityModel.Tokens;
    using System.Collections.Generic;
    using System.Collections;

    class Program
    {
        private static TransactionProcessorClient TransactionProcessorClient;
        private static HttpClient HttpClient;

        private static SecurityServiceClient SecurityServiceClient;

        private static EventStoreProjectionManagementClient ProjectionClient;

        private static EventStorePersistentSubscriptionsClient PersistentSubscriptionsClient;

        private static TokenResponse TokenResponse;
        
        static async Task Main(string[] args) {

            CancellationToken cancellationToken = new CancellationToken();

            IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            IConfigurationRoot configurationRoot = builder.Build();
            ConfigurationReader.Initialise(configurationRoot);

            Func<String, String> securityResolver = s => { return ConfigurationReader.GetValue("SecurityServiceUri"); };
            Func<String, String> transactionProcessorResolver = s => { return ConfigurationReader.GetValue("TransactionProcessorApi"); };
            HttpClientHandler handler = new HttpClientHandler
                                        {
                                            ServerCertificateCustomValidationCallback = (message,
                                                                                         cert,
                                                                                         chain,
                                                                                         errors) =>
                                                                                        {
                                                                                            return true;
                                                                                        }
                                        };
            HttpClient client = new HttpClient(handler);
            Program.HttpClient = new HttpClient(handler);

            Program.SecurityServiceClient = new SecurityServiceClient(securityResolver, client);
            Program.TransactionProcessorClient = new TransactionProcessorClient(transactionProcessorResolver, client);
            EventStoreClientSettings settings = EventStoreClientSettings.Create(ConfigurationReader.GetValue("EventStoreAddress"));
            Program.ProjectionClient = new EventStoreProjectionManagementClient(settings);
            Program.PersistentSubscriptionsClient = new EventStorePersistentSubscriptionsClient(settings);

            Mode setupMode = Mode.EstateSetup;

            String configFileName = "setupconfig.staging.json";

            IdentityServerConfiguration identityServerConfiguration = await Program.GetIdentityServerConfig(cancellationToken);
            IdentityServerFunctions identityServerFunctions = new(Program.SecurityServiceClient, identityServerConfiguration);
            EventStoreFunctions eventStoreFunctions = new(Program.ProjectionClient, Program.PersistentSubscriptionsClient);

            Result result = setupMode switch {
                Mode.SecuritySetup => await identityServerFunctions.CreateConfig(cancellationToken),
                Mode.EventStoreSetup => await eventStoreFunctions.SetupEventStore(cancellationToken),
                Mode.EstateSetup => await SetupEstates(configFileName,cancellationToken),
                _ => Result.Invalid($"Invalid mode {setupMode}")
            };

            if (result.IsSuccess) {
                Console.WriteLine($"{setupMode} completed successfully");
            }
            else {
                Console.WriteLine($"Status: {result.Status} Message: {result.Message}");
            }
        }

        private static async Task<Result> SetupEstates(String configFileName, CancellationToken cancellationToken) {
            EstateConfig estateConfiguration = await GetEstatesConfig(configFileName, cancellationToken);
            foreach (Estate estate in estateConfiguration.Estates) {
                EstateSetupFunctions estateSetup = new EstateSetupFunctions(Program.SecurityServiceClient, Program.TransactionProcessorClient, estate);
                Result result = await estateSetup.SetupEstate(cancellationToken);
                if (result.IsFailed)
                    return ResultHelpers.CreateFailure(result);
            }

            return Result.Success();
        }

        public enum Mode {
            SecuritySetup,
            EventStoreSetup,
            EstateSetup
        }

        private static async Task<IdentityServerConfiguration> GetIdentityServerConfig(CancellationToken cancellationToken)
        {
            // Read the identity server config json string
            String identityServerJsonData = null;
            using(StreamReader sr = new StreamReader("identityserverconfig.json"))
            {
                identityServerJsonData = await sr.ReadToEndAsync(cancellationToken);
            }
            
            IdentityServerConfiguration identityServerConfiguration = JsonConvert.DeserializeObject<IdentityServerConfiguration>(identityServerJsonData);

            return identityServerConfiguration;
        }
        
        private static async Task<EstateConfig> GetEstatesConfig(String configFileName, CancellationToken cancellationToken)
        {
            // Read the estate config json string
            String estateJsonData = null;
            using(StreamReader sr = new StreamReader(configFileName))
            {
                estateJsonData = await sr.ReadToEndAsync(cancellationToken);
            }
            
            EstateConfig estateConfiguration = JsonConvert.DeserializeObject<EstateConfig>(estateJsonData);
            return estateConfiguration;
        }
        
    }
}
