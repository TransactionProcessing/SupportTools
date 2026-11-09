using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralLogging.Api.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CentralLogging.Tests.Api;

[Collection("CentralLoggingApi")]
public sealed class IngestionIntegrationTests(SqlServerApiFixture fixture)
{
    private const String LogsEndpoint = "/api/logs";
    private const String RegionFieldName = "region";

    [SqlServerFact]
    public async Task Ingest_persists_one_copy_and_reports_duplicate_ids_in_a_batch_and_on_replay()
    {
        await fixture.ResetDatabaseAsync();
        Guid eventId = Guid.NewGuid();
        LogEventInput input = new()
        {
            EventId = eventId,
            TimestampUtc = DateTimeOffset.UtcNow,
            Severity = "Information",
            ServiceName = "Payments",
            Message = "payment accepted",
            AdditionalProperties = new Dictionary<String, JsonElement>
            {
                [RegionFieldName] = JsonSerializer.SerializeToElement("eu-west")
            }
        };

        using HttpRequestMessage batchRequest = ApiTestData.AuthorizedRequest(
            HttpMethod.Post, LogsEndpoint, JsonContent.Create(new[] { input, input }));
        using HttpResponseMessage batchResponse = await fixture.Client.SendAsync(batchRequest);
        batchResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using JsonDocument batchBody = await ApiTestData.ReadJsonAsync(batchResponse);
        batchBody.RootElement.GetProperty("accepted").GetInt32().ShouldBe(1);
        batchBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(1);

        using HttpRequestMessage replayRequest = ApiTestData.AuthorizedRequest(
            HttpMethod.Post, LogsEndpoint, JsonContent.Create(new[] { input }));
        using HttpResponseMessage replayResponse = await fixture.Client.SendAsync(replayRequest);
        replayResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using JsonDocument replayBody = await ApiTestData.ReadJsonAsync(replayResponse);
        replayBody.RootElement.GetProperty("accepted").GetInt32().ShouldBe(0);
        replayBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(1);

        (Int32 logCount, Int64 fieldCount) = await fixture.ReadDatabaseAsync(async database =>
            (await database.LogEvents.CountAsync(),
             await database.ServiceStructuredFields.Where(field => field.FieldName == RegionFieldName)
                 .Select(field => field.EventCount).SingleAsync()));
        logCount.ShouldBe(1);
        fieldCount.ShouldBe(1);
    }

    [SqlServerFact]
    public async Task Ingest_rejects_missing_required_fields_over_http()
    {
        await fixture.ResetDatabaseAsync();
        String payload = $"[{{\"eventId\":\"{Guid.NewGuid()}\",\"timestampUtc\":\"{DateTimeOffset.UtcNow:O}\",\"severity\":\"Information\",\"serviceName\":\"Payments\"}}]";
        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(
            HttpMethod.Post, LogsEndpoint, new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.ReadDatabaseAsync(database => database.LogEvents.CountAsync())).ShouldBe(0);
    }

    [SqlServerFact]
    public async Task Ingest_tracks_service_names_using_case_sensitive_catalog_keys()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        LogEventInput[] inputs =
        [
            CreateEvent("Payments", timestamp, RegionFieldName, "eu-west"),
            CreateEvent("payments", timestamp.AddSeconds(1), RegionFieldName, "eu-west")
        ];

        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(
            HttpMethod.Post, LogsEndpoint, JsonContent.Create(inputs));
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        String responseBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, responseBody);
        Int32 catalogRows = await fixture.ReadDatabaseAsync(database => database.ServiceStructuredFields
            .Where(field => field.FieldName == RegionFieldName)
            .CountAsync());
        catalogRows.ShouldBe(2);
    }

    private static LogEventInput CreateEvent(String serviceName,
                                             DateTimeOffset timestamp,
                                             String fieldName,
                                             String value) => new()
    {
        EventId = Guid.NewGuid(),
        TimestampUtc = timestamp,
        Severity = "Information",
        ServiceName = serviceName,
        Message = "catalog collation check",
        AdditionalProperties = new Dictionary<String, JsonElement>
        {
            [fieldName] = JsonSerializer.SerializeToElement(value)
        }
    };
}
