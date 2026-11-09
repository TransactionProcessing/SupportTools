using System.Text;
using System.Text.Json;
using CentralLogging.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CentralLogging.Tests.Api;

[Collection(CentralLoggingApiCollection.Name)]
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
        firstBody.RootElement.GetProperty("accepted").GetInt32().ShouldBe(2);
        firstBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(0);
        firstBody.RootElement.GetProperty("invalidLines").GetInt32().ShouldBe(0);

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
        replayBody.RootElement.GetProperty("duplicates").GetInt32().ShouldBe(2);
        (await fixture.ReadDatabaseAsync(database => database.LogEvents.CountAsync())).ShouldBe(2);
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
                    await LogEndpoints.UploadAsync(file, database, CancellationToken.None);
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
