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

    [Fact]
    public void TimelineObservationSelector_returns_latest_100_in_display_order()
    {
        var observations = Enumerable.Range(0, 101)
            .Select(index => new HealthObservation
            {
                ObservedAtUtc = DateTimeOffset.UtcNow.AddMinutes(index),
                Status = HealthStatus.Healthy
            })
            .ToArray();

        var selected = TimelineObservationSelector.Select(observations).ToArray();

        Assert.Equal(100, selected.Length);
        Assert.Equal(observations[1].ObservedAtUtc, selected[0].ObservedAtUtc);
        Assert.Equal(observations[100].ObservedAtUtc, selected[^1].ObservedAtUtc);
    }

    [Fact]
    public void TimelineObservationSelector_exposes_maximum_segments_as_read_only_property()
    {
        var property = typeof(TimelineObservationSelector).GetProperty(nameof(TimelineObservationSelector.MaximumSegments));

        Assert.NotNull(property);
        Assert.False(property!.CanWrite);
        Assert.Equal(100, property.GetValue(null));
    }
}
