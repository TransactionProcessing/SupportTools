using HealthMonitoring.Api;
using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;

namespace HealthMonitoring.Monitoring;

public sealed class InitializationOptions
{
    public bool RequireSqlServer { get; init; } = true;
    public bool RequireKurrentDb { get; init; } = true;
}

public sealed record InitializationStatus(bool Required, bool Complete, bool RequireSqlServer, bool RequireKurrentDb, bool SqlServerConfigured, bool KurrentDbConfigured);

public sealed class InitializationRequest
{
    public InitializationRequest() { }
    public InitializationRequest(string sqlServerConnectionString, string kurrentDbConnectionString)
    {
        SqlServerConnectionString = sqlServerConnectionString;
        KurrentDbConnectionString = kurrentDbConnectionString;
    }

    public string SqlServerConnectionString { get; set; } = string.Empty;
    public string KurrentDbConnectionString { get; set; } = string.Empty;
    public bool TestConnections { get; set; } = true;
}

public static class InitializationStateCalculator
{
    public static bool IsComplete(IEnumerable<MonitoredService> services, bool requireSqlServer = true, bool requireKurrentDb = true)
    {
        var activeTypes = services
            .Where(service => service.ArchivedAtUtc is null)
            .Select(service => service.MonitorType)
            .ToHashSet();
        return (!requireSqlServer || activeTypes.Contains(MonitorType.SqlServer))
            && (!requireKurrentDb || activeTypes.Contains(MonitorType.KurrentDb));
    }
}

public interface IInitializationService
{
    Task<InitializationStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task InitializeAsync(InitializationRequest request, string dashboardEnvironment, CancellationToken cancellationToken);
}

public sealed class InitializationService(
    IHealthMonitoringRepository repository,
    InitializationOptions options,
    SqlServerMonitorClient sqlServerClient,
    KurrentDbMonitorClient kurrentDbClient) : IInitializationService
{
    public async Task<InitializationStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var services = await repository.ListServicesAsync(false, cancellationToken);
        var sqlConfigured = services.Any(service => service.MonitorType == MonitorType.SqlServer);
        var kurrentConfigured = services.Any(service => service.MonitorType == MonitorType.KurrentDb);
        var required = options.RequireSqlServer || options.RequireKurrentDb;
        return new InitializationStatus(required, InitializationStateCalculator.IsComplete(services, options.RequireSqlServer, options.RequireKurrentDb), options.RequireSqlServer, options.RequireKurrentDb, sqlConfigured, kurrentConfigured);
    }

    public async Task InitializeAsync(InitializationRequest request, string dashboardEnvironment, CancellationToken cancellationToken)
    {
        if (options.RequireSqlServer && string.IsNullOrWhiteSpace(request.SqlServerConnectionString)) throw new ArgumentException("SQL Server connection string is required.", nameof(request));
        if (options.RequireKurrentDb && string.IsNullOrWhiteSpace(request.KurrentDbConnectionString)) throw new ArgumentException("KurrentDB connection string is required.", nameof(request));

        if (request.TestConnections && !string.IsNullOrWhiteSpace(request.SqlServerConnectionString))
        {
            var sqlServer = ServiceConfigurationValidator.ToService(new ServiceRegistrationRequest
            {
                ServiceId = "sql-server",
                Name = "SQL Server",
                MonitorType = MonitorType.SqlServer,
                ConnectionString = request.SqlServerConnectionString
            }, dashboardEnvironment);
            var sqlResult = await sqlServerClient.CheckAsync(sqlServer, cancellationToken);
            if (sqlResult.Normalized.Status == HealthStatus.Unhealthy) throw new ArgumentException($"SQL Server connection test failed: {sqlResult.Normalized.Error}");
        }

        if (request.TestConnections && !string.IsNullOrWhiteSpace(request.KurrentDbConnectionString))
        {
            var kurrentDb = ServiceConfigurationValidator.ToService(new ServiceRegistrationRequest
            {
                ServiceId = "kurrentdb",
                Name = "KurrentDB",
                MonitorType = MonitorType.KurrentDb,
                ConnectionString = request.KurrentDbConnectionString
            }, dashboardEnvironment);
            var kurrentResult = await kurrentDbClient.CheckAsync(kurrentDb, cancellationToken);
            if (kurrentResult.Normalized.Status == HealthStatus.Unhealthy) throw new ArgumentException($"KurrentDB connection test failed: {kurrentResult.Normalized.Error}");
        }

        if (!string.IsNullOrWhiteSpace(request.SqlServerConnectionString))
            await SaveAsync(new ServiceRegistrationRequest { ServiceId = "sql-server", Name = "SQL Server", MonitorType = MonitorType.SqlServer, ConnectionString = request.SqlServerConnectionString, PollingIntervalSeconds = 60, RequestTimeoutSeconds = 10, RetentionDays = 365 }, dashboardEnvironment, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.KurrentDbConnectionString))
            await SaveAsync(new ServiceRegistrationRequest { ServiceId = "kurrentdb", Name = "KurrentDB", MonitorType = MonitorType.KurrentDb, ConnectionString = request.KurrentDbConnectionString, PollingIntervalSeconds = 60, RequestTimeoutSeconds = 10, RetentionDays = 365 }, dashboardEnvironment, cancellationToken);
    }

    private async Task SaveAsync(ServiceRegistrationRequest request, string dashboardEnvironment, CancellationToken cancellationToken)
    {
        var errors = ServiceConfigurationValidator.Validate(request);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        var existing = await repository.GetServiceAsync(request.ServiceId, cancellationToken);
        await repository.UpsertServiceAsync(ServiceConfigurationValidator.ToService(request, dashboardEnvironment, existing, preserveManagedSettings: false), cancellationToken);
    }
}
