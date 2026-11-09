using System.Text;
using System.Text.Json;
using CentralLogging.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CentralLogging.Tests.Api;

[Collection("CentralLoggingApi")]
public sealed class UploadIntegrationTests(SqlServerApiFixture fixture)
{
    [SqlServerFact]
    public async Task Upload_retains_malformed_and_incomplete_lines_and_deduplicates_an_identical_reupload()
    {
        await fixture.ResetDatabaseAsync();
        String malformedLine = "{malformed json";
        String incompleteLine = "{\"level\":\"Warning\",\"loggerName\":\"Checkout\",\"messageTemplate\":\"started\",\"amount\":12.5}";
        String contents = $"{malformedLine}\n\n{incompleteLine}\n";

        using HttpResponseMessage firstResponse = await SendUploadAsync(contents);
        firstResponse.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        using JsonDocument firstBody = await ApiTestData.ReadJsonAsync(firstResponse);
        firstBody.RootElement.GetProperty("accepted").GetInt32().ShouldBe(0);
        firstBody.RootElement.GetProperty("retainedLines").GetInt32().ShouldBe(2);
        firstBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(0);

        var stored = await fixture.ReadDatabaseAsync(async database => await database.LogEvents
            .OrderBy(log => log.ServiceName)
            .Select(log => new { log.EventId, log.Severity, log.ServiceName, log.Message, log.PropertiesJson })
            .ToListAsync());
        stored.Count.ShouldBe(2);
        stored.Single(log => log.ServiceName == "Unknown").Message.ShouldBe(malformedLine);
        using (JsonDocument malformedProperties = JsonDocument.Parse(stored.Single(log => log.ServiceName == "Unknown").PropertiesJson))
            malformedProperties.RootElement.GetProperty("rawLog").GetString().ShouldBe(malformedLine);

        var incomplete = stored.Single(log => log.ServiceName == "Checkout");
        incomplete.Severity.ShouldBe("Warning");
        incomplete.Message.ShouldBe("started");
        incomplete.EventId.ShouldNotBe(Guid.Empty);
        using (JsonDocument incompleteProperties = JsonDocument.Parse(incomplete.PropertiesJson))
        {
            incompleteProperties.RootElement.GetProperty("rawLog").GetString().ShouldBe(incompleteLine);
            incompleteProperties.RootElement.GetProperty("sourceEventId").ValueKind.ShouldBe(JsonValueKind.Null);
            incompleteProperties.RootElement.GetProperty("amount").GetDouble().ShouldBe(12.5);
        }

        using HttpResponseMessage replayResponse = await SendUploadAsync(contents);
        using JsonDocument replayBody = await ApiTestData.ReadJsonAsync(replayResponse);
        replayBody.RootElement.GetProperty("accepted").GetInt32().ShouldBe(0);
        replayBody.RootElement.GetProperty("retainedLines").GetInt32().ShouldBe(0);
        replayBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(2);
        (await fixture.ReadDatabaseAsync(database => database.LogEvents.CountAsync())).ShouldBe(2);
    }

    [SqlServerFact]
    public async Task Upload_reports_valid_retained_and_duplicate_counts_across_batches()
    {
        await fixture.ResetDatabaseAsync();
        Guid duplicateId = Guid.NewGuid();
        Guid repeatedId = Guid.NewGuid();
        String[] lines = Enumerable.Range(0, 499)
            .Select(index => ApiTestData.LogLine(Guid.NewGuid(), "Payments", $"event-{index}"))
            .Append(ApiTestData.LogLine(duplicateId, "Payments", "first duplicate"))
            .Append(ApiTestData.LogLine(duplicateId, "Payments", "second duplicate"))
            .Append("{malformed json")
            .Append("{\"eventId\":\"" + repeatedId + "\",\"timestampUtc\":\"2026-11-09T12:00:00Z\",\"serviceName\":\"Checkout\",\"severity\":\"Info\",\"message\":\"complete\"}")
            .Append("{\"loggerName\":\"Checkout\",\"message\":\"incomplete\"}")
            .ToArray();

        using HttpResponseMessage response = await SendUploadAsync(String.Join('\n', lines) + "\n");
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        using JsonDocument body = await ApiTestData.ReadJsonAsync(response);
        body.RootElement.GetProperty("accepted").GetInt32().ShouldBe(501);
        body.RootElement.GetProperty("retainedLines").GetInt32().ShouldBe(2);
        body.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(1);
        (await fixture.ReadDatabaseAsync(database => database.LogEvents.CountAsync())).ShouldBe(503);
    }

