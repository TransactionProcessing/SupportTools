using HealthMonitoring.Api;
using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;
using HealthMonitoring.Monitoring;
using HealthMonitoring.Reporting;
using HealthMonitoring.Components;
using HealthMonitoring.Health;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<HealthMonitoringDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("HealthMonitoring")));
builder.Services.AddScoped<IHealthMonitoringRepository, HealthMonitoringRepository>();
builder.Services.AddSingleton<IHealthStatusNormalizer, HealthStatusNormalizer>();
builder.Services.AddHttpClient<IHealthEndpointClient, HealthEndpointClient>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<IAlertDispatcher, LoggingAlertDispatcher>();
builder.Services.AddScoped<IncidentCalculator>();
builder.Services.AddHostedService<HealthPollingWorker>();
builder.Services.AddHostedService<RetentionCleanupWorker>();
builder.Services.AddHttpClient();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<IDashboardQueryService, DashboardQueryService>();
builder.Services.AddHealthChecks().AddCheck<SqlServerHealthCheck>("sql-server");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var connectionString = app.Configuration.GetConnectionString("HealthMonitoring");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        await scope.ServiceProvider.GetRequiredService<HealthMonitoringDbContext>().Database.EnsureCreatedAsync();
    }
}

app.MapServiceRegistrationEndpoints();
app.MapHealthChecks("/health");
app.UseStaticFiles();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public partial class Program;
