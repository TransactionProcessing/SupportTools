using HealthMonitoring.Domain;
using HealthMonitoring.Reporting;

namespace HealthMonitoring.Tests.Reporting;

public sealed class ObservationMetricsTests
{
    [Fact]
    public void Uptime_percent_uses_all_observations_not_the_display_window()
    {
        var metrics = new ObservationMetrics(250, 225);

        Assert.Equal(250, metrics.TotalCount);
        Assert.Equal(90d, metrics.UptimePercent);
    }

    [Fact]
    public void Uptime_percent_is_zero_when_no_observations_exist()
    {
        var metrics = new ObservationMetrics(0, 0);

        Assert.Equal(0d, metrics.UptimePercent);
    }
}
