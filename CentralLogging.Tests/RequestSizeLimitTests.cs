using CentralLogging.Api.Endpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CentralLogging.Tests;

public sealed class RequestSizeLimitTests
{
    private const String LogsRoute = "/api/logs";

    [Fact]
    public void Json_ingestion_uses_the_configured_request_limit()
    {
        DefaultHttpContext context = CreateContext(HttpMethods.Post, LogsRoute);
        TestRequestBodySizeFeature feature = new();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        LogRequestSizeLimit.Apply(context, new LogIngestionOptions { MaximumRequestBytes = 123, MaximumUploadBytes = 456 });

        feature.MaxRequestBodySize.ShouldBe(123);
    }

    [Fact]
    public void File_upload_allows_multipart_framing_above_the_file_limit()
    {
        DefaultHttpContext context = CreateContext(HttpMethods.Post, "/api/logs/upload");
        TestRequestBodySizeFeature feature = new();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        LogRequestSizeLimit.Apply(context, new LogIngestionOptions { MaximumRequestBytes = 123, MaximumUploadBytes = 456 });

        feature.MaxRequestBodySize.ShouldBe(456 + 64 * 1024);
    }

    [Theory]
    [InlineData("GET", LogsRoute)]
    [InlineData("POST", "/api/health")]
    public void Non_ingestion_requests_do_not_change_the_server_limit(String method, String path)
    {
        DefaultHttpContext context = CreateContext(method, path);
        TestRequestBodySizeFeature feature = new() { MaxRequestBodySize = 789 };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        LogRequestSizeLimit.Apply(context, new LogIngestionOptions { MaximumRequestBytes = 123, MaximumUploadBytes = 456 });

        feature.MaxRequestBodySize.ShouldBe(789);
    }

    [Fact]
    public void Request_limit_is_left_unchanged_when_server_feature_is_read_only()
    {
        DefaultHttpContext context = CreateContext(HttpMethods.Post, LogsRoute);
        TestRequestBodySizeFeature feature = new() { IsReadOnly = true, MaxRequestBodySize = 789 };
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        LogRequestSizeLimit.Apply(context, new LogIngestionOptions { MaximumRequestBytes = 123, MaximumUploadBytes = 456 });

        feature.MaxRequestBodySize.ShouldBe(789);
    }

    [Fact]
    public async Task Kestrel_rejects_a_json_request_that_exceeds_the_configured_limit()
    {
        WebApplication app = await StartKestrelAsync();
        try
        {
            using HttpClient client = CreateClient(app);
            using HttpResponseMessage response = await client.PostAsync(LogsRoute, new StringContent(new String('x', 65)));

            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.RequestEntityTooLarge);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Kestrel_accepts_a_json_request_at_or_below_the_configured_limit()
    {
        WebApplication app = await StartKestrelAsync();
        try
        {
            using HttpClient client = CreateClient(app);
            using HttpResponseMessage response = await client.PostAsync(LogsRoute, new StringContent(new String('x', 64)));

            response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void Missing_server_limit_feature_is_ignored()
    {
        DefaultHttpContext context = CreateContext(HttpMethods.Post, LogsRoute);

        Should.NotThrow(() => LogRequestSizeLimit.Apply(context,
            new LogIngestionOptions { MaximumRequestBytes = 123, MaximumUploadBytes = 456 }));
    }

    private static async Task<WebApplication> StartKestrelAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        WebApplication app = builder.Build();
        LogIngestionOptions options = new() { MaximumRequestBytes = 64, MaximumUploadBytes = 64 };
        app.Use(async (context, next) =>
        {
            LogRequestSizeLimit.Apply(context, options);
            await next(context);
        });
        app.MapPost(LogsRoute, async (HttpRequest request) =>
        {
            await request.Body.CopyToAsync(Stream.Null);
            return Results.Ok();
        });
        await app.StartAsync();
        return app;
    }

    private static HttpClient CreateClient(WebApplication app)
    {
        String address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        return new HttpClient { BaseAddress = new Uri(address) };
    }

    private static DefaultHttpContext CreateContext(String method, String path) => new()
    {
        Request = { Method = method, Path = path }
    };

    private sealed class TestRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
    {
        public Boolean IsReadOnly { get; set; }
        public Int64? MaxRequestBodySize { get; set; }
    }
}
