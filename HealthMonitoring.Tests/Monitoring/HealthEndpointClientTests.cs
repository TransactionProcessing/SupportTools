using System.Net;
using System.Net.Http;
using HealthMonitoring.Domain;
using HealthMonitoring.Monitoring;

namespace HealthMonitoring.Tests.Monitoring;

public sealed class HealthEndpointClientTests
{
    [Fact]
    public async Task Parses_standard_aspnet_health_response()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{\"status\":\"Healthy\",\"entries\":{\"database\":{\"status\":\"Healthy\",\"duration\":\"00:00:00.004\",\"data\":{}}}}" );
        var client = new HealthEndpointClient(new HttpClient(handler), new HealthStatusNormalizer());
        var service = new MonitoredService { HealthUrl = new Uri("https://service/health") };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Healthy, result.Normalized.Status);
        Assert.Single(result.Normalized.Checks);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
    }

    [Fact]
    public async Task Timeout_is_recorded_as_unhealthy_observation_candidate()
    {
        var handler = new StubHandler(new TaskCanceledException("timeout"));
        var client = new HealthEndpointClient(new HttpClient(handler), new HealthStatusNormalizer());
        var service = new MonitoredService { HealthUrl = new Uri("https://service/health") };

        var result = await client.CheckAsync(service, CancellationToken.None);

        Assert.Equal(HealthStatus.Unhealthy, result.Normalized.Status);
        Assert.Contains("timeout", result.Normalized.Error, StringComparison.OrdinalIgnoreCase);
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
            if (_exception is not null) return Task.FromException<HttpResponseMessage>(_exception);
            return Task.FromResult(new HttpResponseMessage(_status!.Value) { Content = new StringContent(_body!) });
        }
    }
}
