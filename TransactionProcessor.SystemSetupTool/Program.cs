using System;
using System.Data;
using FileProcessor.Client;
using FileProcessor.DataTransferObjects.Requests;
using KurrentDB.Client;
using SecurityService.DataTransferObjects;
using Shared.Results;
using Shared.Serialisation;
using SimpleResults;
using TransactionProcessor.SystemSetupTool.fileprofileconfig;

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
        private static FileProcessorClient FileProcessorClient;
        
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
            Func<String, String> fileProcessorResolver = s => { return ConfigurationReader.GetValue("FileProcessorApi"); };
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
            Program.FileProcessorClient = new FileProcessorClient(fileProcessorResolver, client, Serialise, Deserialise);
            
            KurrentDBClientSettings settings = KurrentDBClientSettings.Create(ConfigurationReader.GetValue("EventStoreAddress"));
            Program.ProjectionClient = new (settings);
            Program.PersistentSubscriptionsClient = new (settings);

            Mode setupMode = Mode.FileProcessorSetup;

            String configFileName = "setupconfig.staging.json";

            FileProcessingOptions fileProcessingOptions = await Program.GetFileProfileConfig(cancellationToken);
            IdentityServerConfiguration identityServerConfiguration = await Program.GetIdentityServerConfig(cancellationToken);
            IdentityServerFunctions identityServerFunctions = new(Program.SecurityServiceClient, identityServerConfiguration);
            EventStoreFunctions eventStoreFunctions = new(Program.ProjectionClient, Program.PersistentSubscriptionsClient);

            Result result = setupMode switch {
                Mode.SecuritySetup => await identityServerFunctions.CreateConfig(cancellationToken),
                Mode.EventStoreSetup => await eventStoreFunctions.SetupEventStore(cancellationToken),
                Mode.EstateSetup => await SetupEstates(configFileName,cancellationToken),
                Mode.FileProcessorSetup => await SetupFileProcessors(fileProcessingOptions, cancellationToken),
                _ => Result.Invalid($"Invalid mode {setupMode}")
            };

            if (result.IsSuccess) {
                Console.WriteLine($"{setupMode} completed successfully");
            }
            else {
                Console.WriteLine($"Status: {result.Status} Message: {result.Message}");
            }
        }

        private static async Task<Result> SetupFileProcessors(FileProcessingOptions fileProcessingOptions,
                                                              CancellationToken cancellationToken) {
            Result<TokenResponse> tokenResult = await SecurityServiceClient.GetToken("serviceClient", "d192cbc46d834d0da90e8a9d50ded543", CancellationToken.None);
            if (tokenResult.IsFailed)
                return ResultHelpers.CreateFailure(tokenResult);
            foreach (FileProfile fileProfile in fileProcessingOptions.FileProfiles) {
                CreateFileProfileRequest createFileProfileRequest = new()
                {
                    FileProfileId = fileProfile.Id,
                    Name = fileProfile.Name,
                    ListeningDirectory = fileProfile.ListeningDirectory,
                    RequestType = fileProfile.RequestType,
                    OperatorName = fileProfile.OperatorName,
                    LineTerminator = Enum.Parse<LineTerminatorType>(fileProfile.LineTerminator),
                    FileFormatHandler = fileProfile.FileFormatHandler
                };
                Result<FileProcessor.Models.FileProfile> result = await Program.FileProcessorClient.CreateFileProfile(tokenResult.Data.AccessToken, createFileProfileRequest, cancellationToken);
                if (result.IsFailed)
                    return ResultHelpers.CreateFailure(result);
            }
            return Result.Success();
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
            EstateSetup,
            FileProcessorSetup,
        }

        private static async Task<IdentityServerConfiguration> GetIdentityServerConfig(CancellationToken cancellationToken) {
            // Read the identity server config json string
            String identityServerJsonData;
            using StreamReader sr = new("identityserverconfig.json");
            identityServerJsonData = await sr.ReadToEndAsync(cancellationToken);


            IdentityServerConfiguration identityServerConfiguration = StringSerialiser.Deserialise<IdentityServerConfiguration>(identityServerJsonData);

            return identityServerConfiguration;
        }

        private static async Task<FileProcessingOptions> GetFileProfileConfig(CancellationToken cancellationToken)
        {
            // Read the file profile config json string
            String fileProfileJsonData;
            using StreamReader sr = new("fileprofilesconfig.json");
            fileProfileJsonData = await sr.ReadToEndAsync(cancellationToken);

            FileProcessingOptions fileProcessingOptions = StringSerialiser.Deserialise<FileProcessingOptions>(fileProfileJsonData);

            return fileProcessingOptions;
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
