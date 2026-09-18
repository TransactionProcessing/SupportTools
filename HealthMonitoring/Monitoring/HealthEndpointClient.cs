using System.Diagnostics;
using System.Net;
using System.Text.Json;
using HealthMonitoring.Domain;

namespace HealthMonitoring.Monitoring;

public sealed record HealthEndpointResult(
    NormalizedHealthResult Normalized,
    HttpStatusCode? HttpStatusCode,
    TimeSpan Duration,
    string? RawResponse);

public interface IHealthEndpointClient
{
    Task<HealthEndpointResult> CheckAsync(MonitoredService service, CancellationToken cancellationToken);
}

public sealed class HealthEndpointClient(IHttpClientFactory httpClientFactory, IHealthStatusNormalizer normalizer) : IHealthEndpointClient
{
    public async Task<HealthEndpointResult> CheckAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(service.RequestTimeout);
            using var customClient = service.IgnoreCertificateErrors && service.HealthUrl.Scheme == Uri.UriSchemeHttps
                ? new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                })
                : null;
            var httpClient = customClient ?? httpClientFactory.CreateClient();
            using var response = await httpClient.GetAsync(service.HealthUrl, timeout.Token);
            var raw = await response.Content.ReadAsStringAsync(timeout.Token);
            stopwatch.Stop();
            try
            {
                var parsed = Parse(raw);
                var normalized = normalizer.Normalize(parsed, response.StatusCode, stopwatch.Elapsed, service.StatusPolicy) with { RawResponse = raw };
                if (!response.IsSuccessStatusCode)
                {
                    normalized = normalized with { Status = HealthStatus.Unhealthy, Error = $"Health endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}." };
                }

                return new HealthEndpointResult(normalized, response.StatusCode, stopwatch.Elapsed, raw);
            }
            catch (JsonException)
            {
                var normalized = normalizer.NormalizeInvalid(response.StatusCode, raw, stopwatch.Elapsed, service.StatusPolicy);
                return new HealthEndpointResult(normalized, response.StatusCode, stopwatch.Elapsed, raw);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            var normalized = normalizer.NormalizeInvalid(null, "Request timeout.", stopwatch.Elapsed, service.StatusPolicy);
            return new HealthEndpointResult(normalized, null, stopwatch.Elapsed, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            var normalized = normalizer.NormalizeInvalid(null, exception.Message, stopwatch.Elapsed, service.StatusPolicy);
            return new HealthEndpointResult(normalized, null, stopwatch.Elapsed, null);
        }
    }

    private static HealthResponseEnvelope Parse(string raw)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var status = root.GetProperty("status").GetString() ?? nameof(HealthStatus.Unhealthy);
        var entries = new Dictionary<string, HealthResponseEntry>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("entries", out var entriesElement))
        {
            foreach (var property in entriesElement.EnumerateObject())
            {
                var entry = property.Value;
                var entryStatus = entry.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? nameof(HealthStatus.Unhealthy) : nameof(HealthStatus.Unhealthy);
                var description = entry.TryGetProperty("description", out var descriptionElement) ? descriptionElement.GetString() : null;
                var duration = entry.TryGetProperty("duration", out var durationElement) && TimeSpan.TryParse(durationElement.GetString(), out var parsedDuration) ? parsedDuration : TimeSpan.Zero;
                var exception = entry.TryGetProperty("exception", out var exceptionElement) ? exceptionElement.GetString() : null;
                var data = entry.TryGetProperty("data", out var dataElement)
                    ? dataElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, JsonElement>();
                entries[property.Name] = new HealthResponseEntry(entryStatus, description, duration, exception, data);
            }
        }

        return new HealthResponseEnvelope(status, entries);
    }
}
