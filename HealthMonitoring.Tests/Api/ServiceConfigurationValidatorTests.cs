using HealthMonitoring.Api;
using HealthMonitoring.Domain;

namespace HealthMonitoring.Tests.Api;

public sealed class ServiceConfigurationValidatorTests
{
    [Fact]
    public void Rejects_non_http_urls_and_invalid_intervals()
    {
        var request = new ServiceRegistrationRequest("service", "Service", "ftp://service")
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
        var request = new ServiceRegistrationRequest("service", "Updated", "https://new/health") { PollingIntervalSeconds = 30, RetentionDays = 1 };

        var result = ServiceConfigurationValidator.ToService(request, "Development", existing, preserveManagedSettings: true);

        Assert.Equal(TimeSpan.FromMinutes(5), result.PollingInterval);
        Assert.Equal(TimeSpan.FromDays(10), result.RetentionPeriod);
        Assert.Equal(new Uri("https://new/health"), result.HealthUrl);
    }

    [Fact]
    public void Registration_contract_does_not_accept_environment_from_the_service()
    {
        Assert.DoesNotContain(typeof(ServiceRegistrationRequest).GetProperties(), property => property.Name == "Environment");
    }

    [Fact]
    public void New_service_uses_the_dashboard_environment()
    {
        var request = new ServiceRegistrationRequest("service", "Service", "https://service/health");

        var result = ServiceConfigurationValidator.ToService(request, "Production");

        Assert.Equal("Production", result.Environment);
    }

    [Fact]
    public void Certificate_validation_setting_is_preserved_for_a_service()
    {
        var request = new ServiceRegistrationRequest("service", "Service", "https://service/health")
        {
            IgnoreCertificateErrors = true
        };

        var result = ServiceConfigurationValidator.ToService(request, "Development");

        Assert.True(result.IgnoreCertificateErrors);
    }

    [Fact]
    public void Kurrent_db_registration_requires_a_connection_string_instead_of_a_health_url()
    {
        var request = new ServiceRegistrationRequest
        {
            ServiceId = "eventstore",
            Name = "KurrentDB",
            MonitorType = MonitorType.KurrentDb
        };

        var errors = ServiceConfigurationValidator.Validate(request);

        Assert.DoesNotContain(errors, error => error.Contains("HealthUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ConnectionString", StringComparison.Ordinal));
    }

    [Fact]
    public void Kurrent_db_registration_stores_the_connection_string()
    {
        var request = new ServiceRegistrationRequest
        {
            ServiceId = "eventstore",
            Name = "KurrentDB",
            MonitorType = MonitorType.KurrentDb,
            ConnectionString = "esdb://admin:password@localhost:2113?tls=false"
        };

        var result = ServiceConfigurationValidator.ToService(request, "Development");

        Assert.Equal(MonitorType.KurrentDb, result.MonitorType);
        Assert.Equal(request.ConnectionString, result.ConnectionString);
    }

    [Fact]
    public void Sql_server_registration_requires_a_connection_string_instead_of_a_health_url()
    {
        var request = new ServiceRegistrationRequest
        {
            ServiceId = "orders-db",
            Name = "Orders SQL Server",
            MonitorType = MonitorType.SqlServer
        };

        var errors = ServiceConfigurationValidator.Validate(request);

        Assert.DoesNotContain(errors, error => error.Contains("HealthUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ConnectionString", StringComparison.Ordinal));
    }
}
