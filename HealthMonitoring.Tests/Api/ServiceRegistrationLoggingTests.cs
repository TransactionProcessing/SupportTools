using HealthMonitoring.Api;
using HealthMonitoring.Domain;

namespace HealthMonitoring.Tests.Api;

public sealed class ServiceRegistrationLoggingTests
{
    [Fact]
    public void Registration_body_for_logging_redacts_connection_strings()
    {
        var request = new ServiceRegistrationRequest("orders-db", "Orders DB", "http://orders/health")
        {
            MonitorType = MonitorType.SqlServer,
            ConnectionString = "Server=orders;Password=secret"
        };

        var body = ServiceRegistrationLogFormatter.Serialize(request);

        Assert.Contains("orders-db", body);
        Assert.Contains("[REDACTED]", body);
        Assert.DoesNotContain("Password=secret", body);
    }
}
