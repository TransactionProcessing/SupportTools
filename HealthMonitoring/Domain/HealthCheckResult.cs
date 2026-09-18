namespace HealthMonitoring.Domain;

public sealed class HealthCheckResult
{
    public Guid Id { get; set; }
    public Guid HealthObservationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public HealthStatus Status { get; set; }
    public string? Description { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Exception { get; set; }
    public string? DataJson { get; set; }
}
