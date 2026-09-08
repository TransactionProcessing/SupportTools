using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TransactionProcessor.SystemSetupTool
{
    public sealed record SubscriptionConfigurationRequest
    {
        public string? SubscriptionId { get; init; }
        public string? SecondaryIndexName { get; init; }
        public string? EndpointUrl { get; init; }
        public string? Tag { get; init; }
        public int TimeoutSeconds { get; init; } = 30;
        public int RetryMaxAttempts { get; init; } = 3;
        public int RetryDelaySeconds { get; init; } = 1;
        public int CheckpointBatchSize { get; init; } = 100;
        public bool ContinueOnParked { get; init; }
        public AuthenticationConfiguration? Authentication { get; init; }
        public bool SoftDeleteParked { get; init; } = true;
    }
    
    public interface ICatchupServiceClient
    {
        Task<CatchupApiResponse<IReadOnlyCollection<EndpointDefinition>>> GetEndpointsAsync(CancellationToken cancellationToken = default);
        Task<CatchupApiResponse<EndpointDefinition>> CreateEndpointAsync(EndpointDefinition endpoint, CancellationToken cancellationToken = default);
        Task<CatchupApiResponse<IReadOnlyCollection<SubscriptionDefinition>>> GetSubscriptionConfigurationsAsync(CancellationToken cancellationToken = default);
        Task<CatchupApiResponse<SubscriptionDefinition>> CreateSubscriptionConfigurationAsync(SubscriptionConfigurationRequest request, CancellationToken cancellationToken = default);
        Task<CatchupApiResponse> StartSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default);
        Task<CatchupApiResponse<string>> CreateIndexAsync(string indexName, string payload, CancellationToken cancellationToken = default);
        Task<CatchupApiResponse<string>> GetIndexAsync(String indexName,CancellationToken cancellationToken = default);
    }

    public record CatchupApiResponse(HttpStatusCode StatusCode, string? Content, string? ContentType)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and < 300;
    }

    public sealed record CatchupApiResponse<T>(HttpStatusCode StatusCode, T? Value, string? Content, string? ContentType)
        : CatchupApiResponse(StatusCode, Content, ContentType);

    public sealed class CatchupServiceClient(Func<String, String> urlResolver, HttpClient httpClient) : ICatchupServiceClient
    {
        private readonly Func<String, String> UrlResolver = urlResolver;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        public Task<CatchupApiResponse<string>> GetIndexAsync(String indexName, CancellationToken cancellationToken = default) =>
            SendRawAsync(HttpMethod.Get, $"indexes/{Escape(indexName)}", null, cancellationToken);

        public Task<CatchupApiResponse<IReadOnlyCollection<EndpointDefinition>>> GetEndpointsAsync(CancellationToken cancellationToken = default) =>
            SendJsonAsync<IReadOnlyCollection<EndpointDefinition>>(HttpMethod.Get, "endpoints", null, cancellationToken);

        public Task<CatchupApiResponse<EndpointDefinition>> CreateEndpointAsync(EndpointDefinition endpoint, CancellationToken cancellationToken = default) =>
            SendJsonAsync<EndpointDefinition>(HttpMethod.Post, "endpoints", endpoint, cancellationToken);
        
        public Task<CatchupApiResponse<IReadOnlyCollection<SubscriptionDefinition>>> GetSubscriptionConfigurationsAsync(CancellationToken cancellationToken = default) =>
            SendJsonAsync<IReadOnlyCollection<SubscriptionDefinition>>(HttpMethod.Get, "subscriptions/config", null, cancellationToken);
        
        public Task<CatchupApiResponse<SubscriptionDefinition>> CreateSubscriptionConfigurationAsync(SubscriptionConfigurationRequest request, CancellationToken cancellationToken = default) =>
            SendJsonAsync<SubscriptionDefinition>(HttpMethod.Post, "subscriptions/config", request, cancellationToken);

        public Task<CatchupApiResponse> StartSubscriptionAsync(string subscriptionId, CancellationToken cancellationToken = default) =>
            SendAsync(HttpMethod.Post, $"subscriptions/config/{Escape(subscriptionId)}/start", null, cancellationToken);
        
        public Task<CatchupApiResponse<string>> CreateIndexAsync(string indexName, string payload, CancellationToken cancellationToken = default) =>
            SendRawAsync(HttpMethod.Post, $"indexes/{Escape(indexName)}", payload, cancellationToken);
        
        private async Task<CatchupApiResponse<T>> SendJsonAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            var response = await SendAsync(method, path, body, cancellationToken);
            T? value = default;
            if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(response.Content))
                value = JsonSerializer.Deserialize<T>(response.Content, JsonOptions);

            return new(response.StatusCode, value, response.Content, response.ContentType);
        }

        private async Task<CatchupApiResponse<string>> SendRawAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
        {
            var response = await SendAsync(method, path, body is null ? null : new StringContent(body, Encoding.UTF8, "application/json"), cancellationToken);
            return new(response.StatusCode, response.Content, response.Content, response.ContentType);
        }

        private async Task<CatchupApiResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
        {
            using var content = body is null ? null : JsonContent.Create(body, options: JsonOptions);
            return await SendAsync(method, path, content, cancellationToken);
        }

        private async Task<CatchupApiResponse> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken) {
            var baseUrl = this.UrlResolver("CatchupService");

            string fullUrl = $"{baseUrl}/{path}";


            using var request = new HttpRequestMessage(method, fullUrl) { Content = content };
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return new(response.StatusCode, string.IsNullOrWhiteSpace(responseContent) ? null : responseContent, response.Content.Headers.ContentType?.ToString());
        }

        private static string Escape(string value) => Uri.EscapeDataString(value);

        private static void AddQuery(ICollection<string> query, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                query.Add($"{Escape(name)}={Escape(value)}");
        }

        private static string AddQueryString(string path, IReadOnlyCollection<string> query) =>
            query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
    }

    public sealed record SubscriptionDefinition(
        string SubscriptionId,
        string SecondaryIndexName,
        string EndpointUrl,
        string Tag,
        TimeoutSettings Timeout,
        RetrySettings Retry,
        CheckpointSettings Checkpoint,
        bool ContinueOnParked = false,
        AuthenticationConfiguration? Authentication = null)
    {
        public int? EndpointId { get; init; }
        public bool Enabled { get; init; } = true;
        public SubscriptionOperationalState OperationalState { get; init; } = SubscriptionOperationalState.Healthy;
        public string? OperationalReason { get; init; }
        public bool SoftDeleteParked { get; init; } = true;

        public Uri Endpoint => new(EndpointUrl, UriKind.Absolute);
    }

    public sealed record RetrySettings(int MaxAttempts, TimeSpan Delay)
    {
        public static RetrySettings Default { get; } = new(3, TimeSpan.FromSeconds(1));
    }

    public sealed record CheckpointSettings(int BatchSize)
    {
        public static CheckpointSettings Default { get; } = new(100);
    }

    public sealed record TimeoutSettings(TimeSpan RequestTimeout)
    {
        public static TimeoutSettings Default { get; } = new(TimeSpan.FromSeconds(30));
    }

    public sealed record AuthenticationConfiguration(string? Scheme, IReadOnlyDictionary<string, string> Parameters)
    {
        public static AuthenticationConfiguration Empty { get; } = new(null, new Dictionary<string, string>());
    }


    public enum SubscriptionOperationalState
    {
        Healthy = 0,
        Stopped = 1,
        Faulted = 2
    }

    public sealed record EndpointDefinition(
        string Name,
        string Url,
        AuthenticationConfiguration? Authentication = null)
    {
        public Uri Uri => new(Url, UriKind.Absolute);
    }
}
