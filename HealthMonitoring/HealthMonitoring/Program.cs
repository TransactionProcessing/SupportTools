using HealthMonitoring.Api;
using HealthMonitoring.Domain;
using HealthMonitoring.Persistence;
using HealthMonitoring.Monitoring;
using HealthMonitoring.Reporting;
using HealthMonitoring.Components;
using HealthMonitoring.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Transaction Processing - Health Monitoring";
});

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile("/home/txnproc/config/appsettings.json", true, true)
    .AddJsonFile($"/home/txnproc/config/appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile($"/home/txnproc/config/appsettings.local.json", optional: true)
    .AddJsonFile("hosting.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddSingleton(new InitializationOptions
{
    RequireSqlServer = builder.Configuration.GetValue("Initialization:RequireSqlServer", true),
    RequireKurrentDb = builder.Configuration.GetValue("Initialization:RequireKurrentDb", true)
});
builder.Services.Configure<SqlServerMonitorOptions>(builder.Configuration.GetSection("Monitoring:SqlServer"));

builder.Services.AddDbContext<HealthMonitoringDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("HealthMonitoring")));
builder.Services.AddHealthMonitoringServices();
builder.Services.AddHttpClient();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddScoped<IDashboardQueryService, DashboardQueryService>();
builder.Services.AddScoped<IInitializationService, InitializationService>();
builder.Services.AddHealthChecks().AddCheck<SqlServerHealthCheck>("sql-server");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var connectionString = app.Configuration.GetConnectionString("HealthMonitoring");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        await scope.ServiceProvider
            .GetRequiredService<HealthMonitoringDbContext>()
            .Database
            .MigrateAsync();
    }
}

app.MapServiceRegistrationEndpoints(app.Environment.EnvironmentName);
app.MapDependencyMappingEndpoints();
app.MapInitializationEndpoints(app.Environment.EnvironmentName);
app.MapHealthChecks("/health");
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

public partial class Program
{
    protected Program()
    {
    }
}
