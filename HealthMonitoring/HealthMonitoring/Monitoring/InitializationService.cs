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
    KurrentDbMonitorClient kurrentDbClient,
    SqlServerRegistrationVersionResolver sqlServerVersionResolver,
    KurrentDbRegistrationVersionResolver kurrentDbVersionResolver) : IInitializationService
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
        ValidateRequiredConnections(request);
        if (request.TestConnections) await TestConnectionsAsync(request, dashboardEnvironment, cancellationToken);
        await SaveConfiguredServicesAsync(request, dashboardEnvironment, cancellationToken);
    }

    private void ValidateRequiredConnections(InitializationRequest request)
    {
        if (options.RequireSqlServer && string.IsNullOrWhiteSpace(request.SqlServerConnectionString))
            throw new ArgumentException("SQL Server connection string is required.", nameof(request));
        if (options.RequireKurrentDb && string.IsNullOrWhiteSpace(request.KurrentDbConnectionString))
            throw new ArgumentException("KurrentDB connection string is required.", nameof(request));
    }

    private async Task TestConnectionsAsync(InitializationRequest request, string dashboardEnvironment, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SqlServerConnectionString))
            await TestSqlServerAsync(request.SqlServerConnectionString, dashboardEnvironment, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.KurrentDbConnectionString))
            await TestKurrentDbAsync(request.KurrentDbConnectionString, dashboardEnvironment, cancellationToken);
    }

    private async Task TestSqlServerAsync(string connectionString, string dashboardEnvironment, CancellationToken cancellationToken)
    {
        var service = ServiceConfigurationValidator.ToService(new ServiceRegistrationRequest
        {
            ServiceId = "sql-server",
            Name = "SQL Server",
            MonitorType = MonitorType.SqlServer,
            ConnectionString = connectionString
        }, dashboardEnvironment);
        var result = await sqlServerClient.CheckAsync(service, cancellationToken);
        ThrowIfConnectionTestFailed(result.Normalized, "SQL Server");
    }

    private async Task TestKurrentDbAsync(string connectionString, string dashboardEnvironment, CancellationToken cancellationToken)
    {
        var service = ServiceConfigurationValidator.ToService(new ServiceRegistrationRequest
        {
            ServiceId = "kurrentdb",
            Name = "KurrentDB",
            MonitorType = MonitorType.KurrentDb,
            ConnectionString = connectionString
        }, dashboardEnvironment);
        var result = await kurrentDbClient.CheckAsync(service, cancellationToken);
        ThrowIfConnectionTestFailed(result.Normalized, "KurrentDB");
    }

    private static void ThrowIfConnectionTestFailed(NormalizedHealthResult result, string name)
    {
        if (result.Status == HealthStatus.Unhealthy)
            throw new ArgumentException($"{name} connection test failed: {result.Error}");
    }

    private async Task SaveConfiguredServicesAsync(InitializationRequest request, string dashboardEnvironment, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SqlServerConnectionString))
            await SaveAsync(CreateSqlServerRequest(request.SqlServerConnectionString), dashboardEnvironment, cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.KurrentDbConnectionString))
            await SaveAsync(CreateKurrentDbRequest(request.KurrentDbConnectionString), dashboardEnvironment, cancellationToken);
    }

    private static ServiceRegistrationRequest CreateSqlServerRequest(string connectionString) => new()
    {
        ServiceId = "sql-server",
        Name = "SQL Server",
        MonitorType = MonitorType.SqlServer,
        ConnectionString = connectionString,
        PollingIntervalSeconds = 60,
        RequestTimeoutSeconds = 10,
        RetentionDays = 365
    };

    private static ServiceRegistrationRequest CreateKurrentDbRequest(string connectionString) => new()
    {
        ServiceId = "kurrentdb",
        Name = "KurrentDB",
        MonitorType = MonitorType.KurrentDb,
        ConnectionString = connectionString,
        PollingIntervalSeconds = 60,
        RequestTimeoutSeconds = 10,
        RetentionDays = 365
    };

    private async Task SaveAsync(ServiceRegistrationRequest request, string dashboardEnvironment, CancellationToken cancellationToken)
    {
        var errors = ServiceConfigurationValidator.Validate(request);
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));
        var existing = await repository.GetServiceAsync(request.ServiceId, cancellationToken);
        var service = ServiceConfigurationValidator.ToService(request, dashboardEnvironment, existing, preserveManagedSettings: false);
        if (request.MonitorType == MonitorType.SqlServer)
            service.Version = await sqlServerVersionResolver.ResolveAsync(service, request.Version ?? existing?.Version, cancellationToken);
        else if (request.MonitorType == MonitorType.KurrentDb)
            service.Version = await kurrentDbVersionResolver.ResolveAsync(service, request.Version ?? existing?.Version, cancellationToken);
        await repository.UpsertServiceAsync(service, cancellationToken);
    }
}
