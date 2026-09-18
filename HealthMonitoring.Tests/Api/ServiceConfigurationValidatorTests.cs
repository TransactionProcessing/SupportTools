using HealthMonitoring.Api;

namespace HealthMonitoring.Tests.Api;

public sealed class ServiceConfigurationValidatorTests
{
    [Fact]
    public void Rejects_non_http_urls_and_invalid_intervals()
    {
        var request = new ServiceRegistrationRequest("service", "Service", "Production", "ftp://service")
        {
            PollingIntervalSeconds = 3,
            RequestTimeoutSeconds = 5,
            RetentionDays = 0
        };

        var errors = ServiceConfigurationValidator.Validate(request);

        Assert.Contains(errors, error => error.Contains("HTTP", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("PollingInterval", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Retention", StringComparison.Ordinal));
    }

    [Fact]
    public void Self_registration_preserves_dashboard_managed_settings_for_existing_service()
    {
        var existing = new HealthMonitoring.Domain.MonitoredService
        {
            Id = Guid.NewGuid(),
            ServiceId = "service",
            Name = "Service",
            Environment = "Production",
            HealthUrl = new Uri("https://old/health"),
            PollingInterval = TimeSpan.FromMinutes(5),
            RetentionPeriod = TimeSpan.FromDays(10)
        };
        var request = new ServiceRegistrationRequest("service", "Updated", "Production", "https://new/health") { PollingIntervalSeconds = 30, RetentionDays = 1 };

        var result = ServiceConfigurationValidator.ToService(request, existing, preserveManagedSettings: true);

        Assert.Equal(TimeSpan.FromMinutes(5), result.PollingInterval);
        Assert.Equal(TimeSpan.FromDays(10), result.RetentionPeriod);
        Assert.Equal(new Uri("https://new/health"), result.HealthUrl);
    }
}
