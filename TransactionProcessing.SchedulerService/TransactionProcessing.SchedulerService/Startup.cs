namespace TransactionProcessing.SchedulerService
{
    using System;
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
        public IConfiguration Configuration { get; }
        public Startup(IConfiguration configuration)
        {
            this.Configuration = configuration;
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