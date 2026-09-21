using System.Net;
using System.Net.Http;
using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;
using Microsoft.Extensions.Logging.Abstractions;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class KurrentDbVersionTests
{
    [Theory]
    [InlineData("25.0.0.1673-build.1", "KurrentDB 25.0.0")]
    [InlineData("25.1", "KurrentDB 25.1")]
    public void Formats_kurrentdb_versions(string rawVersion, string expected)
    {
        Assert.Equal(expected, KurrentDbVersionFormatter.Format(rawVersion));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    public void Returns_null_for_an_unusable_kurrentdb_version(string? rawVersion)
    {
        Assert.Null(KurrentDbVersionFormatter.Format(rawVersion));
    }

    [Fact]
    public async Task Reads_and_formats_the_info_endpoint_version()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"esVersion\":\"25.0.0.1673-build.1\"}");
        var probe = new KurrentDbVersionProbe(new StubHttpClientFactory(handler));
        var service = new MonitoredService
        {
            ConnectionString = "esdb://admin:changeit@localhost:2113?tls=false",
            RequestTimeout = TimeSpan.FromSeconds(5)
        };

        var version = await probe.GetVersionAsync(service, CancellationToken.None);

        Assert.Equal("25.0.0.1673-build.1", version);
        Assert.Equal("http://localhost:2113/info", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Basic YWRtaW46Y2hhbmdlaXQ=", handler.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Registration_version_resolution_preserves_fallback_when_info_is_unavailable()
    {
        var service = new MonitoredService { MonitorType = MonitorType.KurrentDb };
        var probe = new FailingVersionProbe();
        var resolver = new KurrentDbRegistrationVersionResolver(probe, NullLogger<KurrentDbRegistrationVersionResolver>.Instance);

        var version = await resolver.ResolveAsync(service, "KurrentDB 24.0.0", CancellationToken.None);

        Assert.Equal("KurrentDB 24.0.0", version);
    }

    [Fact]
    public async Task Registration_version_resolution_returns_a_human_readable_version()
    {
        var service = new MonitoredService { MonitorType = MonitorType.KurrentDb };
        var resolver = new KurrentDbRegistrationVersionResolver(new VersionProbe("25.0.0.1673-build.1"), NullLogger<KurrentDbRegistrationVersionResolver>.Instance);

        var version = await resolver.ResolveAsync(service, "reported-by-caller", CancellationToken.None);

        Assert.Equal("KurrentDB 25.0.0", version);
    }

    private sealed class VersionProbe(string version) : IKurrentDbVersionProbe
    {
        public Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken) => Task.FromResult<string?>(version);
    }

    private sealed class FailingVersionProbe : IKurrentDbVersionProbe
    {
        public Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken) =>
            Task.FromException<string?>(new HttpRequestException("info endpoint unavailable"));
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            _ = name;
            return new(handler, disposeHandler: false);
        }
    }
}
