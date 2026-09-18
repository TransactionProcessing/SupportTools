using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class KurrentDbMonitorClientTests
{
    [Fact]
    public async Task Successful_probe_returns_healthy_kurrentdb_observation()
    {
        var client = new KurrentDbMonitorClient(new StubProbe());
        var service = new MonitoredService { MonitorType = MonitorType.KurrentDb };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Normalized.Status);
        Assert.Single(result.Normalized.Checks);
        Assert.Equal("KurrentDB", result.Normalized.Checks[0].Name);
        Assert.Contains("KurrentDB", result.RawResponse, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Failed_probe_returns_unhealthy_with_the_failure_detail()
    {
        var client = new KurrentDbMonitorClient(new StubProbe(new InvalidOperationException("connection refused")));
        var service = new MonitoredService { MonitorType = MonitorType.KurrentDb };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Normalized.Status);
        Assert.Contains("connection refused", result.Normalized.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HealthStatus.Unhealthy, result.Normalized.Checks[0].Status);
    }

    private sealed class StubProbe(Exception? exception = null) : IKurrentDbProbe
    {
        public Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken)
        {
            _ = service;
            cancellationToken.ThrowIfCancellationRequested();
            return exception is null ? Task.CompletedTask : Task.FromException(exception);
        }
    }
}
