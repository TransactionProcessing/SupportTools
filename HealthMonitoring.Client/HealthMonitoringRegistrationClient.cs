using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace HealthMonitoring.Client;

public sealed class HealthMonitoringRegistrationClientOptions
{
    public Uri MonitoringServerUrl { get; set; } = new("http://localhost:9620");
}

public sealed class ServiceRegistrationOptions
{
    public string ServiceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public Uri HealthUrl { get; init; } = new("http://localhost/health");
    public string MonitorType { get; init; } = "HttpHealthEndpoint";
    public string? ConnectionString { get; init; }
    public bool IgnoreCertificateErrors { get; init; }
    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RetentionPeriod { get; init; } = TimeSpan.FromDays(365);
    public string? Group { get; init; }
    public string? Description { get; init; }
    public string? Version { get; init; }
    public string? Host { get; init; }
    public object? StatusPolicy { get; init; }
}

public sealed record DependencyMappingOptions(string DependencyName, string TargetServiceId);

public sealed record ServiceRegistrationResult(string ServiceId, string Action, Guid MonitoredServiceId);

public interface IHealthMonitoringRegistrationClient
{
    Task<ServiceRegistrationResult> RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default);
    Task SetDependencyMappingsAsync(string serviceId, IEnumerable<DependencyMappingOptions> mappings, CancellationToken cancellationToken = default);
    Task<ServiceRegistrationResult> RegisterAndConfigureAsync(ServiceRegistrationOptions options, IEnumerable<DependencyMappingOptions> mappings, CancellationToken cancellationToken = default);
}

