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
        // HealthEndpointClient selects the default or certificate-bypassing client per service,
        // so it must receive IHttpClientFactory rather than be registered as a typed HttpClient.
        services.AddScoped<IHealthEndpointClient, HealthEndpointClient>();
        services.AddSingleton<IKurrentDbProbe, KurrentDbProbe>();
        services.AddScoped<KurrentDbMonitorClient>();
        services.AddSingleton<ISqlServerProbe, SqlServerProbe>();
        services.AddSingleton<SqlServerRegistrationVersionResolver>();
        services.AddScoped<SqlServerMonitorClient>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAlertDispatcher, LoggingAlertDispatcher>();
        services.AddScoped<IncidentCalculator>();
        services.AddHostedService<HealthPollingWorker>();
        services.AddHostedService<RetentionCleanupWorker>();
        return services;
    }
}
