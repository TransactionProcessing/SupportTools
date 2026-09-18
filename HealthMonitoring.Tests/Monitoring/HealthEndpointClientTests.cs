using System.Net;
using System.Net.Http;
using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;
using Microsoft.Extensions.Http;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class HealthEndpointClientTests
{
    private const string HealthEndpointUrl = "https://service/health";

    [Fact]
    public async Task Parses_standard_aspnet_health_response()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"status\":\"Healthy\",\"entries\":{\"database\":{\"status\":\"Healthy\",\"duration\":\"00:00:00.004\",\"data\":{}}}}" );
        var client = new HealthEndpointClient(new StubHttpClientFactory(handler), new HealthStatusNormalizer());
        var service = new MonitoredService { HealthUrl = new Uri(HealthEndpointUrl) };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Normalized.Status);
        Assert.Single(result.Normalized.Checks);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
    }

    [Fact]
    public async Task Timeout_is_recorded_as_unhealthy_observation_candidate()
    {
        var handler = new StubHandler(new TaskCanceledException("timeout"));
        var client = new HealthEndpointClient(new StubHttpClientFactory(handler), new HealthStatusNormalizer());
        var service = new MonitoredService { HealthUrl = new Uri(HealthEndpointUrl) };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Normalized.Status);
        Assert.Contains("timeout", result.Normalized.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_success_response_preserves_degraded_policy_for_non_critical_dependency()
    {
        var body = "{\"status\":\"Unhealthy\",\"entries\":{\"cache\":{\"status\":\"Unhealthy\",\"duration\":\"00:00:00.004\",\"data\":{}}}}";
        var handler = new StubHandler(HttpStatusCode.ServiceUnavailable, body);
        var client = new HealthEndpointClient(new StubHttpClientFactory(handler), new HealthStatusNormalizer());
        var service = new MonitoredService { HealthUrl = new Uri(HealthEndpointUrl) };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Degraded, result.Normalized.Status);
        Assert.Contains("503", result.Normalized.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task Invalid_health_payload_is_recorded_as_unhealthy_observation_candidate(string body)
    {
        var handler = new StubHandler(HttpStatusCode.OK, body);
        var client = new HealthEndpointClient(new StubHttpClientFactory(handler), new HealthStatusNormalizer());
        var service = new MonitoredService { HealthUrl = new Uri(HealthEndpointUrl) };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Normalized.Status);
        Assert.Contains("Invalid health response", result.Normalized.Error, StringComparison.Ordinal);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode? _status;
        private readonly string? _body;
        private readonly Exception? _exception;

        public StubHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
        public StubHandler(Exception exception) { _exception = exception; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            if (_exception is not null) return Task.FromException<HttpResponseMessage>(_exception);
            return Task.FromResult(new HttpResponseMessage(_status!.Value) { Content = new StringContent(_body!) });
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
