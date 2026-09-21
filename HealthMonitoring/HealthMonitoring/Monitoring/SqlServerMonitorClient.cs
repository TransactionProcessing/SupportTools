using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using HealthMonitoring.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HealthMonitoring.Monitoring;

public interface ISqlServerProbe
{
    Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken);
    Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken);
}

public static partial class SqlServerVersionFormatter
{
    private static readonly IReadOnlyDictionary<int, int> MajorVersionYears = new Dictionary<int, int>
    {
        [16] = 2022,
        [15] = 2019,
        [14] = 2017,
        [13] = 2016,
        [12] = 2014,
        [11] = 2012,
        [10] = 2008,
        [9] = 2005,
        [8] = 2000
    };

    public static string? Format(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion)) return null;

        var bannerMatch = BannerRegex().Match(rawVersion);
        if (bannerMatch.Success) return $"SQL Server {bannerMatch.Groups[1].Value}";

        var productVersionMatch = ProductVersionRegex().Match(rawVersion.Trim());
        if (!productVersionMatch.Success || !int.TryParse(productVersionMatch.Groups[1].Value, out var majorVersion))
            return null;

        if (!MajorVersionYears.TryGetValue(majorVersion, out var year)) return null;
        if (majorVersion == 10 && int.TryParse(productVersionMatch.Groups[2].Value, out var minorVersion) && minorVersion >= 50)
            year = 2008;

        return $"SQL Server {year}";
    }

    [GeneratedRegex(@"\b(?:Microsoft\s+)?SQL\s+Server\s+(20\d{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex BannerRegex();

    [GeneratedRegex(@"^(\d+)(?:\.(\d+))?(?:\.|$)")]
    private static partial Regex ProductVersionRegex();
}

public sealed class SqlServerRegistrationVersionResolver(ISqlServerProbe probe, ILogger<SqlServerRegistrationVersionResolver> logger)
{
    public async Task<string?> ResolveAsync(MonitoredService service, string? fallback, CancellationToken cancellationToken)
    {
        try
        {
            return SqlServerVersionFormatter.Format(await probe.GetVersionAsync(service, cancellationToken)) ?? fallback;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to determine SQL Server version while registering service {ServiceId}.", service.ServiceId);
            return fallback;
        }
    }
}

public sealed class SqlServerMonitorOptions
{
    public string[] AllowedDataSources { get; init; } = [];
}

public sealed class SqlServerMonitorClient(ISqlServerProbe probe) : IHealthEndpointClient
{
    public async Task<HealthEndpointResult> CheckAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await probe.ProbeAsync(service, cancellationToken);
            stopwatch.Stop();
            var check = new NormalizedCheckResult("SQL Server", HealthStatus.Healthy, "SQL Server is reachable.", stopwatch.Elapsed, null, new Dictionary<string, JsonElement>());
            var raw = JsonSerializer.Serialize(new { status = nameof(HealthStatus.Healthy), totalDuration = stopwatch.Elapsed, entries = new { SqlServer = new { status = nameof(HealthStatus.Healthy), duration = stopwatch.Elapsed, data = new { } } } });
            return new HealthEndpointResult(new NormalizedHealthResult(HealthStatus.Healthy, nameof(HealthStatus.Healthy), [check], RawResponse: raw), null, stopwatch.Elapsed, raw);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failure(service, stopwatch.Elapsed, "SQL Server health check timed out.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failure(service, stopwatch.Elapsed, exception.Message, exception);
        }
    }

    private static HealthEndpointResult Failure(MonitoredService service, TimeSpan duration, string error, Exception? exception = null)
    {
        var status = string.Equals(service.StatusPolicy.EndpointFailureStatus, nameof(HealthStatus.Degraded), StringComparison.OrdinalIgnoreCase)
            ? HealthStatus.Degraded
            : HealthStatus.Unhealthy;
        var check = new NormalizedCheckResult("SQL Server", status, error, duration, exception?.ToString(), new Dictionary<string, JsonElement>());
        var raw = JsonSerializer.Serialize(new { status = status.ToString(), totalDuration = duration, entries = new { SqlServer = new { status = status.ToString(), duration, exception = exception?.ToString(), data = new { } } } });
        return new HealthEndpointResult(new NormalizedHealthResult(status, status.ToString(), [check], error, raw), null, duration, raw);
    }
}

internal sealed class SqlServerProbe(IOptions<SqlServerMonitorOptions> options) : ISqlServerProbe
{
    public async Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service.ConnectionString)) throw new InvalidOperationException("SQL Server connection string is not configured.");
        var connectionString = ValidateConnectionString(service.ConnectionString, options.Value.AllowedDataSources);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(service.RequestTimeout);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(timeoutSource.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(service.RequestTimeout.TotalSeconds));
        await command.ExecuteScalarAsync(timeoutSource.Token);
    }

    public async Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service.ConnectionString)) throw new InvalidOperationException("SQL Server connection string is not configured.");
        var connectionString = ValidateConnectionString(service.ConnectionString, options.Value.AllowedDataSources);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(service.RequestTimeout);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(timeoutSource.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CAST(@@VERSION AS nvarchar(max))";
        command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(service.RequestTimeout.TotalSeconds));
        return Convert.ToString(await command.ExecuteScalarAsync(timeoutSource.Token));
    }

    private static string ValidateConnectionString(string connectionString, IReadOnlyCollection<string> allowedDataSources)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("SQL Server connection string is invalid.", exception);
        }
        if (allowedDataSources.Count == 0)
            throw new InvalidOperationException("No SQL Server data sources are configured for monitoring.");

        var dataSource = builder.DataSource.Trim();
        if (!allowedDataSources.Any(allowed => string.Equals(allowed.Trim(), dataSource, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"SQL Server data source '{dataSource}' is not allowed for monitoring.");

        return builder.ConnectionString;
    }
}
