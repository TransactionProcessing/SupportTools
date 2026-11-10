using System.Text;
using CentralLogging.Api.Data;
using CentralLogging.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CentralLogging.Tests;

public sealed class IngestionSizeLimitTests
{
    [Fact]
    public async Task Upload_rejects_a_file_larger_than_the_configured_byte_limit()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        FormFile file = CreateFile("12345");
        LogIngestionOptions options = new() { MaximumUploadBytes = 4, MaximumLineBytes = 100 };

        IResult result = await LogEndpoints.UploadAsync(file, database, Options.Create(options), CancellationToken.None);

        ProblemHttpResult problem = result.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        database.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Upload_rejects_a_line_larger_than_the_configured_byte_limit()
    {
        await using CentralLogsDbContext database = CreateDatabase();
        FormFile file = CreateFile("12345\n");
        LogIngestionOptions options = new() { MaximumUploadBytes = 100, MaximumLineBytes = 4 };

        IResult result = await LogEndpoints.UploadAsync(file, database, Options.Create(options), CancellationToken.None);

        ProblemHttpResult problem = result.ShouldBeOfType<ProblemHttpResult>();
        problem.StatusCode.ShouldBe(StatusCodes.Status413PayloadTooLarge);
        database.ChangeTracker.Entries().ShouldBeEmpty();
    }

    private static FormFile CreateFile(String contents)
    {
        Byte[] bytes = Encoding.UTF8.GetBytes(contents);
        MemoryStream stream = new(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", "logs.ndjson")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-ndjson"
        };
    }

    private static CentralLogsDbContext CreateDatabase()
    {
        DbContextOptions<CentralLogsDbContext> options = new DbContextOptionsBuilder<CentralLogsDbContext>()
            .UseSqlServer("Server=localhost;Database=CentralLogsTests;Integrated Security=true;TrustServerCertificate=true")
            .Options;
        return new CentralLogsDbContext(options);
    }
}
