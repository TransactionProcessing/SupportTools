using System.Reflection;
using CentralLogging.Forwarder;
using Microsoft.Extensions.Configuration;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
FileInfo assemblyFile = new(Assembly.GetExecutingAssembly().Location);
String assemblyDirectory = assemblyFile.Directory!.FullName;
String externalConfigDirectory = OperatingSystem.IsWindows()
    ? @"C:\home\txnproc\config"
    : "/home/txnproc/config";
builder.Configuration
    .SetBasePath(assemblyDirectory)
    .AddJsonFile("hosting.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"hosting.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(externalConfigDirectory, "appsettings.json"), optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(externalConfigDirectory, $"appsettings.{builder.Environment.EnvironmentName}.json"), optional: true, reloadOnChange: true)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddJsonFile(Path.Combine(externalConfigDirectory, "appsettings.local.json"), optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Central Logging Forwarder");
builder.Services.Configure<ForwarderOptions>(builder.Configuration.GetSection(ForwarderOptions.SectionName));
builder.Services.AddHttpClient("CentralLogs", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<ForwarderCheckpointStore>();
builder.Services.AddHostedService<LogForwarderWorker>();

await builder.Build().RunAsync();
