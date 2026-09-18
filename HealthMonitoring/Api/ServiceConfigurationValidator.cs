using HealthMonitoring.Domain;

namespace HealthMonitoring.Api;

public static class ServiceConfigurationValidator
{
    public static IReadOnlyList<string> Validate(ServiceRegistrationRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ServiceId)) errors.Add("ServiceId is required.");
        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Name is required.");
        if (string.IsNullOrWhiteSpace(request.Environment)) errors.Add("Environment is required.");
        if (!Uri.TryCreate(request.HealthUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) errors.Add("HealthUrl must be an absolute HTTP or HTTPS URL.");
        if (request.PollingIntervalSeconds < 5) errors.Add("PollingIntervalSeconds must be at least 5.");
        if (request.RequestTimeoutSeconds < 1 || request.RequestTimeoutSeconds > request.PollingIntervalSeconds) errors.Add("RequestTimeoutSeconds must be between 1 and the polling interval.");
        if (request.RetentionDays < 1) errors.Add("RetentionDays must be positive.");
        return errors;
    }

    public static MonitoredService ToService(ServiceRegistrationRequest request, MonitoredService? existing = null, bool preserveManagedSettings = true) => new()
    {
        Id = existing?.Id ?? Guid.NewGuid(),
        ServiceId = request.ServiceId,
        Name = request.Name,
        Environment = request.Environment,
        Group = request.Group,
        Description = request.Description,
        HealthUrl = new Uri(request.HealthUrl),
        IsEnabled = existing?.IsEnabled ?? true,
        PollingInterval = preserveManagedSettings && existing is not null ? existing.PollingInterval : TimeSpan.FromSeconds(request.PollingIntervalSeconds),
        RequestTimeout = preserveManagedSettings && existing is not null ? existing.RequestTimeout : TimeSpan.FromSeconds(request.RequestTimeoutSeconds),
        RetentionPeriod = preserveManagedSettings && existing is not null ? existing.RetentionPeriod : TimeSpan.FromDays(request.RetentionDays),
        StatusPolicy = preserveManagedSettings && existing is not null ? existing.StatusPolicy : request.StatusPolicy ?? new StatusPolicy(),
        Version = request.Version,
        Host = request.Host,
        LastRegisteredAtUtc = DateTimeOffset.UtcNow,
        CreatedAtUtc = existing?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };
}
