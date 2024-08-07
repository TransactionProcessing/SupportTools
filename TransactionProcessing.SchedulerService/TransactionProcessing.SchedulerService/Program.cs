using Microsoft.Extensions.Hosting;
using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TransactionProcessing.SchedulerService.Jobs.Configuration;

namespace TransactionProcessing.SchedulerService
{
    using System.Collections.Specialized;
    using System.IO;
    using System.Reflection;
    using System.Threading;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Options;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using Quartz.Core;
    using Quartz.Logging;
    using Quartz.Simpl;
    using Quartz.Spi;
    using Shared.General;
    using SilkierQuartz;
    using SilkierQuartz.HostedService;

    public class Program
    {
        public static void Main(string[] args) {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            //At this stage, we only need our hosting file for ip and ports
            FileInfo fi = new FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location);

            IConfigurationRoot config = new ConfigurationBuilder().SetBasePath(fi.Directory.FullName)
                                                                  .AddJsonFile("hosting.json", optional: true)
                                                                  .AddJsonFile("hosting.development.json", optional: true)
                                                                  .AddEnvironmentVariables().Build();

            IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args);
            hostBuilder.UseWindowsService();
            hostBuilder.ConfigureWebHostDefaults(webBuilder =>
                                                 {
                                                     webBuilder.UseStartup<Startup>();
                                                     webBuilder.UseConfiguration(config);
                                                     webBuilder.UseKestrel();
                                                 }).ConfigureSilkierQuartzHost();

            return hostBuilder;
        }

    }
}
