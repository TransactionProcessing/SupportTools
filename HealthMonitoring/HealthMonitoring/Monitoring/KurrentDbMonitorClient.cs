using System.Diagnostics;
using System.Text.Json;
using EventStore.Client;
using KurrentDB.Client;
using HealthMonitoring.Domain;

namespace HealthMonitoring.Monitoring;

public interface IKurrentDbProbe
{
    Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken);
}

public sealed class KurrentDbMonitorClient(IKurrentDbProbe probe) : IHealthEndpointClient
{
    public async Task<HealthEndpointResult> CheckAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await probe.ProbeAsync(service, cancellationToken);
            stopwatch.Stop();
            var check = new NormalizedCheckResult("KurrentDB", HealthStatus.Healthy, "KurrentDB is reachable.", stopwatch.Elapsed, null, new Dictionary<string, JsonElement>());
            var raw = JsonSerializer.Serialize(new { status = nameof(HealthStatus.Healthy), totalDuration = stopwatch.Elapsed, entries = new { KurrentDB = new { status = nameof(HealthStatus.Healthy), duration = stopwatch.Elapsed, data = new { } } } });
            return new HealthEndpointResult(new NormalizedHealthResult(HealthStatus.Healthy, nameof(HealthStatus.Healthy), [check], RawResponse: raw), null, stopwatch.Elapsed, raw);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failure(service, stopwatch.Elapsed, "KurrentDB health check timed out.");
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
        var check = new NormalizedCheckResult("KurrentDB", status, error, duration, exception?.ToString(), new Dictionary<string, JsonElement>());
        var raw = JsonSerializer.Serialize(new { status = status.ToString(), totalDuration = duration, entries = new { KurrentDB = new { status = status.ToString(), duration, exception = exception?.ToString(), data = new { } } } });
        return new HealthEndpointResult(new NormalizedHealthResult(status, status.ToString(), [check], error, raw), null, duration, raw);
    }
}

internal sealed class KurrentDbProbe : IKurrentDbProbe
{
    public async Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service.ConnectionString)) throw new InvalidOperationException("KurrentDB connection string is not configured.");

        var settings = KurrentDBClientSettings.Create(service.ConnectionString);
        using var client = new KurrentDBClient(settings);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(service.RequestTimeout);

        await foreach (var _ in client.ReadAllAsync(
            Direction.Forwards,
            Position.Start,
            maxCount: 1,
            resolveLinkTos: false,
            deadline: service.RequestTimeout,
            userCredentials: null,
            cancellationToken: timeoutSource.Token).WithCancellation(timeoutSource.Token))
        {
            break;
        }
    }
}
