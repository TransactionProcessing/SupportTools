using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HealthMonitoring.Tests.Persistence;

public sealed class RepositoryTests
{
    [Fact]
    public async Task Upsert_registration_is_idempotent_by_service_id()
    {
        await using var database = await SqlServerTestDatabase.TryCreateAsync();
        if (database is null) return;
        var first = NewService("merchant-pos");
        var second = NewService("merchant-pos");
        second.Version = "2.0.0";

        await database.Repository.UpsertServiceAsync(first, CancellationToken.None);
        await database.Repository.UpsertServiceAsync(second, CancellationToken.None);

        var services = await database.Repository.ListServicesAsync(includeArchived: true, CancellationToken.None);

        Assert.Single(services);
        Assert.Equal("2.0.0", services[0].Version);
    }

    [Fact]
    public async Task Observation_and_checks_are_persisted_and_read_newest_first()
    {
        await using var database = await SqlServerTestDatabase.TryCreateAsync();
        if (database is null) return;
        var service = NewService("file-processor");
        await database.Repository.UpsertServiceAsync(service, CancellationToken.None);
        var older = NewObservation(service.Id, DateTimeOffset.UtcNow.AddMinutes(-1), "old");
        var newer = NewObservation(service.Id, DateTimeOffset.UtcNow, "new");
        newer.Checks.Add(new HealthCheckResult { Name = "database", Status = HealthStatus.Healthy });

        await database.Repository.AddObservationAsync(older, CancellationToken.None);
        await database.Repository.AddObservationAsync(newer, CancellationToken.None);

        var observations = await database.Repository.GetObservationsAsync(service.Id, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1), CancellationToken.None);

        Assert.Equal(2, observations.Count);
        Assert.Equal("new", observations[0].RawResponseJson);
        Assert.Single(observations[0].Checks);
    }

    [Fact]
    public async Task Server_status_upsert_keeps_one_latest_row_per_server()
    {
        await using var database = await SqlServerTestDatabase.TryCreateAsync();
        if (database is null) return;

        var first = NewServerStatus("server-1", "SERVER-01", "Passed", "[]");
        var second = NewServerStatus("server-1", "SERVER-01", "Warning", "[{\"name\":\"Disk Space\"}]");

        await database.Repository.UpsertServerStatusAsync(first, CancellationToken.None);
        await database.Repository.UpsertServerStatusAsync(second, CancellationToken.None);

        var current = await database.Repository.GetServerStatusAsync("server-1", CancellationToken.None);

        Assert.NotNull(current);
        Assert.Equal(first.Id, current.Id);
        Assert.Equal("Warning", current.OverallStatus);
        Assert.Equal(second.ChecksJson, current.ChecksJson);
    }

    private static MonitoredService NewService(string serviceId) => new()
    {
        Id = Guid.NewGuid(),
        ServiceId = serviceId,
        Name = serviceId,
        Environment = "Test",
        HealthUrl = new Uri("https://localhost/health")
    };

    private static HealthObservation NewObservation(Guid serviceId, DateTimeOffset timestamp, string raw) => new()
    {
        Id = Guid.NewGuid(),
        MonitoredServiceId = serviceId,
        ObservedAtUtc = timestamp,
        Status = HealthStatus.Healthy,
        AggregateStatus = "Healthy",
        HttpStatusCode = 200,
        RawResponseJson = raw
    };

    private static ServerStatus NewServerStatus(string serverId, string serverName, string overallStatus, string checksJson) => new()
    {
        Id = Guid.NewGuid(),
        ServerId = serverId,
        ServerName = serverName,
        OverallStatus = overallStatus,
        ChecksJson = checksJson,
        ReceivedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };
}

public sealed class SqlServerTestDatabase : IAsyncDisposable
{
    private readonly HealthMonitoringDbContext _context;
    public IHealthMonitoringRepository Repository { get; }

    private SqlServerTestDatabase(HealthMonitoringDbContext context)
    {
        _context = context;
        Repository = new HealthMonitoringRepository(context);
    }

    public static async Task<SqlServerTestDatabase?> TryCreateAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("HealthMonitoring__TestConnectionString");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var options = new DbContextOptionsBuilder<HealthMonitoringDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var context = new HealthMonitoringDbContext(options);
        await context.Database.MigrateAsync();
        return new SqlServerTestDatabase(context);
    }

    public ValueTask DisposeAsync() => _context.DisposeAsync();
}
