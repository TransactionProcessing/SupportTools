using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class SqlServerMonitorClientTests
{
    [Fact]
    public async Task Successful_probe_returns_healthy_sql_server_observation()
    {
        var client = new SqlServerMonitorClient(new StubProbe());
        var service = new MonitoredService { MonitorType = MonitorType.SqlServer };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Normalized.Status);
        Assert.Single(result.Normalized.Checks);
        Assert.Equal("SQL Server", result.Normalized.Checks[0].Name);
    }

    [Fact]
    public async Task Failed_probe_returns_unhealthy_with_the_failure_detail()
    {
        var client = new SqlServerMonitorClient(new StubProbe(new InvalidOperationException("login failed")));
        var service = new MonitoredService { MonitorType = MonitorType.SqlServer };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Normalized.Status);
        Assert.Contains("login failed", result.Normalized.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HealthStatus.Unhealthy, result.Normalized.Checks[0].Status);
    }

    private sealed class StubProbe(Exception? exception = null) : ISqlServerProbe
    {
        public Task ProbeAsync(MonitoredService _, CancellationToken cancellationToken) =>
            exception is null ? Task.CompletedTask : Task.FromException(exception);
    }
}
