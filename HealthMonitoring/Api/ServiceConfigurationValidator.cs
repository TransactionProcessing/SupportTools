using HealthMonitoring.Domain;

namespace HealthMonitoring.Api;

public static class ServiceConfigurationValidator
{
    private const string DefaultHealthUrl = "http://localhost/health";

    public static IReadOnlyList<string> Validate(ServiceRegistrationRequest request) =>
        [.. ValidateIdentity(request), .. ValidateMonitorSettings(request)];

    public static MonitoredService ToService(ServiceRegistrationRequest request, string dashboardEnvironment, MonitoredService? existing = null, bool preserveManagedSettings = true)
    {
        var now = DateTimeOffset.UtcNow;
        return new MonitoredService
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            ServiceId = request.ServiceId,
            Name = request.Name,
            Environment = dashboardEnvironment,
            Group = request.Group,
            Description = request.Description,
            MonitorType = request.MonitorType,
            HealthUrl = ParseHealthUrl(request.HealthUrl),
            ConnectionString = request.ConnectionString,
            IgnoreCertificateErrors = request.IgnoreCertificateErrors,
            IsEnabled = existing?.IsEnabled ?? true,
            PollingInterval = GetPollingInterval(request, existing, preserveManagedSettings),
            RequestTimeout = GetRequestTimeout(request, existing, preserveManagedSettings),
            RetentionPeriod = GetRetentionPeriod(request, existing, preserveManagedSettings),
            StatusPolicy = GetStatusPolicy(request, existing, preserveManagedSettings),
            Version = request.Version,
            Host = request.Host,
            LastRegisteredAtUtc = now,
            CreatedAtUtc = existing?.CreatedAtUtc ?? now,
            UpdatedAtUtc = now
        };
    }

    private static IEnumerable<string> ValidateIdentity(ServiceRegistrationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ServiceId)) yield return "ServiceId is required.";
        if (string.IsNullOrWhiteSpace(request.Name)) yield return "Name is required.";
        if (!Enum.IsDefined(request.MonitorType)) yield return "MonitorType is invalid.";
    }

    private static IEnumerable<string> ValidateMonitorSettings(ServiceRegistrationRequest request)
    {
        if (request.MonitorType == MonitorType.HttpHealthEndpoint && !IsHttpUrl(request.HealthUrl))
            yield return "HealthUrl must be an absolute HTTP or HTTPS URL.";
        if (request.MonitorType is MonitorType.KurrentDb or MonitorType.SqlServer && string.IsNullOrWhiteSpace(request.ConnectionString))
            yield return "ConnectionString is required for database monitors.";
        if (request.PollingIntervalSeconds < 5) yield return "PollingIntervalSeconds must be at least 5.";
        if (request.RequestTimeoutSeconds < 1 || request.RequestTimeoutSeconds > request.PollingIntervalSeconds)
            yield return "RequestTimeoutSeconds must be between 1 and the polling interval.";
        if (request.RetentionDays < 1) yield return "RetentionDays must be positive.";
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static Uri ParseHealthUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : new Uri(DefaultHealthUrl);

    private static TimeSpan GetPollingInterval(ServiceRegistrationRequest request, MonitoredService? existing, bool preserveManagedSettings) =>
        preserveManagedSettings && existing is not null ? existing.PollingInterval : TimeSpan.FromSeconds(request.PollingIntervalSeconds);

    private static TimeSpan GetRequestTimeout(ServiceRegistrationRequest request, MonitoredService? existing, bool preserveManagedSettings) =>
        preserveManagedSettings && existing is not null ? existing.RequestTimeout : TimeSpan.FromSeconds(request.RequestTimeoutSeconds);

    private static TimeSpan GetRetentionPeriod(ServiceRegistrationRequest request, MonitoredService? existing, bool preserveManagedSettings) =>
        preserveManagedSettings && existing is not null ? existing.RetentionPeriod : TimeSpan.FromDays(request.RetentionDays);

    private static StatusPolicy GetStatusPolicy(ServiceRegistrationRequest request, MonitoredService? existing, bool preserveManagedSettings) =>
        preserveManagedSettings && existing is not null ? existing.StatusPolicy : request.StatusPolicy ?? new StatusPolicy();
}
