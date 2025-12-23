using MerchantPos.EF.Models;
using MerchantPos.EF.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SecurityService.Client;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using NLog;
using NLog.Extensions.Logging;
using NLog.Web;
using TransactionProcessing.MerchantPos.Runtime;
using TransactionProcessor.Client;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using ClientProxyBase;

var logger = LogManager.Setup().LoadConfigurationFromFile("nlog.config").GetCurrentClassLogger();
try
{
    // Use Info here so default config (Info+) writes this during startup
    logger.Info("Starting application initialization");

    // Explicitly construct the builder with environment support and then load environment-specific appsettings
    var envName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environments.Production;

    // IMPORTANT: use the application's folder (not process working dir) as the ContentRootPath.
    // When running as a service the current directory is often C:\Windows\System32 which causes the
    // "appsettings.json not found" error.
    var contentRoot = AppContext.BaseDirectory;

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = contentRoot,
        EnvironmentName = envName
    });

    // Load hosting.json so values such as "urls" or Kestrel endpoints are applied
    builder.Configuration.AddJsonFile("hosting.json", optional: true, reloadOnChange: true);

    // Explicit configuration ordering: appsettings.json, appsettings.{Environment}.json, environment vars, command line
    // Make appsettings.json optional to avoid hard crash when missing; log a warning instead.
    builder.Configuration
        .SetBasePath(builder.Environment.ContentRootPath)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddCommandLine(args);

    // Use NLog as the logging provider for the host
    builder.Host.UseNLog();

    // Enable running as a Windows Service (call before Build)
    builder.Host.UseWindowsService();

    // Configure logging (providers) - NLog will be used via UseNLog
    builder.Logging.ClearProviders();         // Remove default providers
    builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
    // Keep Console/Debug providers if you still want them alongside NLog (optional)
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
    builder.Logging.AddNLog();

    // Worker Services
    builder.Services.AddHostedService<WorkerHost>();

    var connectionString = builder.Configuration.GetConnectionString("MerchantDb");

    // EF Core
    builder.Services.AddDbContext<MerchantDbContext>(options =>
    {
        options.UseSqlite(connectionString);
    });

    // Repos + services
    builder.Services.AddScoped<IEfRepository, EfRepository>();
    builder.Services.AddScoped<IApiClient, ApiClient>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.RegisterHttpClient<ISecurityServiceClient, SecurityServiceClient>();
    builder.Services.RegisterHttpClient<ITransactionProcessorClient, TransactionProcessorClient>();
    builder.Services.AddHttpClient();

    builder.Services.AddScoped<MerchantRuntime>();
    builder.Services.AddSingleton<IMerchantRuntimeFactory, MerchantRuntimeFactory>();
    builder.Services.AddSingleton<MerchantMetrics>();
    //builder.Services.AddSingleton<Func<String, String>>(
    //                                                                configSetting =>
    //                                                                {
    //                                                                    return configSetting switch
    //                                                                    {
    //                                                                        "SecurityService" => "https://localhost:5001",
    //                                                                        "TransactionProcessorACL" => "http://localhost:5003",
    //                                                                        "TransactionProcessorApi" => "http://localhost:5002",
    //                                                                        "TestHost" => "http://localhost:9000",
    //                                                                        _ => string.Empty,
    //                                                                    };
    //                                                                });

    // Replace the existing AddSingleton<Func<String, String>>(...) registration with this:
    builder.Services.AddSingleton<Func<string, string>>(sp =>
    {
        // Resolve IConfiguration from the DI container
        var config = sp.GetRequiredService<IConfiguration>().GetSection("ApiConfiguration");

        // Return a small resolver that looks up keys in the ApiConfiguration section (case-insensitive)
        return (string configSetting) =>
        {
            if (string.IsNullOrWhiteSpace(configSetting))
                return string.Empty;

            // Section indexer is case-sensitive by default, so use GetChildren() to perform case-insensitive lookup
            var child = config.GetChildren()
                .FirstOrDefault(c => string.Equals(c.Key, configSetting, StringComparison.OrdinalIgnoreCase));
            return child?.Value ?? string.Empty;
        };
    });
    // Health checks
    builder.Services.AddHealthChecks();
        
    // Bind config
    var settings = new WorkerSettings();
    builder.Configuration.GetSection("WorkerSettings").Bind(settings);
    builder.Services.AddSingleton(settings);

    // --- Build the web app (this replaces ConfigureWebHostDefaults) ---
    var app = builder.Build();

    // If appsettings.json was missing, log a warning so the deployment issue is visible
    var cfgFile = Path.Combine(builder.Environment.ContentRootPath, "appsettings.json");
    if (!File.Exists(cfgFile))
    {
        var warnLogger = app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
        warnLogger.LogWarning("appsettings.json was not found at {Path}. Using defaults and environment variables.", cfgFile);
    }

    // Auto-create SQLite database and tables
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<MerchantDbContext>();
        db.Database.EnsureCreated();

        var diLogger = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
        Shared.Logger.Logger.Initialise(diLogger);
    }

    app.MapGet("/metrics/stream", async (HttpContext ctx, IEfRepository repo, MerchantMetrics metrics) =>
    {
        ctx.Response.Headers.Add("Content-Type", "text/event-stream");
        ctx.Response.Headers.Add("Cache-Control", "no-cache");

        while (!ctx.RequestAborted.IsCancellationRequested)
        {
            var balances = await repo.GetAllMerchants();

            var dto = balances.Select(b => new
            {
                MerchantId = b.MerchantId,
                MerchantName = b.MerchantName,
                Balance = metrics.Get(b.MerchantId).Balance,
                Sales = metrics.Get(b.MerchantId).SalesCount,
                FailedSales = metrics.Get(b.MerchantId).FailedSales,
                LastSale = metrics.Get(b.MerchantId).LastSaleUtc,
                LastEndOfDay = metrics.Get(b.MerchantId).LastEndOfDay
            }).ToList();

            string json = JsonSerializer.Serialize(dto);
            await ctx.Response.WriteAsync($"data: {json}\n\n");
            await ctx.Response.Body.FlushAsync();

            await Task.Delay(1000); // update every second
        }
    });

    app.MapGet("/dashboard", () =>
    {
        var html = @"
<!DOCTYPE html>
<html>
<head>
  <title>Merchant Dashboard</title>
  <style>
    body { font-family: Arial; margin: 20px; }
    table { border-collapse: collapse; width: 100%; }
    th, td { border: 1px solid #ccc; padding: 8px; text-align: left; }
    th { background: #eee; }
  </style>
</head>
<body>
  <h2>Real-Time Merchant Metrics</h2>

  <table id='tbl'>
    <thead>
      <tr>
        <th>Merchant</th>
        <th>Balance</th>
        <th>Sales Count</th>
        <th>Failed Sales</th>
        <th>Last Sale (UTC)</th>
        <th>Last EOD (UTC)</th>
      </tr>
    </thead>
    <tbody></tbody>
  </table>

  <script>
    const evt = new EventSource('/metrics/stream');
    evt.onmessage = function(e) {
        let data = JSON.parse(e.data);
        let tbody = document.querySelector('#tbl tbody');
        tbody.innerHTML = '';

        data.forEach(row => {
            tbody.innerHTML += `
              <tr>
                <td>${row.MerchantName}</td>
                <td>${row.Balance}</td>
                <td>${row.Sales}</td>
                <td>${row.FailedSales}</td>
                <td>${row.LastSale ?? ''}</td>
                <td>${row.LastEndOfDay ?? ''}</td>
              </tr>`;
        });
    };
  </script>
</body>
</html>";

        return Results.Text(html, "text/html");
    });

    // Health Check Endpoint
    app.MapHealthChecks("/health");

    // Run both web app + background workers
    await app.RunAsync();
}
catch (Exception ex)
{
    // NLog: catch setup errors
    logger.Error(ex, "Application stopped because of exception");
    throw;
}
finally
{
    // Ensure to flush and stop internal timers/threads before application-exit (important for NLog)
    LogManager.Shutdown();
}


public record BalanceDto(Guid MerchantId, decimal Balance);


public class MerchantMetrics
{
    private readonly ConcurrentDictionary<Guid, MerchantMetricSnapshot> _metrics
        = new();

    public MerchantMetricSnapshot Get(Guid merchantId)
        => _metrics.GetOrAdd(merchantId, new MerchantMetricSnapshot());

    public void IncrementSales(Guid merchantId)
    {
        var m = Get(merchantId);
        Interlocked.Increment(ref m.SalesCount);
        m.LastSaleUtc = DateTime.UtcNow;
    }

    public void IncrementFailedSales(Guid merchantId)
    {
        var m = Get(merchantId);
        Interlocked.Increment(ref m.FailedSales);
    }

    public void SetBalance(Guid merchantId, Decimal balance)
    {
        var m = Get(merchantId);
        m.Balance = balance;
    }

    public void SetLastEndOfDay(Guid merchantId)
    {
        var m = Get(merchantId);
        m.LastEndOfDay = DateTime.UtcNow;
    }
}

public class MerchantMetricSnapshot
{
    public int SalesCount;
    public int FailedSales;
    public decimal Balance;
    public DateTime? LastSaleUtc;
    public DateTime? LastEndOfDay;
}