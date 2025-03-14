using TransactionProcessing.SchedulerService.Jobs.Jobs;

namespace TransactionProcessing.SchedulerService
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Jobs;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using NLog.Extensions.Logging;
    using Quartz;
    using Quartz.Impl;
    using Shared.Logger;
    using SilkierQuartz;
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

            String connectionString = Startup.Configuration.GetConnectionString("SchedulerReadModel");
            Startup.AddOrUpdateConnectionString("SchedulerReadModel", connectionString);
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

            IScheduler scheduler = StdSchedulerFactory.GetDefaultScheduler().Result;

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
}