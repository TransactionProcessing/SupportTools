using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;
using HealthMonitoring.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class InitializationStateTests
{
    [Fact]
    public void Base_initialization_is_complete_only_when_both_required_monitor_types_exist()
    {
        var services = new[]
        {
            new MonitoredService { MonitorType = MonitorType.SqlServer },
            new MonitoredService { MonitorType = MonitorType.KurrentDb }
        };

        Assert.True(InitializationStateCalculator.IsComplete(services));
        Assert.False(InitializationStateCalculator.IsComplete(services.Take(1)));
    }

    [Fact]
    public void Archived_monitors_do_not_complete_initialization()
    {
        var services = new[]
        {
            new MonitoredService { MonitorType = MonitorType.SqlServer },
            new MonitoredService { MonitorType = MonitorType.KurrentDb, ArchivedAtUtc = DateTimeOffset.UtcNow }
        };

        Assert.False(InitializationStateCalculator.IsComplete(services));
    }

    [Fact]
    public void Each_base_monitor_requirement_is_evaluated_independently()
    {
        var services = new[] { new MonitoredService { MonitorType = MonitorType.SqlServer } };

        Assert.True(InitializationStateCalculator.IsComplete(services, requireSqlServer: true, requireKurrentDb: false));
        Assert.False(InitializationStateCalculator.IsComplete(services, requireSqlServer: false, requireKurrentDb: true));
        Assert.True(InitializationStateCalculator.IsComplete([], requireSqlServer: false, requireKurrentDb: false));
    }

    [Fact]
    public async Task Initialization_registration_captures_the_sql_server_version()
    {
        var repository = new InMemoryRepository();
        var sqlServerProbe = new VersionProbe("Microsoft SQL Server 2022 (RTM) - 16.0.1000.6");
        var sqlServerClient = new SqlServerMonitorClient(sqlServerProbe);
        var kurrentDbClient = new KurrentDbMonitorClient(new NoOpKurrentDbProbe());
        var resolver = new SqlServerRegistrationVersionResolver(sqlServerProbe, NullLogger<SqlServerRegistrationVersionResolver>.Instance);
        var service = new InitializationService(repository, new InitializationOptions { RequireSqlServer = true, RequireKurrentDb = false }, sqlServerClient, kurrentDbClient, resolver);

        await service.InitializeAsync(new InitializationRequest("Server=localhost;Database=Orders;", string.Empty) { TestConnections = false }, "Development", CancellationToken.None);

        Assert.Equal("SQL Server 2022", repository.SavedService?.Version);
    }

    private sealed class VersionProbe(string version) : ISqlServerProbe
    {
        public Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken) => Task.FromResult<string?>(version);
    }

    private sealed class NoOpKurrentDbProbe : IKurrentDbProbe
    {
        public Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryRepository : IHealthMonitoringRepository
    {
        public MonitoredService? SavedService { get; private set; }

        public Task<MonitoredService?> GetServiceAsync(string serviceId, CancellationToken cancellationToken) => Task.FromResult(SavedService);
        public Task<MonitoredService> UpsertServiceAsync(MonitoredService service, CancellationToken cancellationToken) { SavedService = service; return Task.FromResult(service); }
        public Task<IReadOnlyList<MonitoredService>> ListServicesAsync(bool includeArchived, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ArchiveServiceAsync(string serviceId, DateTimeOffset archivedAtUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddObservationAsync(HealthObservation observation, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<HealthObservation>> GetObservationsAsync(Guid serviceId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceStatusSnapshot?> GetSnapshotAsync(Guid serviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertSnapshotAsync(ServiceStatusSnapshot snapshot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceIncident?> GetOpenIncidentAsync(Guid serviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateIncidentAsync(ServiceIncident incident, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteObservationsBeforeAsync(Guid serviceId, DateTimeOffset cutoffUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ServiceDependencyLink>> ListDependencyLinksAsync(Guid serviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceDependencyLink> UpsertDependencyLinkAsync(ServiceDependencyLink link, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteDependencyLinkAsync(Guid linkId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
