using HealthMonitoring.Domain;

namespace HealthMonitoring.Api;

public sealed class ServiceRegistrationRequest
{
    public ServiceRegistrationRequest() { }
    public ServiceRegistrationRequest(string serviceId, string name, string healthUrl) { ServiceId = serviceId; Name = name; HealthUrl = healthUrl; }
    public string ServiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HealthUrl { get; set; } = string.Empty;
    public MonitorType MonitorType { get; set; } = MonitorType.HttpHealthEndpoint;
    public string? ConnectionString { get; set; }
    public bool IgnoreCertificateErrors { get; set; }
    public int PollingIntervalSeconds { get; set; } = 60;
    public int RequestTimeoutSeconds { get; set; } = 10;
    public int RetentionDays { get; set; } = 365;
    public StatusPolicy? StatusPolicy { get; set; }
    public string? Group { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Host { get; set; }
}

public sealed record ServiceRegistrationResponse(string ServiceId, string Action, MonitoredService Service);

public sealed record ServiceSummary(
    Guid Id,
    string ServiceId,
    string Name,
    string Environment,
    MonitorType MonitorType,
    string HealthUrl,
    bool IsEnabled,
    HealthStatus Status,
    DateTimeOffset? LastObservedAtUtc,
    TimeSpan? LastResponseDuration,
    string? LastError);
