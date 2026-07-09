using System;
using KurrentDB.Client;
using SecurityService.DataTransferObjects;
using Shared.Results;
using Shared.Serialisation;
using SimpleResults;

namespace TransactionProcessor.SystemSetupTool
{
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Client;
    using estateconfig;
    using identityserverconfig;
    using SecurityService.Client;
    using Microsoft.Extensions.Configuration;
    using Shared.General;

    class Program
    {
        private static TransactionProcessorClient TransactionProcessorClient;
        
        private static SecurityServiceClient SecurityServiceClient;

        private static KurrentDBProjectionManagementClient ProjectionClient;

        private static KurrentDBPersistentSubscriptionsClient PersistentSubscriptionsClient;

        private static TokenResponse TokenResponse;
        
        static async Task Main(string[] args) {

            CancellationToken cancellationToken = CancellationToken.None;

            IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            IConfigurationRoot configurationRoot = builder.Build();
            ConfigurationReader.Initialise(configurationRoot);

            IStringSerialiser stringSerialiser = new SystemTextJsonSerializer(SystemTextJsonSerializer.GetDefaultJsonSerializerOptions());
            StringSerialiser.Initialise(stringSerialiser);

            Func<String, String> securityResolver = s => { return ConfigurationReader.GetValue("SecurityServiceUri"); };
            Func<String, String> transactionProcessorResolver = s => { return ConfigurationReader.GetValue("TransactionProcessorApi"); };
            HttpClientHandler handler = new() {
                                            ServerCertificateCustomValidationCallback = (message,
                                                                                         cert,
                                                                                         chain,
                                                                                         errors) =>
                                                                                        {
                                                                                            return true;
                                                                                        }
                                        };
            HttpClient client = new(handler);
            
            Program.SecurityServiceClient = new SecurityServiceClient(securityResolver, client, Serialise, Deserialise);
            Program.TransactionProcessorClient = new TransactionProcessorClient(transactionProcessorResolver, client, Serialise, Deserialise);
            KurrentDBClientSettings settings = KurrentDBClientSettings.Create(ConfigurationReader.GetValue("EventStoreAddress"));
            Program.ProjectionClient = new (settings);
            Program.PersistentSubscriptionsClient = new (settings);

            Mode setupMode = Mode.EstateSetup;

            String configFileName = "setupconfig.json";

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

        static String Serialise(Object arg)
        {
            return StringSerialiser.Serialise<Object>(arg, new SerialiserOptions(SerialiserPropertyFormat.SnakeCase));
        }

        static Object Deserialise(String arg, Type type)
        {
            return StringSerialiser.DeserializeObject<Object>(arg, type, new SerialiserOptions(SerialiserPropertyFormat.SnakeCase));
        }

        private static async Task<Result> SetupEstates(String configFileName, CancellationToken cancellationToken) {
            EstateConfig estateConfiguration = await GetEstatesConfig(configFileName, cancellationToken);
            foreach (Estate estate in estateConfiguration.Estates) {
                EstateSetupFunctions estateSetup = new(Program.SecurityServiceClient, Program.TransactionProcessorClient, estate);
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

        private static async Task<IdentityServerConfiguration> GetIdentityServerConfig(CancellationToken cancellationToken) {
            // Read the identity server config json string
            String identityServerJsonData;
            using StreamReader sr = new("identityserverconfig.json");
            identityServerJsonData = await sr.ReadToEndAsync(cancellationToken);


            IdentityServerConfiguration identityServerConfiguration = StringSerialiser.Deserialise<IdentityServerConfiguration>(identityServerJsonData);

            return identityServerConfiguration;
        }

        private static async Task<EstateConfig> GetEstatesConfig(String configFileName, CancellationToken cancellationToken)
        {
            // Read the estate config json string
            String estateJsonData;
            using StreamReader sr = new(configFileName);
            estateJsonData = await sr.ReadToEndAsync(cancellationToken);

            EstateConfig estateConfiguration = StringSerialiser.Deserialise<EstateConfig>(estateJsonData);
            return estateConfiguration;
        }
        
    }
}
