using System.Collections.Generic;
using Newtonsoft.Json;

namespace TransactionProcessing.SchedulerService.Jobs
{
    using System;
    using System.Text.Json.Nodes;
    using System.Text.Json;
    using System.Threading.Tasks;
    using DataGeneration;
    using Quartz;
    using Shared.Logger;
    using System.Linq;

    /// <summary>
    /// 
    /// </summary>
    /// <seealso cref="Quartz.IJob" />
    public class GenerateTransactionsJob : BaseJob{

        #region Methods

        public override async Task ExecuteJob(IJobExecutionContext context)
        {
            TransactionJobConfig configuration = Helpers.LoadJobConfig<TransactionJobConfig>(context.MergedJobDataMap);
            
            ITransactionDataGenerator t = CreateTransactionDataGenerator(configuration.ClientId, configuration.ClientSecret, RunningMode.Live);
            t.TraceGenerated += TraceGenerated;

            await Jobs.GenerateTransactions(t, configuration, context.CancellationToken);
        }
        #endregion
    }
    
    public record BaseConfiguration(
        String ClientId,
        String ClientSecret,
        String EstateManagementApi,
        String FileProcessorApi,
        String SecurityService,
        String TestHostApi,
        String TransactionProcessorApi);

    public record MerchantStatementJobConfig(String ClientId,
        String ClientSecret,
        String EstateManagementApi,
        String FileProcessorApi,
        String SecurityService,
        String TestHostApi,
        String TransactionProcessorApi,Guid EstateId) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);

    public record SettlementJobConfig(String ClientId,
        String ClientSecret,
        String EstateManagementApi,
        String FileProcessorApi,
        String SecurityService,
        String TestHostApi,
        String TransactionProcessorApi, Guid EstateId) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);

    public record TransactionJobConfig(String ClientId,
        String ClientSecret,
        String EstateManagementApi,
        String FileProcessorApi,
        String SecurityService,
        String TestHostApi,
        String TransactionProcessorApi, Guid EstateId, Guid MerchantId, Boolean IsLogon, List<String> ContractNames) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);

    public record FileUploadJobConfig(String ClientId,
        String ClientSecret,
        String EstateManagementApi,
        String FileProcessorApi,
        String SecurityService,
        String TestHostApi,
        String TransactionProcessorApi, Guid EstateId, Guid MerchantId, List<String> ContractNames, Guid UserId) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);

    public record ReplayParkedQueueJobConfig(String ClientId,
        String ClientSecret,
        String EstateManagementApi,
        String FileProcessorApi,
        String SecurityService,
        String TestHostApi,
        String TransactionProcessorApi, String EventStoreAddress) : BaseConfiguration(ClientId, ClientSecret, EstateManagementApi, FileProcessorApi, SecurityService, TestHostApi, TransactionProcessorApi);

    public static class Helpers
    {
        public static T LoadJobConfig<T>(JobDataMap jobDataMap)
        {
            String standardConfiguration = jobDataMap.GetString("Standard Configuration");
            String jobConfiguration = jobDataMap.GetString("Job Configuration");
            string fullConfig = Helpers.MergeJsonConfig(standardConfiguration, jobConfiguration);
            T configuration = JsonConvert.DeserializeObject<T>(fullConfig);

            return configuration;
        }

        public static String MergeJsonConfig(String jsonDocument1, String jsonDocument2)
        {
            using (JsonDocument doc1 = JsonDocument.Parse(jsonDocument1))
            using (JsonDocument doc2 = JsonDocument.Parse(jsonDocument2))
            {
                JsonObject mergedObject = new JsonObject();

                foreach (var property in doc1.RootElement.EnumerateObject())
                {
                    mergedObject.Add(property.Name, property.Value.Clone());
                }

                foreach (var property in doc2.RootElement.EnumerateObject())
                {
                    if (mergedObject.ContainsKey(property.Name) && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        JsonElement existingArray = mergedObject[property.Name];
                        JsonElement newArray = property.Value;

                        List<JsonElement> mergedArray = existingArray.EnumerateArray().ToList();
                        mergedArray.AddRange(newArray.EnumerateArray());

                        mergedObject[property.Name] = JsonDocument.Parse(JsonSerializer.Serialize(mergedArray)).RootElement;
                    }
                    else
                    {
                        mergedObject.Add(property.Name, property.Value.Clone());
                    }
                }

                string mergedJson = JsonSerializer.Serialize(mergedObject);
                return mergedJson;
            }
        }

        internal class JsonObject : Dictionary<string, JsonElement>
        {
        }
    }
}