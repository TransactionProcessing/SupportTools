using System.Text.Json.Serialization;

namespace HealthMonitoring.Domain;

public sealed class MonitoredService
{
    public Guid Id { get; set; }
    public string ServiceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public MonitorType MonitorType { get; set; } = MonitorType.HttpHealthEndpoint;
    public string? Group { get; set; }
    public string? Description { get; set; }
    public Uri HealthUrl { get; set; } = new("https://localhost");
    [JsonIgnore]
    public string? ConnectionString { get; set; }
    public bool IgnoreCertificateErrors { get; set; }
    public bool IsEnabled { get; set; } = true;
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(365);
    public StatusPolicy StatusPolicy { get; set; } = new();
    public string StatusPolicyJson { get; set; } = "{}";
    public string? Version { get; set; }
    public string? Host { get; set; }
    public DateTimeOffset? LastRegisteredAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAtUtc { get; set; }
}
