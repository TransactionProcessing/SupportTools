using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HealthMonitoring.Client;

public sealed class HealthMonitoringRegistrationConfiguration
{
    public Uri MonitoringServerUrl { get; set; } = new("http://localhost:9620/");
    public ServiceRegistrationOptions Service { get; set; } = new();
    public List<DependencyMappingOptions> Dependencies { get; set; } = [];
}

public sealed class HealthMonitoringRegistrationHostedService(
    IHealthMonitoringRegistrationClient client,
    HealthMonitoringRegistrationConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await client.RegisterAndConfigureAsync(
            configuration.Service,
            configuration.Dependencies,
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class HealthMonitoringRegistrationExtensions
{
    public static IServiceCollection AddHealthMonitoringRegistration(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "HealthMonitoring")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        var section = configuration.GetSection(sectionName);
        if (!section.Exists())
            return services;

        var registration = new HealthMonitoringRegistrationConfiguration();
        section.Bind(registration);
        if (!registration.MonitoringServerUrl.IsAbsoluteUri)
            throw new ArgumentException("The monitoring server URL must be absolute.", nameof(configuration));

        services.AddSingleton(registration);
        services.AddHttpClient<IHealthMonitoringRegistrationClient, HealthMonitoringRegistrationClient>(client =>
        {
            client.BaseAddress = registration.MonitoringServerUrl;
        });
        services.AddHostedService<HealthMonitoringRegistrationHostedService>();

        return services;
    }
}
