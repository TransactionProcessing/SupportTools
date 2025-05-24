using Quartz.Plugins.RecentHistory;
using Quartz.Plugins.RecentHistory.Impl;
using TransactionProcessing.SchedulerService.Jobs.Jobs;

namespace TransactionProcessing.SchedulerService
{
    using Jobs;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Data.SqlClient;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NLog;
    using NLog.Config;
    using NLog.Extensions.Logging;
    using Quartz;
    using Quartz.Impl;
    using Quartz.Impl.AdoJobStore.Common;
    using Quartz.Impl.Matchers;
    using Quartz.Spi;
    using Shared.Extensions;
    using Shared.General;
    using Shared.Logger;
    using SilkierQuartz;
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Data;
    using System.Data.Common;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using ILogger = Microsoft.Extensions.Logging.ILogger;
    using SetupBuilderExtensions = NLog.SetupBuilderExtensions;

    /// <summary>
    /// 
    /// </summary>
    public class Startup{
        #region Constructors

        public Startup(IWebHostEnvironment webHostEnvironment){
            IConfigurationBuilder builder = new ConfigurationBuilder().SetBasePath(webHostEnvironment.ContentRootPath)
                                                                      .AddJsonFile("/home/txnproc/config/appsettings.json", true, true)
                                                                      .AddJsonFile($"/home/txnproc/config/appsettings.{webHostEnvironment.EnvironmentName}.json",
                                                                                   optional:true).AddJsonFile("appsettings.json", optional:true, reloadOnChange:true)
                                                                      .AddJsonFile($"appsettings.{webHostEnvironment.EnvironmentName}.json",
                                                                                   optional:true,
                                                                                   reloadOnChange:true).AddEnvironmentVariables();

            Startup.Configuration = builder.Build();
            Startup.WebHostEnvironment = webHostEnvironment;
        }

        #endregion

        #region Properties
        
        public static IConfigurationRoot Configuration{ get; set; }
        
        public static IWebHostEnvironment WebHostEnvironment{ get; set; }

        #endregion

        #region Methods

        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            ConfigurationReader.Initialise(Startup.Configuration);

            String nlogConfigFilename = "nlog.config";

            //if (env.IsDevelopment())
            //{
            //    app.UseDeveloperExceptionPage();
            //    string directoryPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            //    LogManager.AddHiddenAssembly(Assembly.LoadFrom(Path.Combine(directoryPath, "Shared.dll")));

            //    var developmentNlogConfigFilename = "nlog.development.config";
            //    if (File.Exists(Path.Combine(env.ContentRootPath, developmentNlogConfigFilename)))
            //    {
            //        nlogConfigFilename = developmentNlogConfigFilename;
            //    }
            //}
            //else
            //{
            //    LogManager.AddHiddenAssembly(Assembly.LoadFrom(Path.Combine(env.ContentRootPath, "Shared.dll")));
            //}

            //loggerFactory.ConfigureNLog(Path.Combine(env.ContentRootPath, nlogConfigFilename));
            //loggerFactory.AddNLog();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();

                string directoryPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                LogManager.AddHiddenAssembly(Assembly.LoadFrom(Path.Combine(directoryPath, "Shared.dll")));

                var developmentNlogConfigFilename = "nlog.development.config";
                var developmentConfigPath = Path.Combine(env.ContentRootPath, developmentNlogConfigFilename);

                if (File.Exists(developmentConfigPath))
                {
                    nlogConfigFilename = developmentNlogConfigFilename;
                }
            }
            else
            {
                LogManager.AddHiddenAssembly(Assembly.LoadFrom(Path.Combine(env.ContentRootPath, "Shared.dll")));
            }

            string configPath = Path.Combine(env.ContentRootPath, nlogConfigFilename);

            // Correct way to load config with auto-reload support
            LogManager.Configuration = new XmlLoggingConfiguration(configPath);

            // Register NLog with ILoggerFactory
            loggerFactory.AddNLog();

            ILogger logger = loggerFactory.CreateLogger("TransactionProcessor");

            Shared.Logger.Logger.Initialise(logger);

            Startup.Configuration.LogConfiguration(Shared.Logger.Logger.LogWarning);

            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSilkierQuartz();

