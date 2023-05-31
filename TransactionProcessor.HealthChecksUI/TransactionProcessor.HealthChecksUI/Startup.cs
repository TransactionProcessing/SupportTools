using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TransactionProcessor.HealthChecksUI
{
    using HealthChecks.UI.Core;
    using Microsoft.Extensions.Diagnostics.HealthChecks;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using Newtonsoft.Json;
    using JsonSerializer = System.Text.Json.JsonSerializer;

    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services){
            services.AddTransient<CustomHealthReportDelegatingHandler>();

            services.AddHealthChecksUI(settings =>
                                       {
                                           settings.UseApiEndpointHttpMessageHandler(ApiEndpointHttpHandler);
                                           settings.UseApiEndpointDelegatingHandler<CustomHealthReportDelegatingHandler>();
                                       }).AddInMemoryStorage();
        }

        private HttpClientHandler ApiEndpointHttpHandler(IServiceProvider arg)
        {
            return new HttpClientHandler
                   {
                       ServerCertificateCustomValidationCallback = (message,
                                                                    cert,
                                                                    chain,
                                                                    errors) =>
                                                                   {
                                                                       return true;
                                                                   }
                   };
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecksUI();
            });
        }
    }

    public class CustomHealthReportDelegatingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Add("Accept", new[] { "application/json" });
            HttpResponseMessage response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch(HttpRequestException ex){
                // Just ignore this 
                JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
                                                 {
                                                     Converters =
                                                     {
                                                         new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)
                                                     }
                                                 };
                UIHealthReport report = UIHealthReport.CreateFrom(ex);
                report.Status = UIHealthStatus.Unhealthy;
                String json = JsonSerializer.Serialize(report, _options);
                response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            return response;
        }
    }
}
