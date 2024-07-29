using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json;
using Quartz;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TransactionProcessing.SchedulerService.Jobs.Common;

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