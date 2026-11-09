using CentralLogging.Api.Data;
using CentralLogging.Api.Endpoints;
using CentralLogging.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CentralLogging.Tests;

public sealed class IngestionValidationTests
{
    [Fact]
    public async Task Ingest_rejects_an_event_missing_a_required_field()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        LogEventInput input = new()
        {
            EventId = Guid.NewGuid(),
            TimestampUtc = DateTimeOffset.UtcNow,
            Severity = "Information",
            ServiceName = "Payments",
            Message = ""
        };
        IResult result = await LogEndpoints.IngestAsync([input], database, CancellationToken.None);

        ShouldBeBadRequest(result);
        database.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Ingest_rejects_an_empty_batch()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        IResult result = await LogEndpoints.IngestAsync([], database, CancellationToken.None);

        ShouldBeBadRequest(result);
    }

    [Fact]
    public async Task Ingest_rejects_a_batch_over_the_documented_limit()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        LogEventInput[] events = Enumerable.Range(0, 501).Select(_ => new LogEventInput
        {
            EventId = Guid.NewGuid(),
            TimestampUtc = DateTimeOffset.UtcNow,
            Severity = "Information",
            ServiceName = "Payments",
            Message = "accepted shape"
        }).ToArray();
        IResult result = await LogEndpoints.IngestAsync(events, database, CancellationToken.None);

        ShouldBeBadRequest(result);
        database.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_rejects_an_invalid_numeric_filter_before_querying_storage()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        IResult result = await LogEndpoints.SearchAsync(database, null, null, null, null, null,
            ["{\"field\":\"amount\",\"type\":\"number\",\"value\":\"not-a-number\"}"], null, CancellationToken.None);

        ShouldBeBadRequest(result);
    }

    [Fact]
    public async Task Search_rejects_a_reversed_time_range_before_querying_storage()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IResult result = await LogEndpoints.SearchAsync(database, now, now.AddMinutes(-1), null, null, null,
            null, null, CancellationToken.None);

        ShouldBeBadRequest(result);
    }

    private static void ShouldBeBadRequest(IResult result)
    {
        result.ShouldBeOfType<ProblemHttpResult>()
            .StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    private static CentralLogsDbContext CreateDatabase()
    {
        DbContextOptions<CentralLogsDbContext> options = new DbContextOptionsBuilder<CentralLogsDbContext>()
            .UseSqlServer("Server=localhost;Database=CentralLogsTests;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        return new CentralLogsDbContext(options);
    }
}
