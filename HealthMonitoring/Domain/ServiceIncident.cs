namespace HealthMonitoring.Domain;

public sealed class ServiceIncident
{
    public Guid Id { get; set; }
    public Guid MonitoredServiceId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public HealthStatus Severity { get; set; }
    public Guid FirstObservationId { get; set; }
    public Guid LastObservationId { get; set; }
    public bool IsOpen => EndedAtUtc is null;
    public TimeSpan? Duration => (EndedAtUtc ?? DateTimeOffset.UtcNow) - StartedAtUtc;
}
