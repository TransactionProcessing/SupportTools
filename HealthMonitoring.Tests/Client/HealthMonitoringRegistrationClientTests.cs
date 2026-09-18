using System.Net;
using System.Net.Http.Json;
using HealthMonitoring.Client;

namespace HealthMonitoring.Tests.Client;

public sealed class HealthMonitoringRegistrationClientTests
{
    private const string PaymentsApiServiceId = "payments-api";
    private const string SecurityServiceId = "security-service";
    private const string SecurityServiceName = "Security Service";
    private const string MonitoringBaseUrl = "https://monitoring.local";

    [Fact]
    public async Task RegisterAsync_posts_service_configuration_and_returns_acknowledgement()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new
            {
                serviceId = PaymentsApiServiceId,
                action = "created",
                service = new { id = Guid.Parse("11111111-1111-1111-1111-111111111111") }
            })
        });
        var client = CreateClient(handler);

        var result = await client.RegisterAsync(new ServiceRegistrationOptions
        {
            ServiceId = PaymentsApiServiceId,
            Name = "Payments API",
            HealthUrl = new Uri("https://payments-api/health"),
            PollingInterval = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(8),
            RetentionPeriod = TimeSpan.FromDays(90),
            IgnoreCertificateErrors = true
        });

        Assert.Equal("created", result.Action);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.MonitoredServiceId);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal($"{MonitoringBaseUrl}/api/services/register", handler.Request.RequestUri!.ToString());
        var payload = await handler.Request.Content!.ReadFromJsonAsync<ServiceRegistrationPayload>();
        Assert.Equal(PaymentsApiServiceId, payload!.ServiceId);
        Assert.Equal(0, payload.MonitorType);
        Assert.Equal(30, payload.PollingIntervalSeconds);
        Assert.True(payload.IgnoreCertificateErrors);
    }

    [Fact]
    public async Task SetDependencyMappingsAsync_resolves_target_service_and_upserts_mapping()
    {
        var targetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var handler = new QueueHandler(
            JsonResponse(HttpStatusCode.OK, Array.Empty<object>()),
            JsonResponse(HttpStatusCode.OK, new { id = targetId, serviceId = SecurityServiceId, name = SecurityServiceName }),
            JsonResponse(HttpStatusCode.Created, new { }));
        var client = CreateClient(handler);

        await client.SetDependencyMappingsAsync("payments-api", new[]
        {
            new DependencyMappingOptions(SecurityServiceName, SecurityServiceId)
        });

        Assert.Collection(handler.Requests,
            request => Assert.Equal($"{MonitoringBaseUrl}/api/services/{PaymentsApiServiceId}/dependency-links", request.RequestUri!.ToString()),
            request => Assert.Equal($"{MonitoringBaseUrl}/api/services/{SecurityServiceId}", request.RequestUri!.ToString()),
            request => Assert.Equal(HttpMethod.Post, request.Method));
        var payload = await handler.Requests[2].Content!.ReadFromJsonAsync<DependencyMappingPayload>();
        Assert.Equal(SecurityServiceName, payload!.DependencyName);
        Assert.Equal(targetId, payload.TargetMonitoredServiceId);
    }

    [Fact]
    public async Task SetDependencyMappingsAsync_updates_existing_mapping_instead_of_creating_a_duplicate()
    {
        var linkId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var targetId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var handler = new QueueHandler(
            JsonResponse(HttpStatusCode.OK, new[] { new { id = linkId, dependencyName = SecurityServiceName } }),
            JsonResponse(HttpStatusCode.OK, new { id = targetId, serviceId = SecurityServiceId, name = SecurityServiceName }),
            JsonResponse(HttpStatusCode.OK, new { }));
        var client = CreateClient(handler);

        await client.SetDependencyMappingsAsync("payments-api", new[]
        {
            new DependencyMappingOptions(SecurityServiceName, SecurityServiceId)
        });

        Assert.Equal(HttpMethod.Put, handler.Requests[2].Method);
        Assert.EndsWith($"/api/services/{PaymentsApiServiceId}/dependency-links/{linkId}", handler.Requests[2].RequestUri!.ToString());
    }

    private static HealthMonitoringRegistrationClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(MonitoringBaseUrl) });

    private sealed class ServiceRegistrationPayload
    {
        public string ServiceId { get; set; } = string.Empty;
        public int MonitorType { get; set; }
        public int PollingIntervalSeconds { get; set; }
        public bool IgnoreCertificateErrors { get; set; }
    }

    private sealed class DependencyMappingPayload
    {
        public string DependencyName { get; set; } = string.Empty;
        public Guid TargetMonitoredServiceId { get; set; }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Request = request;
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(responses[_index++]);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value) => new(statusCode)
    {
        Content = JsonContent.Create(value)
    };
}
