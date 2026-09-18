using HealthMonitoring.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace HealthMonitoring.Tests;

public sealed class HealthMonitoringServiceRegistrationTests
{
    [Fact]
    public void Monitoring_registration_exposes_the_state_repository_required_by_incident_calculator()
    {
        var services = new ServiceCollection();

        services.AddHealthMonitoringServices();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMonitoringStateRepository));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IncidentCalculator));
    }
}
