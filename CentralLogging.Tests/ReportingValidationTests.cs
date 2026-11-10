using CentralLogging.Api.Data;
using CentralLogging.Api.Endpoints;
using CentralLogging.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace CentralLogging.Tests;

public sealed class ReportingValidationTests
{
    [Fact]
    public async Task Page_rejects_missing_ranges()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        LogSearchRequest request = new();

        IResult result = await LogEndpoints.SearchPageAsync(database, request, CancellationToken.None);

        ShouldBeBadRequest(result);
    }

    [Fact]
    public async Task Page_rejects_a_malformed_cursor()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LogSearchRequest request = new() { FromUtc = now.AddHours(-1), ToUtc = now, Cursor = "not-a-cursor" };

        IResult result = await LogEndpoints.SearchPageAsync(database, request, CancellationToken.None);

        ShouldBeBadRequest(result);
    }

    [Fact]
    public async Task Export_rejects_nonpositive_row_limit_before_accessing_the_database()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LogSearchRequest request = new() { FromUtc = now.AddHours(-1), ToUtc = now, MaxRows = 0 };

        IResult result = await LogEndpoints.ExportAsync(database, request, new DefaultHttpContext(), CancellationToken.None);

        ShouldBeBadRequest(result);
    }

    [Theory]
    [InlineData("minute", 100)]
    [InlineData("hour", 0)]
    public async Task Aggregate_rejects_invalid_bucket_or_group_limit_before_accessing_the_database(String bucket, Int32 groupTake)
    {
        await using CentralLogsDbContext database = CreateDatabase();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        LogSearchRequest request = new() { FromUtc = now.AddHours(-1), ToUtc = now, Bucket = bucket, GroupTake = groupTake };

        IResult result = await LogEndpoints.AggregateAsync(database, request, CancellationToken.None);

        ShouldBeBadRequest(result);
    }

    private static void ShouldBeBadRequest(IResult result) => result.ShouldBeOfType<ProblemHttpResult>()
        .StatusCode.ShouldBe(StatusCodes.Status400BadRequest);

    private static CentralLogsDbContext CreateDatabase()
    {
        DbContextOptions<CentralLogsDbContext> options = new DbContextOptionsBuilder<CentralLogsDbContext>()
            .UseSqlServer("Server=localhost;Database=CentralLogsTests;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        return new CentralLogsDbContext(options);
    }
}
