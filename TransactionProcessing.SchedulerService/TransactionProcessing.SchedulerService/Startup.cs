namespace TransactionProcessing.SchedulerService
{
    using System;
    using System.Configuration;
    using Jobs;
    using Jobs.GenerateTransactions;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Quartz.Impl;
    using SilkierQuartz;

    public class Startup
    {
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

        public Startup(IWebHostEnvironment webHostEnvironment)
        {
            //this.Configuration = configuration;
            IConfigurationBuilder builder = new ConfigurationBuilder().SetBasePath(webHostEnvironment.ContentRootPath)
                                                                      .AddJsonFile("/home/txnproc/config/appsettings.json", true, true)
                                                                      .AddJsonFile($"/home/txnproc/config/appsettings.{webHostEnvironment.EnvironmentName}.json", optional: true)
                                                                      .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                                                                      .AddJsonFile($"appsettings.{webHostEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                                                                      .AddEnvironmentVariables();

            Startup.Configuration = builder.Build();
            Startup.WebHostEnvironment = webHostEnvironment;

            var connectionString = Configuration.GetConnectionString("SchedulerReadModel");
            AddOrUpdateConnectionString("SchedulerReadModel", connectionString);
        }

        public static void AddOrUpdateConnectionString(string name, string connectionString)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.ConnectionStrings.ConnectionStrings;
                
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
            catch (ConfigurationErrorsException)
            {
                Console.WriteLine("Error writing connection string");
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
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

            app.UseEndpoints(endpoints =>
                             {
                                 endpoints.MapRazorPages();
                             });
        }



        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<Func<String, IBootstrapper>>(container => (type) =>
                                                                            {
                                                                                switch (type)
                                                                                {
                                                                                    case "GenerateTransactionsJob":
                                                                                        return new GenerateTransactionsBootstrapper();
                                                                                    case "GenerateFileUploadsJob":
                                                                                        return new GenerateFileUploadsBootstrapper();
                                                                                    case "ProcessSettlementJob":
                                                                                        return new ProcessSettlementBootstrapper();
                                                                                }

                                                                                return null;
                                                                            });

            services.AddRazorPages();
            
            services.AddSingleton<GenerateTransactionsJob>();
            services.AddSingleton<GenerateFileUploadsJob>();
            services.AddSingleton<ProcessSettlementJob>();

            services.AddSilkierQuartz(s =>
                                      {
                                          s.VirtualPathRoot = "/quartz";
                                          s.UseLocalTime = true;
                                          s.DefaultDateFormat = "yyyy-MM-dd";
                                          s.DefaultTimeFormat = "HH:mm:ss";
                                          s.Scheduler = StdSchedulerFactory.GetDefaultScheduler().Result;
                                          
                                      },
                                      a =>
                                      {
                                          a.AccessRequirement = SilkierQuartzAuthenticationOptions.SimpleAccessRequirement.AllowAnonymous;
                                      });

            
        }
    }
}