    [SqlServerFact]
    public async Task Upload_strips_a_UTF8_bom_before_parsing_and_preserves_event_identity()
    {
        await fixture.ResetDatabaseAsync();
        Guid eventId = Guid.NewGuid();
        String contents = $"\uFEFF{ApiTestData.LogLine(eventId, "Payments", "bom event")}\n";

        using HttpResponseMessage response = await SendUploadAsync(contents);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        using JsonDocument body = await ApiTestData.ReadJsonAsync(response);
        body.RootElement.GetProperty("accepted").GetInt32().ShouldBe(1);

        var stored = await fixture.ReadDatabaseAsync(database => database.LogEvents
            .Select(log => new { log.EventId, log.ServiceName, log.Message })
            .SingleAsync());
        stored.EventId.ShouldBe(eventId);
        stored.ServiceName.ShouldBe("Payments");
        stored.Message.ShouldBe("bom event");

        using HttpResponseMessage replayResponse = await SendUploadAsync(contents);
        using JsonDocument replayBody = await ApiTestData.ReadJsonAsync(replayResponse);
        replayBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(1);
    }

    [SqlServerFact]
    public async Task Upload_keeps_completed_batches_when_a_later_batch_fails()
    {
        await fixture.ResetDatabaseAsync();
        const String constraintName = "CK_CentralLoggingIntegrationRejectBatch";
        await fixture.ReadDatabaseAsync(async database =>
        {
            await database.Database.ExecuteSqlRawAsync($"ALTER TABLE dbo.LogEvents DROP CONSTRAINT IF EXISTS {constraintName}");
            await database.Database.ExecuteSqlRawAsync($"ALTER TABLE dbo.LogEvents ADD CONSTRAINT {constraintName} CHECK (ServiceName <> N'FailBatch')");
            return true;
        });

        try
        {
            String[] lines = Enumerable.Range(0, 500)
                .Select(index => ApiTestData.LogLine(Guid.NewGuid(), "Payments", $"event-{index}"))
                .Append(ApiTestData.LogLine(Guid.NewGuid(), "FailBatch", "fail the second batch"))
                .ToArray();
            Byte[] bytes = Encoding.UTF8.GetBytes(String.Join('\n', lines) + "\n");
            await using MemoryStream stream = new(bytes);
            FormFile file = new(stream, 0, bytes.Length, "file", "events.jsonl")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/x-ndjson"
            };

            DbUpdateException? failure = await Should.ThrowAsync<DbUpdateException>(async () =>
            {
                await fixture.ReadDatabaseAsync(async database =>
                {
                    await LogEndpoints.UploadAsync(file, database,
                        Microsoft.Extensions.Options.Options.Create(new LogIngestionOptions()), CancellationToken.None);
                    return true;
                });
            });
            failure.InnerException.ShouldBeOfType<SqlException>().Number.ShouldBe(547);
            (await fixture.ReadDatabaseAsync(database => database.LogEvents.CountAsync())).ShouldBe(500);
        }
        finally
        {
            await fixture.ReadDatabaseAsync(async database =>
            {
                await database.Database.ExecuteSqlRawAsync($"ALTER TABLE dbo.LogEvents DROP CONSTRAINT IF EXISTS {constraintName}");
                return true;
            });
        }
    }

    private async Task<HttpResponseMessage> SendUploadAsync(String contents)
    {
        using MultipartFormDataContent form = ApiTestData.Upload("events.jsonl", contents);
        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(HttpMethod.Post, "/api/logs/upload", form);
        return await fixture.Client.SendAsync(request);
    }
}
