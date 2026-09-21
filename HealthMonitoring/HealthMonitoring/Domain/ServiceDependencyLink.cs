namespace HealthMonitoring.Domain;

public sealed class ServiceDependencyLink
{
    public Guid Id { get; set; }
    public Guid MonitoredServiceId { get; set; }
    public string DependencyName { get; set; } = string.Empty;
    public Guid TargetMonitoredServiceId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
