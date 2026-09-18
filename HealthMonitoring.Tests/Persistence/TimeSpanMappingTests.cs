using HealthMonitoring.Persistence;
using AppDomain = HealthMonitoring.Domain;
using Microsoft.EntityFrameworkCore;

namespace HealthMonitoring.Tests.Persistence;

public sealed class TimeSpanMappingTests
{
    [Fact]
    public void Long_duration_properties_use_a_numeric_sql_provider_type()
    {
        var options = new DbContextOptionsBuilder<HealthMonitoringDbContext>().UseSqlServer("Server=.;Database=ignored;").Options;
        using var context = new HealthMonitoringDbContext(options);
        var model = context.Model;

        var service = model.FindEntityType(typeof(AppDomain.MonitoredService))!;
        var observation = model.FindEntityType(typeof(AppDomain.HealthObservation))!;
        var check = model.FindEntityType(typeof(AppDomain.HealthCheckResult))!;
        var snapshot = model.FindEntityType(typeof(AppDomain.ServiceStatusSnapshot))!;

        Assert.Equal(typeof(long), service.FindProperty(nameof(AppDomain.MonitoredService.RetentionPeriod))!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(typeof(long), service.FindProperty(nameof(AppDomain.MonitoredService.PollingInterval))!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(typeof(long), service.FindProperty(nameof(AppDomain.MonitoredService.RequestTimeout))!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(typeof(long), observation.FindProperty(nameof(AppDomain.HealthObservation.ResponseDuration))!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(typeof(long), check.FindProperty(nameof(AppDomain.HealthCheckResult.Duration))!.GetTypeMapping().Converter!.ProviderClrType);
        Assert.Equal(typeof(long?), snapshot.FindProperty(nameof(AppDomain.ServiceStatusSnapshot.LastResponseDuration))!.GetTypeMapping().Converter!.ProviderClrType);
    }
}
