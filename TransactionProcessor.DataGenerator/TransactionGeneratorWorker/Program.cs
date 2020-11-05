using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TransactionGeneratorWorker
{
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using EstateManagement.Client;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using NLog;
    using NLog.Config;
    using NLog.Extensions.Logging;
    using SecurityService.Client;
    using SecurityService.DataTransferObjects.Responses;
    using Shared.General;
    using Shared.Logger;
    using TransactionProcessor.Client;
    using Logger = Shared.Logger.Logger;

    /// <summary>
    /// 
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Creates the host builder.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <returns></returns>
        public static IHostBuilder CreateHostBuilder(String[] args)
        {
            return Host.CreateDefaultBuilder(args).UseWindowsService().UseSystemd().ConfigureServices((hostContext,
                                                                                                       services) =>
                                                                                                      {
                                                                                                          services.AddHostedService<Worker>();
                                                                                                          Program.SetupConfiguration(services);
                                                                                                          Program.SetupLogging(services);
                                                                                                          Program.ConfigureServices(services);
                                                                                                      });
        }

        /// <summary>
        /// Defines the entry point of the application.
        /// </summary>
        /// <param name="args">The arguments.</param>
        public static void Main(String[] args)
        {
            Program.CreateHostBuilder(args).Build().Run();
        }

        /// <summary>
        /// Configures the services.
        /// </summary>
        /// <param name="services">The services.</param>
        private static void ConfigureServices(IServiceCollection services)
        {
            HttpClient httpClient = new HttpClient();
            
            services.AddSingleton(httpClient);
            services.AddSingleton<Func<String, String>>(container => (serviceName) =>
                                                                     {
                                                                         return ConfigurationReader.GetBaseServerUri(serviceName).OriginalString;
                                                                     });
            services.AddSingleton<IEstateClient, EstateClient>();
            services.AddSingleton<ISecurityServiceClient, SecurityServiceClient>();
            services.AddSingleton<ITransactionProcessorClient, TransactionProcessorClient>();

        }

        /// <summary>
        /// Setups the configuration.
        /// </summary>
        /// <param name="services">The services.</param>
        private static void SetupConfiguration(IServiceCollection services)
        {
            // work with with a builder using multiple calls
            IConfigurationBuilder builder = new ConfigurationBuilder();
            builder.SetBasePath(Directory.GetCurrentDirectory());
            builder.AddJsonFile("appsettings.json", false);
            builder.AddJsonFile("appsettings.development.json", true).AddEnvironmentVariables();

            IConfigurationRoot configuration = builder.Build();

            ConfigurationReader.Initialise(configuration);
        }



        /// <summary>
        /// Setups the logging.
        /// </summary>
        /// <param name="services">The services.</param>
        private static void SetupLogging(IServiceCollection services)
        {
            services.AddLogging();
            ILoggerFactory loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();

            loggerFactory.AddNLog();

            LogManager.Configuration = new XmlLoggingConfiguration("nlog.config", true);

            //Logger needs initialised.
            Logger.Initialise(loggerFactory.CreateLogger<Program>());
        }

        

    }
}
