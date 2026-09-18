using System.Diagnostics;
using System.Text.Json;
using HealthMonitoring.Domain;
using Microsoft.Data.SqlClient;

namespace HealthMonitoring.Monitoring;

public interface ISqlServerProbe
{
    Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken);
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

internal sealed class SqlServerProbe : ISqlServerProbe
{
    public async Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service.ConnectionString)) throw new InvalidOperationException("SQL Server connection string is not configured.");

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(service.RequestTimeout);
        await using var connection = new SqlConnection(service.ConnectionString);
        await connection.OpenAsync(timeoutSource.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(service.RequestTimeout.TotalSeconds));
        await command.ExecuteScalarAsync(timeoutSource.Token);
    }
}
