using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CentralLogging.Api.Models;
using Shouldly;

namespace CentralLogging.Tests.Api;

[Collection("CentralLoggingApi")]
public sealed class LogReportingIntegrationTests(SqlServerApiFixture fixture)
{
    [SqlServerFact]
    public async Task Page_uses_a_stable_cursor_for_events_with_the_same_timestamp()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddMinutes(-2);
        LogEventInput[] events = Enumerable.Range(0, 3).Select(_ => NewEvent(timestamp, "Payments")).ToArray();
        await PostEventsAsync(events);

        using HttpResponseMessage first = await GetAsync($"/api/logs/page?fromUtc={Encode(timestamp.AddMinutes(-1).ToString("O"))}&toUtc={Encode(timestamp.AddMinutes(1).ToString("O"))}&take=2");
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument firstBody = await ApiTestData.ReadJsonAsync(first);
        JsonElement firstItems = firstBody.RootElement.GetProperty("items");
        firstItems.GetArrayLength().ShouldBe(2);
        String cursor = firstBody.RootElement.GetProperty("nextCursor").GetString()!;
        cursor.ShouldNotBeNullOrWhiteSpace();

        using HttpResponseMessage second = await GetAsync($"/api/logs/page?fromUtc={Encode(timestamp.AddMinutes(-1).ToString("O"))}&toUtc={Encode(timestamp.AddMinutes(1).ToString("O"))}&take=2&cursor={Encode(cursor)}");
        using JsonDocument secondBody = await ApiTestData.ReadJsonAsync(second);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondBody.RootElement.GetProperty("items").GetArrayLength().ShouldBe(1);
        secondBody.RootElement.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);

        HashSet<Guid> ids = firstItems.EnumerateArray().Select(item => item.GetProperty("eventId").GetGuid()).ToHashSet();
        ids.Add(secondBody.RootElement.GetProperty("items")[0].GetProperty("eventId").GetGuid()).ShouldBeTrue();
        ids.Count.ShouldBe(3);
    }

    [SqlServerFact]
    public async Task Page_rejects_an_invalid_cursor()
    {
        await fixture.ResetDatabaseAsync();
        using HttpResponseMessage response = await GetAsync("/api/logs/page?fromUtc=2026-11-09T00%3A00%3A00Z&toUtc=2026-11-10T00%3A00%3A00Z&cursor=not-a-cursor");
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [SqlServerFact]
    public async Task Export_returns_filtered_ndjson_in_order_and_marks_truncation()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddMinutes(-2);
        LogEventInput[] events =
        [
            NewEvent(timestamp, "Payments"),
            NewEvent(timestamp.AddSeconds(1), "Payments"),
            NewEvent(timestamp.AddSeconds(2), "Orders")
        ];
        await PostEventsAsync(events);
        String path = $"/api/logs/export?fromUtc={Encode(timestamp.AddMinutes(-1).ToString("O"))}&toUtc={Encode(timestamp.AddMinutes(1).ToString("O"))}&serviceName=Payments&maxRows=1";

        using HttpResponseMessage response = await GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/x-ndjson");
        response.Headers.GetValues("X-Log-Export-Max-Rows").Single().ShouldBe("1");
        response.Headers.GetValues("X-Log-Export-Truncated").Single().ShouldBe("true");
        String[] lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Length.ShouldBe(1);
        using JsonDocument row = JsonDocument.Parse(lines[0]);
        row.RootElement.GetProperty("eventId").GetGuid().ShouldBe(events[1].EventId);
    }

    [SqlServerFact]
    public async Task Export_marks_complete_results_as_not_truncated()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddMinutes(-2);
        await PostEventsAsync(Enumerable.Range(0, 2).Select(_ => NewEvent(timestamp, "Payments")).ToArray());

        using HttpResponseMessage response = await GetAsync($"/api/logs/export?fromUtc={Encode(timestamp.AddMinutes(-1).ToString("O"))}&toUtc={Encode(timestamp.AddMinutes(1).ToString("O"))}&maxRows=2");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Log-Export-Truncated").Single().ShouldBe("false");
        (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Length.ShouldBe(2);
    }

    [SqlServerFact]
    public async Task Export_rejects_missing_dates_invalid_row_limits_and_ranges_over_31_days()
    {
        await fixture.ResetDatabaseAsync();
        (String Path, HttpStatusCode Expected)[] cases =
        [
            ("/api/logs/export", HttpStatusCode.BadRequest),
            ("/api/logs/export?fromUtc=2026-11-09T00%3A00%3A00Z&toUtc=2026-11-10T00%3A00%3A00Z&maxRows=100001", HttpStatusCode.BadRequest),
            ("/api/logs/export?fromUtc=2026-09-01T00%3A00%3A00Z&toUtc=2026-11-10T00%3A00%3A00Z", HttpStatusCode.BadRequest)
        ];

        foreach ((String path, HttpStatusCode expected) in cases)
        {
            using HttpResponseMessage response = await GetAsync(path);
            response.StatusCode.ShouldBe(expected);
        }
    }

    [SqlServerFact]
    public async Task Aggregates_return_severity_service_and_hour_or_day_counts()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset hour = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
        LogEventInput[] events =
        [
            NewEvent(hour.AddMinutes(1), "Payments", "Error"),
            NewEvent(hour.AddMinutes(2), "Payments", "Information"),
            NewEvent(hour.AddHours(1).AddMinutes(3), "Orders", "Error")
        ];
        await PostEventsAsync(events);
        String range = $"fromUtc={Encode(hour.AddHours(-1).ToString("O"))}&toUtc={Encode(hour.AddHours(3).ToString("O"))}";

        using HttpResponseMessage hourly = await GetAsync($"/api/logs/aggregates?{range}&bucket=hour");
        hourly.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument hourlyBody = await ApiTestData.ReadJsonAsync(hourly);
        hourlyBody.RootElement.GetProperty("timeBucket").GetString().ShouldBe("hour");
        JsonElement severityCounts = hourlyBody.RootElement.GetProperty("bySeverity");
        severityCounts.GetArrayLength().ShouldBe(2);
        severityCounts[0].GetProperty("value").GetString().ShouldBe("Error");
        severityCounts[0].GetProperty("count").GetInt64().ShouldBe(2);
        severityCounts[1].GetProperty("value").GetString().ShouldBe("Information");
        severityCounts[1].GetProperty("count").GetInt64().ShouldBe(1);
        JsonElement serviceCounts = hourlyBody.RootElement.GetProperty("byService");
        serviceCounts.GetArrayLength().ShouldBe(2);
        serviceCounts[0].GetProperty("value").GetString().ShouldBe("Payments");
        serviceCounts[0].GetProperty("count").GetInt64().ShouldBe(2);
        serviceCounts[1].GetProperty("value").GetString().ShouldBe("Orders");
        serviceCounts[1].GetProperty("count").GetInt64().ShouldBe(1);
        JsonElement hourBuckets = hourlyBody.RootElement.GetProperty("byTimeBucket");
        hourBuckets.GetArrayLength().ShouldBe(2);
        hourBuckets[0].GetProperty("bucketStartUtc").GetDateTimeOffset().ShouldBe(hour);
        hourBuckets[0].GetProperty("count").GetInt64().ShouldBe(2);

        using HttpResponseMessage daily = await GetAsync($"/api/logs/aggregates?{range}&bucket=day&groupTake=1");
        using JsonDocument dailyBody = await ApiTestData.ReadJsonAsync(daily);
        daily.StatusCode.ShouldBe(HttpStatusCode.OK);
        dailyBody.RootElement.GetProperty("timeBucket").GetString().ShouldBe("day");
        dailyBody.RootElement.GetProperty("byService").GetArrayLength().ShouldBe(1);
        dailyBody.RootElement.GetProperty("byService")[0].GetProperty("value").GetString().ShouldBe("Payments");
        dailyBody.RootElement.GetProperty("byTimeBucket").GetArrayLength().ShouldBe(1);
        dailyBody.RootElement.GetProperty("byTimeBucket")[0].GetProperty("count").GetInt64().ShouldBe(3);
    }

    [SqlServerFact]
    public async Task Aggregates_reject_invalid_bucket_and_group_limits()
    {
        await fixture.ResetDatabaseAsync();
        String range = "fromUtc=2026-11-09T00%3A00%3A00Z&toUtc=2026-11-10T00%3A00%3A00Z";
        foreach (String path in new[]
                 {
                     $"/api/logs/aggregates?{range}&bucket=minute",
                     $"/api/logs/aggregates?{range}&groupTake=501",
                     "/api/logs/aggregates"
                 })
        {
            using HttpResponseMessage response = await GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [SqlServerFact]
    public async Task Structured_fields_requires_a_non_blank_service_name_and_limits_its_length()
    {
        await fixture.ResetDatabaseAsync();

        foreach (String path in new[]
                 {
                     "/api/logs/fields",
                     "/api/logs/fields?serviceName=%20%20%20",
                     $"/api/logs/fields?serviceName={Encode(new String('s', 201))}"
                 })
        {
            using HttpResponseMessage response = await GetAsync(path);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [SqlServerFact]
    public async Task Structured_fields_returns_grouped_types_counts_and_timestamps_for_the_service()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset firstSeen = DateTimeOffset.UtcNow.AddMinutes(-2);
        DateTimeOffset lastSeen = firstSeen.AddMinutes(1);
        LogEventInput[] events =
        [
            new LogEventInput
            {
                EventId = Guid.NewGuid(), TimestampUtc = firstSeen, Severity = "Information", ServiceName = "Payments", Message = "reporting integration event",
                AdditionalProperties = new Dictionary<String, JsonElement>
                {
                    ["region"] = JsonSerializer.SerializeToElement("eu-west"),
                    ["attempt"] = JsonSerializer.SerializeToElement(1)
                }
            },
            new LogEventInput
            {
                EventId = Guid.NewGuid(), TimestampUtc = lastSeen, Severity = "Information", ServiceName = "Payments", Message = "reporting integration event",
                AdditionalProperties = new Dictionary<String, JsonElement>
                {
                    ["region"] = JsonSerializer.SerializeToElement(42),
                    ["attempt"] = JsonSerializer.SerializeToElement(2)
                }
            },
            new LogEventInput
            {
                EventId = Guid.NewGuid(), TimestampUtc = firstSeen, Severity = "Information", ServiceName = "Orders", Message = "reporting integration event",
                AdditionalProperties = new Dictionary<String, JsonElement>
                {
                    ["region"] = JsonSerializer.SerializeToElement("eu-west")
                }
            }
        ];
        await PostEventsAsync(events);

        using HttpResponseMessage response = await GetAsync("/api/logs/fields?serviceName=Payments");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument body = await ApiTestData.ReadJsonAsync(response);
        body.RootElement.GetProperty("serviceName").GetString().ShouldBe("Payments");
        JsonElement fields = body.RootElement.GetProperty("fields");
        fields.GetArrayLength().ShouldBe(2);
        fields[0].GetProperty("name").GetString().ShouldBe("attempt");
        fields[0].GetProperty("types")[0].GetString().ShouldBe("number");
        fields[0].GetProperty("eventCount").GetInt64().ShouldBe(2);
        fields[0].GetProperty("firstSeenUtc").GetDateTimeOffset().ShouldBe(firstSeen);
        fields[0].GetProperty("lastSeenUtc").GetDateTimeOffset().ShouldBe(lastSeen);
        fields[1].GetProperty("name").GetString().ShouldBe("region");
        fields[1].GetProperty("types")[0].GetString().ShouldBe("number");
        fields[1].GetProperty("types")[1].GetString().ShouldBe("string");
        fields[1].GetProperty("eventCount").GetInt64().ShouldBe(2);
    }

    [SqlServerFact]
    public async Task Structured_fields_returns_an_empty_list_for_an_unknown_service()
    {
        await fixture.ResetDatabaseAsync();

        using HttpResponseMessage response = await GetAsync("/api/logs/fields?serviceName=Unknown");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using JsonDocument body = await ApiTestData.ReadJsonAsync(response);
        body.RootElement.GetProperty("serviceName").GetString().ShouldBe("Unknown");
        body.RootElement.GetProperty("fields").GetArrayLength().ShouldBe(0);
    }

    private async Task PostEventsAsync(IReadOnlyList<LogEventInput> events)
    {
        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(HttpMethod.Post, "/api/logs", JsonContent.Create(events));
        using HttpResponseMessage response = await fixture.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private async Task<HttpResponseMessage> GetAsync(String path)
    {
        using HttpRequestMessage request = ApiTestData.AuthorizedRequest(HttpMethod.Get, path);
        return await fixture.Client.SendAsync(request);
    }

    private static LogEventInput NewEvent(DateTimeOffset timestamp, String service, String severity = "Information") => new()
    {
        EventId = Guid.NewGuid(),
        TimestampUtc = timestamp,
        Severity = severity,
        ServiceName = service,
        Message = "reporting integration event"
    };

    private static String Encode(String value) => Uri.EscapeDataString(value);
}
