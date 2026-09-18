namespace HealthMonitoring.Domain;

public sealed class ServiceStatusSnapshot
{
    public Guid MonitoredServiceId { get; set; }
    public HealthStatus Status { get; set; } = HealthStatus.Unknown;
    public DateTimeOffset? LastObservedAtUtc { get; set; }
    public TimeSpan? LastResponseDuration { get; set; }
    public Guid? CurrentIncidentId { get; set; }
    public string? LastError { get; set; }
    public Guid? LastObservationId { get; set; }
}
