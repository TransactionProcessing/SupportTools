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
    using NLog.Extensions.Logging;
    using Quartz;
    using Quartz.Impl;
    using Quartz.Impl.AdoJobStore.Common;
    using Quartz.Impl.Matchers;
    using Quartz.Spi;
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

            //String connectionString = Startup.Configuration.GetConnectionString("X");
            //Startup.AddOrUpdateConnectionString("X", connectionString);
        }

        #endregion

        #region Properties
        
        public static IConfigurationRoot Configuration{ get; set; }
        
        public static IWebHostEnvironment WebHostEnvironment{ get; set; }

        #endregion

        #region Methods

        public static void AddOrUpdateConnectionString(String name,
                                                       String connectionString){
            try{
                Configuration configFile = System.Configuration.ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                ConnectionStringSettingsCollection settings = configFile.ConnectionStrings.ConnectionStrings;

                if (settings[name] == null){
                    settings.Add(new ConnectionStringSettings(name, connectionString));
                }
                else{
                    settings[name].ConnectionString = connectionString;
                }

                configFile.Save(ConfigurationSaveMode.Modified);
                System.Configuration.ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch(ConfigurationErrorsException){
                Console.WriteLine("Error writing connection string");
            }
        }

        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {

            String nlogConfigFilename = "nlog.config";

            if (env.IsDevelopment()){
                app.UseDeveloperExceptionPage();
            }
            else{
                app.UseExceptionHandler("/Error");
            }

            
            loggerFactory.ConfigureNLog(Path.Combine(env.ContentRootPath, nlogConfigFilename));
            loggerFactory.AddNLog();
            
            ILogger logger = loggerFactory.CreateLogger("EstateManagement");

            Logger.Initialise(logger);

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
            //var g = scheduler.Context.Get("Quartz.Plugins.RecentHistory.Impl.InProcExecutionHistoryStore,Quartz.Plugins.RecentHistory");
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
        private readonly ILogger<CustomSqlServerConnectionProvider> logger;
        private readonly IConfiguration configuration;

        public CustomSqlServerConnectionProvider(
            ILogger<CustomSqlServerConnectionProvider> logger,
            IConfiguration configuration)
        {
            this.logger = logger;
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
            logger.LogInformation("Initializing");
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
            logger.LogInformation("Shutting down");
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

            // a custom time provider will be pulled from DI
            //services.AddSingleton<TimeProvider, CustomTimeProvider>();

            // async disposable
            //services.AddScoped<AsyncDisposableDependency>();

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
                        // if needed, could create a custom strategy for handling connections
                        //sqlServer.UseConnectionProvider<CustomSqlServerConnectionProvider>();

                        sqlServer.ConnectionString = configuration.GetConnectionString("Quartz");

                        // or from appsettings.json
                        // sqlServer.ConnectionStringName = "Quartz";

                        // this is the default
                        sqlServer.TablePrefix = "QRTZ_";
                    });
                    s.UseNewtonsoftJsonSerializer();
                    //s.UseClustering(c =>
                    //{
                    //    c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                    //    c.CheckinInterval = TimeSpan.FromSeconds(10);
                    //});
                });
                q.UseDefaultThreadPool(maxConcurrency: 10);
                q.UseTimeZoneConverter();
                
                ExecutionHistoryPlugin p = new ExecutionHistoryPlugin();
                p.StoreType = Type.GetType("Quartz.Plugins.RecentHistory.Impl.InProcExecutionHistoryStore,Quartz.Plugins.RecentHistory");
                p.Name = "ExecutionHistoryPlugin";
                q.AddJobListener<ExecutionHistoryPlugin>(p);
            });
            

            return services;
        }
    }

    public class CustomTypeLoader : ITypeLoadHelper
    {
        private readonly ILogger<CustomTypeLoader> logger;

        public CustomTypeLoader(ILogger<CustomTypeLoader> logger)
        {
            this.logger = logger;
        }

        public void Initialize()
        {
        }

        public Type? LoadType(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            logger.LogInformation("Requested to load type {TypeName}", name);
            
            var x  = Type.GetType(name);
            return x;
        }
    }
}