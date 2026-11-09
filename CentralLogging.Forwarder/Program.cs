using CentralLogging.Forwarder;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "Central Logging Forwarder");
builder.Services.Configure<ForwarderOptions>(builder.Configuration.GetSection(ForwarderOptions.SectionName));
builder.Services.AddHttpClient("CentralLogs", client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<ForwarderCheckpointStore>();
builder.Services.AddHostedService<LogForwarderWorker>();

await builder.Build().RunAsync();
