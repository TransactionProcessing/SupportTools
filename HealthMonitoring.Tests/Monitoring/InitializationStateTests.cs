using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class InitializationStateTests
{
    [Fact]
    public void Base_initialization_is_complete_only_when_both_required_monitor_types_exist()
    {
        var services = new[]
        {
            new MonitoredService { MonitorType = MonitorType.SqlServer },
            new MonitoredService { MonitorType = MonitorType.KurrentDb }
        };

        Assert.True(InitializationStateCalculator.IsComplete(services));
        Assert.False(InitializationStateCalculator.IsComplete(services.Take(1)));
    }

    [Fact]
    public void Archived_monitors_do_not_complete_initialization()
    {
        var services = new[]
        {
            new MonitoredService { MonitorType = MonitorType.SqlServer },
            new MonitoredService { MonitorType = MonitorType.KurrentDb, ArchivedAtUtc = DateTimeOffset.UtcNow }
        };

        Assert.False(InitializationStateCalculator.IsComplete(services));
    }

    [Fact]
    public void Each_base_monitor_requirement_is_evaluated_independently()
    {
        var services = new[] { new MonitoredService { MonitorType = MonitorType.SqlServer } };

        Assert.True(InitializationStateCalculator.IsComplete(services, requireSqlServer: true, requireKurrentDb: false));
        Assert.False(InitializationStateCalculator.IsComplete(services, requireSqlServer: false, requireKurrentDb: true));
        Assert.True(InitializationStateCalculator.IsComplete([], requireSqlServer: false, requireKurrentDb: false));
    }
}
