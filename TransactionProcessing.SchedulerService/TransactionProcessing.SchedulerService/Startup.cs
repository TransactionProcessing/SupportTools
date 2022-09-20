namespace TransactionProcessing.SchedulerService
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
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
    using Quartz;
    using Quartz.Impl;
    using SilkierQuartz;
    using ConfigurationManager = System.Configuration.ConfigurationManager;

    /// <summary>
    /// 
    /// </summary>
    public class Startup
    {
        #region Fields

        /// <summary>
        /// The job bootstrappers
        /// </summary>
        private readonly List<(String name, IBootstrapper instance)> JobBootstrappers = new List<(String, IBootstrapper)>();

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="Startup"/> class.
        /// </summary>
        /// <param name="webHostEnvironment">The web host environment.</param>
        public Startup(IWebHostEnvironment webHostEnvironment)
        {
            //this.Configuration = configuration;
            IConfigurationBuilder builder = new ConfigurationBuilder().SetBasePath(webHostEnvironment.ContentRootPath)
                                                                      .AddJsonFile("/home/txnproc/config/appsettings.json", true, true)
                                                                      .AddJsonFile($"/home/txnproc/config/appsettings.{webHostEnvironment.EnvironmentName}.json",
                                                                                   optional:true).AddJsonFile("appsettings.json", optional:true, reloadOnChange:true)
                                                                      .AddJsonFile($"appsettings.{webHostEnvironment.EnvironmentName}.json",
                                                                                   optional:true,
                                                                                   reloadOnChange:true).AddEnvironmentVariables();

            Startup.Configuration = builder.Build();
            Startup.WebHostEnvironment = webHostEnvironment;

            var connectionString = Startup.Configuration.GetConnectionString("SchedulerReadModel");
            Startup.AddOrUpdateConnectionString("SchedulerReadModel", connectionString);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the configuration.
        /// </summary>
        /// <value>
        /// The configuration.
        /// </value>
        public static IConfigurationRoot Configuration { get; set; }

        /// <summary>
        /// Gets or sets the web host environment.
        /// </summary>
        /// <value>
        /// The web host environment.
        /// </value>
        public static IWebHostEnvironment WebHostEnvironment { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Adds the or update connection string.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="connectionString">The connection string.</param>
        public static void AddOrUpdateConnectionString(String name,
                                                       String connectionString)
        {
            try
            {
                Configuration configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                ConnectionStringSettingsCollection settings = configFile.ConnectionStrings.ConnectionStrings;

                if (settings[name] == null)
                {
                    settings.Add(new ConnectionStringSettings(name, connectionString));
                }
                else
                {
                    settings[name].ConnectionString = connectionString;
                }

                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch(ConfigurationErrorsException)
            {
                Console.WriteLine("Error writing connection string");
            }
        }

        /// <summary>
        /// Configures the specified application.
        /// </summary>
        /// <param name="app">The application.</param>
        /// <param name="env">The env.</param>
        public void Configure(IApplicationBuilder app,
                              IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseSilkierQuartz();

            app.UseEndpoints(endpoints => { endpoints.MapRazorPages(); });
        }

        /// <summary>
        /// Configures the services.
        /// </summary>
        /// <param name="services">The services.</param>
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<Func<String, IBootstrapper>>(container => type =>
                                                                            {
                                                                                (String name, IBootstrapper instance) bootstrapper =
                                                                                    this.JobBootstrappers.SingleOrDefault(b => b.name == $"{type}Bootstrapper");
                                                                                return bootstrapper.instance;
                                                                            });

            services.AddRazorPages();

            this.RegisterJobBootstrappers();
            this.RegisterJobs(services);

            IScheduler scheduler = StdSchedulerFactory.GetDefaultScheduler().Result;
            
            services.AddSilkierQuartz(s =>
                                                   {
                                                       s.VirtualPathRoot = "/quartz";
                                                       s.UseLocalTime = true;
                                                       s.DefaultDateFormat = "yyyy-MM-dd";
                                                       s.DefaultTimeFormat = "HH:mm:ss";
                                                       s.Scheduler = scheduler;
                                                   },
                                                   a => { a.AccessRequirement = SilkierQuartzAuthenticationOptions.SimpleAccessRequirement.AllowAnonymous; });
        }

        /// <summary>
        /// Registers the job bootstrappers.
        /// </summary>
        private void RegisterJobBootstrappers()
        {
            IEnumerable<Type> jobs = typeof(IBootstrapper).GetTypeInfo().Assembly.DefinedTypes
                                                          .Where(t => typeof(IBootstrapper).GetTypeInfo().IsAssignableFrom(t.AsType()) && t.IsClass &&
                                                                      t.IsAbstract == false).Select(p => p.AsType());

            foreach (Type job in jobs)
            {
                Object instance = Activator.CreateInstance(job);
                this.JobBootstrappers.Add((job.Name, (IBootstrapper)instance));
            }
        }

        /// <summary>
        /// Registers the jobs.
        /// </summary>
        /// <param name="services">The services.</param>
        private void RegisterJobs(IServiceCollection services)
        {
            Type type = typeof(IJob);
            IEnumerable<Type> jobs = AppDomain.CurrentDomain.GetAssemblies().SelectMany(s => s.GetTypes()).Where(p => type.IsAssignableFrom(p) && p.IsInterface == false);

            foreach (Type job in jobs)
            {
                services.AddSingleton(job);
            }
        }

        #endregion
    }
}