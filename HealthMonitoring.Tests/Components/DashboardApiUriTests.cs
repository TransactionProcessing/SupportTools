using HealthMonitoring.Components;

namespace HealthMonitoring.Tests.Components;

public sealed class DashboardApiUriTests
{
    [Fact]
    public void Builds_an_absolute_uri_from_the_dashboard_base_uri()
    {
        var result = DashboardApiUri.Create("http://localhost:9620/", "api/services/orders");

        Assert.Equal("http://localhost:9620/api/services/orders", result.AbsoluteUri);
    }
}
