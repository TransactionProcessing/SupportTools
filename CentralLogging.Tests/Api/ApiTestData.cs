using System.Text;
using System.Text.Json;

namespace CentralLogging.Tests.Api;

internal static class ApiTestData
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static HttpRequestMessage AuthorizedRequest(HttpMethod method, String path, HttpContent? content = null)
    {
        HttpRequestMessage request = new(method, path) { Content = content };
        request.Headers.Add("X-Log-Api-Key", SqlServerApiFixture.ApiKey);
        return request;
    }

    public static String Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    public static String LogLine(Guid? eventId,
                                 String serviceName,
                                 String message,
                                 DateTimeOffset? timestampUtc = null,
                                 Guid? tenantId = null,
                                 Guid? correlationId = null,
                                 String? additionalPropertiesJson = null)
    {
        Dictionary<String, Object?> log = new()
        {
            ["eventId"] = eventId,
            ["timestampUtc"] = timestampUtc ?? DateTimeOffset.UtcNow,
            ["severity"] = "Information",
            ["serviceName"] = serviceName,
            ["message"] = message,
            ["tenantId"] = tenantId,
            ["correlationId"] = correlationId
        };

        if (!String.IsNullOrWhiteSpace(additionalPropertiesJson))
        {
            using JsonDocument properties = JsonDocument.Parse(additionalPropertiesJson);
            foreach (JsonProperty property in properties.RootElement.EnumerateObject())
                log[property.Name] = property.Value.Clone();
        }

        return Json(log);
    }

    public static MultipartFormDataContent Upload(String fileName, String contents)
    {
        MultipartFormDataContent form = new();
        form.Add(new StringContent(contents, Encoding.UTF8, "text/plain"), "file", fileName);
        return form;
    }
}
