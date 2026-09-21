using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using EventStore.Client;
using KurrentDB.Client;
using HealthMonitoring.Domain;
using Microsoft.Extensions.Logging;

namespace HealthMonitoring.Monitoring;

public interface IKurrentDbProbe
{
    Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken);
}

public interface IKurrentDbVersionProbe
{
    Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken);
}

public static partial class KurrentDbVersionFormatter
{
    public static string? Format(string? rawVersion)
    {
        if (string.IsNullOrWhiteSpace(rawVersion)) return null;

        var match = VersionRegex().Match(rawVersion.Trim());
        return match.Success ? $"KurrentDB {match.Value}" : null;
    }

    [GeneratedRegex(@"\b\d+(?:\.\d+){1,2}\b")]
    private static partial Regex VersionRegex();
}

public sealed class KurrentDbRegistrationVersionResolver(IKurrentDbVersionProbe probe, ILogger<KurrentDbRegistrationVersionResolver> logger)
{
    public async Task<string?> ResolveAsync(MonitoredService service, string? fallback, CancellationToken cancellationToken)
    {
        try
        {
            return KurrentDbVersionFormatter.Format(await probe.GetVersionAsync(service, cancellationToken)) ?? fallback;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Unable to determine KurrentDB version while registering a service.");
            return fallback;
        }
    }
}

public sealed class KurrentDbMonitorClient(IKurrentDbProbe probe) : IHealthEndpointClient
{
    public async Task<HealthEndpointResult> CheckAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await probe.ProbeAsync(service, cancellationToken);
            stopwatch.Stop();
            var check = new NormalizedCheckResult("KurrentDB", HealthStatus.Healthy, "KurrentDB is reachable.", stopwatch.Elapsed, null, new Dictionary<string, JsonElement>());
            var raw = JsonSerializer.Serialize(new { status = nameof(HealthStatus.Healthy), totalDuration = stopwatch.Elapsed, entries = new { KurrentDB = new { status = nameof(HealthStatus.Healthy), duration = stopwatch.Elapsed, data = new { } } } });
            return new HealthEndpointResult(new NormalizedHealthResult(HealthStatus.Healthy, nameof(HealthStatus.Healthy), [check], RawResponse: raw), null, stopwatch.Elapsed, raw);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failure(service, stopwatch.Elapsed, "KurrentDB health check timed out.");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return Failure(service, stopwatch.Elapsed, exception.Message, exception);
        }
    }

    private static HealthEndpointResult Failure(MonitoredService service, TimeSpan duration, string error, Exception? exception = null)
    {
        var status = string.Equals(service.StatusPolicy.EndpointFailureStatus, nameof(HealthStatus.Degraded), StringComparison.OrdinalIgnoreCase)
            ? HealthStatus.Degraded
            : HealthStatus.Unhealthy;
        var check = new NormalizedCheckResult("KurrentDB", status, error, duration, exception?.ToString(), new Dictionary<string, JsonElement>());
        var raw = JsonSerializer.Serialize(new { status = status.ToString(), totalDuration = duration, entries = new { KurrentDB = new { status = status.ToString(), duration, exception = exception?.ToString(), data = new { } } } });
        return new HealthEndpointResult(new NormalizedHealthResult(status, status.ToString(), [check], error, raw), null, duration, raw);
    }
}

internal sealed class KurrentDbProbe : IKurrentDbProbe
{
    public async Task ProbeAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service.ConnectionString)) throw new InvalidOperationException("KurrentDB connection string is not configured.");

        var settings = KurrentDBClientSettings.Create(service.ConnectionString);
        using var client = new KurrentDBClient(settings);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(service.RequestTimeout);

        await foreach (var _ in client.ReadAllAsync(
            Direction.Forwards,
            Position.Start,
            maxCount: 1,
            resolveLinkTos: false,
            deadline: service.RequestTimeout,
            userCredentials: null,
            cancellationToken: timeoutSource.Token).WithCancellation(timeoutSource.Token))
        {
            break;
        }
    }
}

public sealed class KurrentDbVersionProbe(IHttpClientFactory httpClientFactory) : IKurrentDbVersionProbe
{
    public async Task<string?> GetVersionAsync(MonitoredService service, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(service.ConnectionString))
            throw new InvalidOperationException("KurrentDB connection string is not configured.");

        var connectionUri = new Uri(service.ConnectionString, UriKind.Absolute);
        if (connectionUri.Scheme.EndsWith("+discover", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("KurrentDB discovery connection strings cannot be used to query the HTTP info endpoint.");

        var useTls = !string.Equals(GetQueryValue(connectionUri, "tls"), "false", StringComparison.OrdinalIgnoreCase);
        var infoUri = new UriBuilder(connectionUri)
        {
            Scheme = useTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
            Port = connectionUri.IsDefaultPort ? (useTls ? 443 : 80) : connectionUri.Port,
            Path = "/info",
            Query = string.Empty,
            UserName = string.Empty,
            Password = string.Empty
        }.Uri;

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(service.RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, infoUri);
        if (!string.IsNullOrEmpty(connectionUri.UserInfo))
        {
            var separator = connectionUri.UserInfo.IndexOf(':');
            var userName = separator < 0 ? connectionUri.UserInfo : connectionUri.UserInfo[..separator];
            var password = separator < 0 ? string.Empty : connectionUri.UserInfo[(separator + 1)..];
            var credentials = $"{Uri.UnescapeDataString(userName)}:{Uri.UnescapeDataString(password)}";
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(credentials)));
        }

        using var response = await httpClientFactory.CreateClient().SendAsync(request, timeoutSource.Token);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeoutSource.Token), cancellationToken: timeoutSource.Token);

        foreach (var propertyName in new[] { "esVersion", "version", "serverVersion" })
        {
            if (document.RootElement.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                return property.GetString();
        }

        return null;
    }

    private static string? GetQueryValue(Uri uri, string name)
    {
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var values = part.Split('=', 2);
            if (values.Length == 2 && string.Equals(Uri.UnescapeDataString(values[0]), name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(values[1]);
        }

        return null;
    }
}
