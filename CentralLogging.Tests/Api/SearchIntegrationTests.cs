using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralLogging.Api.Models;
using Shouldly;

namespace CentralLogging.Tests.Api;

[Collection(CentralLoggingApiCollection.Name)]
public sealed class SearchIntegrationTests(SqlServerApiFixture fixture)
{
    [SqlServerFact]
    public async Task Search_applies_time_service_tenant_and_correlation_filters()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset firstTimestamp = DateTimeOffset.UtcNow.AddMinutes(-2);
        Guid tenantId = Guid.NewGuid();
        Guid correlationId = Guid.NewGuid();
        Guid differentCorrelationId = Guid.NewGuid();
        LogEventInput[] events =
        [
            CreateEvent("Payments", firstTimestamp, tenantId, correlationId),
            CreateEvent("Payments", firstTimestamp.AddSeconds(10), tenantId, correlationId),
            CreateEvent("Payments", firstTimestamp.AddSeconds(5), tenantId, differentCorrelationId),
            CreateEvent("Payments", firstTimestamp, Guid.NewGuid(), correlationId),
            CreateEvent("Orders", firstTimestamp, tenantId, correlationId),
            CreateEvent("Payments", firstTimestamp.AddMinutes(1), tenantId, correlationId)
        ];
        await PostEventsAsync(events);

        String path = $"/api/logs?fromUtc={Encode(firstTimestamp.AddSeconds(-1).ToString("O"))}" +
                      $"&toUtc={Encode(firstTimestamp.AddSeconds(30).ToString("O"))}" +
                      $"&serviceName=Payments&tenantId={tenantId:D}&correlationId={correlationId:D}&take=10";
        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(HttpMethod.Get, path);
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument body = await ApiTestData.ReadJsonAsync(response);
        body.RootElement.GetArrayLength().ShouldBe(2);
        body.RootElement[0].GetProperty("eventId").GetGuid().ShouldBe(events[1].EventId);
        body.RootElement[1].GetProperty("eventId").GetGuid().ShouldBe(events[0].EventId);
    }

    [SqlServerFact]
    public async Task Structured_filters_match_exact_json_values_and_types()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        LogEventInput[] events =
        [
            CreateEvent("Payments", timestamp, null, null, "{\"value\":12.50}"),
            CreateEvent("Payments", timestamp.AddSeconds(1), null, null, "{\"value\":\"12.50\"}"),
            CreateEvent("Payments", timestamp.AddSeconds(2), null, null, "{\"value\":true}")
        ];
        await PostEventsAsync(events);

        await SearchForValueAsync("number", "12.50").ShouldReturnEventAsync(events[0].EventId);
        await SearchForValueAsync("string", "12.50").ShouldReturnEventAsync(events[1].EventId);
        await SearchForValueAsync("boolean", "true").ShouldReturnEventAsync(events[2].EventId);
        await SearchForValueAsync("number", "12.5").ShouldReturnNoEventsAsync();
        await SearchForValueAsync("string", "true").ShouldReturnNoEventsAsync();
    }

    [SqlServerFact]
    public async Task Search_caps_result_limit_at_500()
    {
        await fixture.ResetDatabaseAsync();
        LogEventInput[] events = Enumerable.Range(0, 501)
            .Select(index => CreateEvent("Payments", DateTimeOffset.UtcNow.AddSeconds(index - 1000), null, null))
            .ToArray();
        await PostEventsAsync(events[..500]);
        await PostEventsAsync(events[500..]);

        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(HttpMethod.Get, "/api/logs?take=501");
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument body = await ApiTestData.ReadJsonAsync(response);
        body.RootElement.GetArrayLength().ShouldBe(500);
    }

    [SqlServerFact]
    public async Task Search_rejects_malformed_and_unsupported_structured_filters_over_http()
    {
        await fixture.ResetDatabaseAsync();
        String[] invalidFilters =
        [
            "not-json",
            ApiTestData.Json(new { field = "value", type = "object", value = "{}" })
        ];

        foreach (String filter in invalidFilters)
        {
            using HttpRequestMessage request = ApiTestData.AuthorizedRequest(
                HttpMethod.Get, $"/api/logs?filter={Encode(filter)}");
            using HttpResponseMessage response = await fixture.Client.SendAsync(request);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    private async Task<JsonDocument> SearchForValueAsync(String type, String value)
    {
        String filter = ApiTestData.Json(new { field = "value", type, value });
        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(
            HttpMethod.Get, $"/api/logs?filter={Encode(filter)}");
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await ApiTestData.ReadJsonAsync(response);
    }

    private async Task PostEventsAsync(IReadOnlyList<LogEventInput> events)
    {
        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(
            HttpMethod.Post, "/api/logs", JsonContent.Create(events));
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private static LogEventInput CreateEvent(String serviceName,
                                              DateTimeOffset timestamp,
                                              Guid? tenantId,
                                              Guid? correlationId,
                                              String? properties = null)
    {
        Dictionary<String, JsonElement> additionalProperties = new(StringComparer.OrdinalIgnoreCase);
        if (!String.IsNullOrWhiteSpace(properties))
        {
            using JsonDocument document = JsonDocument.Parse(properties);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                additionalProperties[property.Name] = property.Value.Clone();
        }

        return new LogEventInput
        {
            EventId = Guid.NewGuid(),
            TimestampUtc = timestamp,
            Severity = "Information",
            ServiceName = serviceName,
            TenantId = tenantId,
            CorrelationId = correlationId,
            Message = "search fixture",
            AdditionalProperties = additionalProperties
        };
    }

    private static String Encode(String value) => Uri.EscapeDataString(value);
}

internal static class SearchDocumentAssertions
{
    public static async Task ShouldReturnEventAsync(this Task<JsonDocument> documentTask, Guid expectedId)
    {
        using JsonDocument document = await documentTask;
        document.RootElement.GetArrayLength().ShouldBe(1);
        document.RootElement[0].GetProperty("eventId").GetGuid().ShouldBe(expectedId);
    }

    public static async Task ShouldReturnNoEventsAsync(this Task<JsonDocument> documentTask)
    {
        using JsonDocument document = await documentTask;
        document.RootElement.GetArrayLength().ShouldBe(0);
    }
}
