using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralLogging.Api.Models;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CentralLogging.Tests.Api;

[Collection(CentralLoggingApiCollection.Name)]
public sealed class IngestionIntegrationTests(SqlServerApiFixture fixture)
{
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
                ["region"] = JsonSerializer.SerializeToElement("eu-west")
            }
        };

        using HttpRequestMessage batchRequest = ApiTestData.AuthorizedRequest(
            HttpMethod.Post, "/api/logs", JsonContent.Create(new[] { input, input }));
        using HttpResponseMessage batchResponse = await fixture.Client.SendAsync(batchRequest);
        batchResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using JsonDocument batchBody = await ApiTestData.ReadJsonAsync(batchResponse);
        batchBody.RootElement.GetProperty("accepted").GetInt32().ShouldBe(1);
        batchBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(1);

        using HttpRequestMessage replayRequest = ApiTestData.AuthorizedRequest(
            HttpMethod.Post, "/api/logs", JsonContent.Create(new[] { input }));
        using HttpResponseMessage replayResponse = await fixture.Client.SendAsync(replayRequest);
        replayResponse.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        using JsonDocument replayBody = await ApiTestData.ReadJsonAsync(replayResponse);
        replayBody.RootElement.GetProperty("accepted").GetInt32().ShouldBe(0);
        replayBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(1);

        (Int32 logCount, Int64 fieldCount) = await fixture.ReadDatabaseAsync(async database =>
            (await database.LogEvents.CountAsync(),
             await database.ServiceStructuredFields.Where(field => field.FieldName == "region")
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
            HttpMethod.Post, "/api/logs", new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        using HttpResponseMessage response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.ReadDatabaseAsync(database => database.LogEvents.CountAsync())).ShouldBe(0);
    }
}
