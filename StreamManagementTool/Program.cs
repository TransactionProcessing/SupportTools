using System.Text.Json;
using EventStore.Client;
using KurrentDB.Client;
using Microsoft.Extensions.Configuration;
using Shared.General;

namespace StreamManagementTool
{
    internal class Program
    {
        static async Task Main(string[] args) {
            IConfigurationBuilder builder = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            IConfigurationRoot configurationRoot = builder.Build();
            ConfigurationReader.Initialise(configurationRoot);

            List<StreamConfiguration> config = await LoadStreamConfig(CancellationToken.None);
            KurrentDBClientSettings settings = KurrentDBClientSettings.Create(ConfigurationReader.GetValue("AppSettings", "EventStoreAddress"));
            KurrentDBClient client = new KurrentDBClient(settings);

            foreach (StreamConfiguration streamConfiguration in config) {
                // Fetch existing metadata
                
                var existingMetadataResult = await client.GetStreamMetadataAsync(streamConfiguration.StreamName);

                // Check if metadata exists
                var existingMetadata = existingMetadataResult.Metadata;

                // Create a new metadata object, updating maxAge to 60 days
                var updatedMetadata = new StreamMetadata(
                    maxAge: TimeSpan.FromDays(streamConfiguration.MaxAgeInDays),               
                    maxCount: existingMetadata.MaxCount,        // Preserve existing maxCount
                    truncateBefore: existingMetadata.TruncateBefore, // Preserve existing truncateBefore
                    cacheControl: existingMetadata.CacheControl,    // Preserve cacheControl
                    customMetadata: existingMetadata.CustomMetadata // Preserve custom metadata
                );

                // Set the updated metadata
                await client.SetStreamMetadataAsync(
                    streamName: streamConfiguration.StreamName,
                    StreamState.StreamRevision(existingMetadataResult.MetastreamRevision.GetValueOrDefault()),
                    metadata: updatedMetadata
                );

                Console.WriteLine($"Stream {streamConfiguration.StreamName} metadata updated successfully. Max Age is {streamConfiguration.MaxAgeInDays} days");
            }
        }

        private static async Task<List<StreamConfiguration>> LoadStreamConfig(CancellationToken cancellationToken)
        {
            // Read the identity server config json string
            String streamConfigurationJsonData = null;
            using (StreamReader sr = new StreamReader("streammanagementconfiguration.json"))
            {
                streamConfigurationJsonData = await sr.ReadToEndAsync(cancellationToken);
            }

            StreamConfigurationList streamConfiguration = JsonSerializer.Deserialize<StreamConfigurationList>(streamConfigurationJsonData);

            return streamConfiguration.StreamConfiguration;
        }
    }

    public record StreamConfigurationList(List<StreamConfiguration> StreamConfiguration);

    public record StreamConfiguration(String StreamName, Int32 MaxAgeInDays);
}