            app.UseEndpoints(endpoints => { endpoints.MapRazorPages(); });
        }

        

        public void ConfigureServices(IServiceCollection services){
            services.AddRazorPages();

            this.RegisterJobs(services);

            services.AddScheduler(Configuration);

            var serviceProvider = services.BuildServiceProvider();
            ISchedulerFactory schedulerFactory = serviceProvider.GetRequiredService<ISchedulerFactory>();

            IScheduler scheduler = schedulerFactory.GetScheduler().Result;
            IExecutionHistoryStore store = new InProcExecutionHistoryStore();
            scheduler.Context.SetExecutionHistoryStore(store);

            services.AddSilkierQuartz(s => {
                                          s.VirtualPathRoot = "/quartz";
                                          s.UseLocalTime = true;
                                          s.DefaultDateFormat = "yyyy-MM-dd";
                                          s.DefaultTimeFormat = "HH:mm:ss";
                                          s.Scheduler = scheduler;
                                          
                },
                                      a => { a.AccessRequirement = SilkierQuartzAuthenticationOptions.SimpleAccessRequirement.AllowAnonymous; });
        }

        private void RegisterJobs(IServiceCollection services)
        {
            Type type = typeof(BaseJob);
            IEnumerable<Type> jobs = AppDomain.CurrentDomain.GetAssemblies().SelectMany(s => s.GetTypes())
                                              .Where(p => type.IsAssignableFrom(p)
                                                          && p.IsInterface == false
                                                          && p.IsAbstract == false);

            foreach (Type job in jobs)
            {
                services.AddSingleton(job);
            }
        }

        #endregion
    }

    public class CustomSqlServerConnectionProvider : IDbProvider
    {
        //private readonly ILogger<CustomSqlServerConnectionProvider> logger;
        private readonly IConfiguration configuration;

        public CustomSqlServerConnectionProvider(IConfiguration configuration)
        {
            this.configuration = configuration;
            Metadata = new DbMetadata
            {
                AssemblyName = typeof(SqlConnection).AssemblyQualifiedName,
                BindByName = true,
                CommandType = typeof(SqlCommand),
                ConnectionType = typeof(SqlConnection),
                DbBinaryTypeName = "VarBinary",
                ExceptionType = typeof(SqlException),
                ParameterDbType = typeof(SqlDbType),
                ParameterDbTypePropertyName = "SqlDbType",
                ParameterNamePrefix = "@",
                ParameterType = typeof(SqlParameter),
                UseParameterNamePrefixInParameterCollection = true
            };
            Metadata.Init();
        }

        public void Initialize()
        {
            Shared.Logger.Logger.LogInformation("Initializing");
        }

        public DbCommand CreateCommand()
        {
            return new SqlCommand();
        }

        public DbConnection CreateConnection()
        {
            return new SqlConnection(ConnectionString);
        }

        public string ConnectionString
        {
            get => configuration.GetConnectionString("Quartz")!;
            set => throw new NotImplementedException();
        }

        public DbMetadata Metadata { get; }

        public void Shutdown()
        {
            Shared.Logger.Logger.LogInformation("Shutting down");
        }
    }

    public static class Extensions {
        public static IServiceCollection AddScheduler(this IServiceCollection services, IConfiguration configuration)
        {
            // base configuration for DI, read from appSettings.json
            services.Configure<QuartzOptions>(configuration.GetSection("Quartz"));

            // if you are using persistent job store, you might want to alter some options
            services.Configure<QuartzOptions>(options =>
            {
                options.Scheduling.IgnoreDuplicates = true; // default: false
                options.Scheduling.OverWriteExistingData = true; // default: true
            });

            // custom connection provider
            services.AddSingleton<IDbProvider, CustomSqlServerConnectionProvider>();

            services.AddQuartz(q => {
                // handy when part of cluster or you want to otherwise identify multiple schedulers
                q.SchedulerId = "Scheduler-Core";

                // you can control whether job interruption happens for running jobs when scheduler is shutting down
                q.InterruptJobsOnShutdown = true;

                // when QuartzHostedServiceOptions.WaitForJobsToComplete = true or scheduler.Shutdown(waitForJobsToComplete: true)
                q.InterruptJobsOnShutdownWithWait = true;

                // we can change from the default of 1
                q.MaxBatchSize = 5;

                // we take this from appsettings.json, just show it's possible
                q.SchedulerName = "Txn Processing Scheduler";
                
                // these are the defaults
                q.UseSimpleTypeLoader();
                q.UsePersistentStore(s => {
                    s.PerformSchemaValidation = false; // default
                    //s.UseProperties = true; // preferred, but not default
                    s.RetryInterval = TimeSpan.FromSeconds(15);
                    s.UseSqlServer(sqlServer =>
                    {
                        sqlServer.ConnectionString = configuration.GetConnectionString("Quartz");

                        // this is the default
                        sqlServer.TablePrefix = "QRTZ_";
                    });
                    s.UseNewtonsoftJsonSerializer();
                });
                q.UseDefaultThreadPool(maxConcurrency: 10);
                q.UseTimeZoneConverter();
                
                ExecutionHistoryPlugin p = new ExecutionHistoryPlugin();
                p.StoreType = Type.GetType("Quartz.Plugins.RecentHistory.Impl.InProcExecutionHistoryStore,Quartz.Plugins.RecentHistory");
                p.Name = "ExecutionHistoryPlugin";
                //q.AddJobListener<ExecutionHistoryPlugin>(p);
            });
            
            return services;
        }
    }
}