public sealed class HealthMonitoringRegistrationClient(HttpClient httpClient) : IHealthMonitoringRegistrationClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServiceRegistrationResult> RegisterAsync(ServiceRegistrationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateServiceOptions(options);

        using var response = await httpClient.PostAsJsonAsync("api/services/register", new ServiceRegistrationPayload(
            options.ServiceId,
            options.Name,
            options.HealthUrl.ToString(),
            ParseMonitorType(options.MonitorType),
            options.ConnectionString,
            options.IgnoreCertificateErrors,
            ToSeconds(options.PollingInterval, nameof(options.PollingInterval)),
            ToSeconds(options.RequestTimeout, nameof(options.RequestTimeout)),
            ToDays(options.RetentionPeriod, nameof(options.RetentionPeriod)),
            options.StatusPolicy,
            options.Group,
            options.Description,
            options.Version,
            options.Host), JsonOptions, cancellationToken);

        await EnsureSuccessAsync(response, "register service", cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ServiceRegistrationResponsePayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The monitoring server returned an empty registration response.");
        return new ServiceRegistrationResult(result.ServiceId, result.Action, result.Service.Id);
    }

    public async Task SetDependencyMappingsAsync(string serviceId, IEnumerable<DependencyMappingOptions> mappings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceId)) throw new ArgumentException("A service ID is required.", nameof(serviceId));
        ArgumentNullException.ThrowIfNull(mappings);

        var existing = await GetDependencyLinksAsync(serviceId, cancellationToken);
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.DependencyName)) throw new ArgumentException("A dependency name is required.", nameof(mappings));
            if (string.IsNullOrWhiteSpace(mapping.TargetServiceId)) throw new ArgumentException("A target service ID is required.", nameof(mappings));

            var target = await GetServiceAsync(mapping.TargetServiceId, cancellationToken);
            var payload = new DependencyMappingPayload(mapping.DependencyName, target.Id);
            var current = existing.FirstOrDefault(link => string.Equals(link.DependencyName.Trim(), mapping.DependencyName.Trim(), StringComparison.OrdinalIgnoreCase));
            using var response = current is null
                ? await httpClient.PostAsJsonAsync($"api/services/{Encode(serviceId)}/dependency-links", payload, JsonOptions, cancellationToken)
                : await httpClient.PutAsJsonAsync($"api/services/{Encode(serviceId)}/dependency-links/{current.Id}", payload, JsonOptions, cancellationToken);
            await EnsureSuccessAsync(response, "save dependency mapping", cancellationToken);
        }
    }

    public async Task<ServiceRegistrationResult> RegisterAndConfigureAsync(ServiceRegistrationOptions options, IEnumerable<DependencyMappingOptions> mappings, CancellationToken cancellationToken = default)
    {
        var result = await RegisterAsync(options, cancellationToken);
        await SetDependencyMappingsAsync(options.ServiceId, mappings, cancellationToken);
        return result;
    }

    private async Task<IReadOnlyList<DependencyLinkResponsePayload>> GetDependencyLinksAsync(string serviceId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/services/{Encode(serviceId)}/dependency-links", cancellationToken);
        await EnsureSuccessAsync(response, "read dependency mappings", cancellationToken);
        return await response.Content.ReadFromJsonAsync<List<DependencyLinkResponsePayload>>(JsonOptions, cancellationToken) ?? [];
    }

    private async Task<ServiceIdentityPayload> GetServiceAsync(string serviceId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"api/services/{Encode(serviceId)}", cancellationToken);
        await EnsureSuccessAsync(response, $"read target service '{serviceId}'", cancellationToken);
        return await response.Content.ReadFromJsonAsync<ServiceIdentityPayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The monitoring server returned an empty service response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException($"Could not {operation}. Server returned {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private static int ParseMonitorType(string monitorType) =>
        string.Equals(monitorType, "KurrentDb", StringComparison.OrdinalIgnoreCase) ? 1 :
        string.Equals(monitorType, "SqlServer", StringComparison.OrdinalIgnoreCase) ? 2 : 0;

    private static int ToSeconds(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value.TotalSeconds > int.MaxValue) throw new ArgumentOutOfRangeException(name);
        return (int)value.TotalSeconds;
    }

    private static int ToDays(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value.TotalDays > int.MaxValue) throw new ArgumentOutOfRangeException(name);
        return (int)value.TotalDays;
    }

    private static void ValidateServiceOptions(ServiceRegistrationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceId)) throw new ArgumentException("A service ID is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Name)) throw new ArgumentException("A service name is required.", nameof(options));
        if (!options.HealthUrl.IsAbsoluteUri) throw new ArgumentException("The health URL must be absolute.", nameof(options));
    }

    private sealed record ServiceRegistrationPayload(string ServiceId, string Name, string HealthUrl, int MonitorType, string? ConnectionString, bool IgnoreCertificateErrors, int PollingIntervalSeconds, int RequestTimeoutSeconds, int RetentionDays, object? StatusPolicy, string? Group, string? Description, string? Version, string? Host);
    private sealed record ServiceRegistrationResponsePayload(string ServiceId, string Action, ServiceIdentityPayload Service);
    private sealed record ServiceIdentityPayload(Guid Id, string ServiceId, string Name);
    private sealed record DependencyMappingPayload(string DependencyName, Guid TargetMonitoredServiceId);
    private sealed record DependencyLinkResponsePayload(Guid Id, string DependencyName, Guid TargetMonitoredServiceId, string TargetServiceId, string TargetServiceName);
}

public static class HealthMonitoringRegistrationClientServiceCollectionExtensions
{
    public static IHttpClientBuilder AddHealthMonitoringRegistrationClient(this IServiceCollection services, Action<HealthMonitoringRegistrationClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new HealthMonitoringRegistrationClientOptions();
        configure(options);
        if (!options.MonitoringServerUrl.IsAbsoluteUri) throw new ArgumentException("The monitoring server URL must be absolute.", nameof(configure));

        return services.AddHttpClient<IHealthMonitoringRegistrationClient, HealthMonitoringRegistrationClient>(client =>
        {
            client.BaseAddress = options.MonitoringServerUrl;
        });
    }
}
