using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HealthMonitoring.Monitoring;

public static class HealthMonitoringServiceCollectionExtensions
{
    public static IServiceCollection AddHealthMonitoringServices(this IServiceCollection services)
    {
        services.AddScoped<HealthMonitoringRepository>();
        services.AddScoped<IHealthMonitoringRepository>(provider => provider.GetRequiredService<HealthMonitoringRepository>());
        services.AddScoped<IMonitoringStateRepository>(provider => provider.GetRequiredService<HealthMonitoringRepository>());
        services.AddSingleton<IHealthStatusNormalizer, HealthStatusNormalizer>();
        services.AddHttpClient<IHealthEndpointClient, HealthEndpointClient>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAlertDispatcher, LoggingAlertDispatcher>();
        services.AddScoped<IncidentCalculator>();
        services.AddHostedService<HealthPollingWorker>();
        services.AddHostedService<RetentionCleanupWorker>();
        return services;
    }
}
