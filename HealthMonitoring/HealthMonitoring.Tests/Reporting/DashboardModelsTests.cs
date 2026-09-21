using HealthMonitoring.Domain;
using HealthMonitoring.Reporting;

namespace HealthMonitoring.Tests.Reporting;

public sealed class DashboardModelsTests
{
    [Fact]
    public void ServiceDashboardRow_preserves_registered_version()
    {
        var row = new ServiceDashboardRow(
            "orders",
            "Orders",
            "Production",
            "2.4.1",
            HealthStatus.Healthy,
            TimeSpan.FromMilliseconds(20),
            99.9,
            DateTimeOffset.UtcNow,
            null);

        Assert.Equal("2.4.1", row.Version);
    }
}
