using HealthMonitoring.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HealthMonitoring.Tests.Client;

public sealed class HealthMonitoringRegistrationExtensionsTests
{
    [Fact]
    public void Configuration_registration_binds_service_settings_and_adds_startup_registration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthMonitoring:MonitoringServerUrl"] = "https://monitoring.example/",
                ["HealthMonitoring:Service:ServiceId"] = "mobile-configuration",
                ["HealthMonitoring:Service:Name"] = "Mobile Configuration",
                ["HealthMonitoring:Service:HealthUrl"] = "https://mobile.example/healthui",
                ["HealthMonitoring:Service:Description"] = "Mobile Configuration",
                ["HealthMonitoring:Service:PollingInterval"] = "00:02:00",
                ["HealthMonitoring:Dependencies:0:DependencyName"] = "Security Service",
                ["HealthMonitoring:Dependencies:0:TargetServiceId"] = "security-service"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddHealthMonitoringRegistration(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<HealthMonitoringRegistrationConfiguration>();

        Assert.Equal(new Uri("https://monitoring.example/"), options.MonitoringServerUrl);
        Assert.Equal("mobile-configuration", options.Service.ServiceId);
        Assert.Equal(TimeSpan.FromMinutes(2), options.Service.PollingInterval);
        Assert.Single(options.Dependencies);
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is HealthMonitoringRegistrationHostedService);
    }

    [Fact]
    public void Missing_configuration_section_skips_automatic_registration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddHealthMonitoringRegistration(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IHealthMonitoringRegistrationClient>());
    }

    [Fact]
    public async Task Startup_registration_calls_register_and_configure()
    {
        var configuration = new HealthMonitoringRegistrationConfiguration
        {
            Service = new ServiceRegistrationOptions
            {
                ServiceId = "mobile-configuration",
                Name = "Mobile Configuration",
                HealthUrl = new Uri("https://mobile.example/healthui")
            },
            Dependencies = [new DependencyMappingOptions("Security Service", "security-service")]
        };
        var client = new RecordingRegistrationClient();

        await new HealthMonitoringRegistrationHostedService(client, configuration)
            .StartAsync(CancellationToken.None);

        Assert.Same(configuration.Service, client.Service);
        Assert.Same(configuration.Dependencies, client.Dependencies);
    }

    private sealed class RecordingRegistrationClient : IHealthMonitoringRegistrationClient
    {
        public ServiceRegistrationOptions? Service { get; private set; }
        public IEnumerable<DependencyMappingOptions>? Dependencies { get; private set; }

        public Task<ServiceRegistrationResult> RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetDependencyMappingsAsync(string serviceId, IEnumerable<DependencyMappingOptions> mappings, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceRegistrationResult> RegisterAndConfigureAsync(ServiceRegistrationOptions options, IEnumerable<DependencyMappingOptions> mappings, CancellationToken cancellationToken = default)
        {
            Service = options;
            Dependencies = mappings;
            return Task.FromResult(new ServiceRegistrationResult(options.ServiceId, "created", Guid.NewGuid()));
        }
    }
}
