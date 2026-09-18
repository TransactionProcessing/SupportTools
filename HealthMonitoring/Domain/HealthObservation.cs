namespace HealthMonitoring.Domain;

public sealed class HealthObservation
{
    public Guid Id { get; set; }
    public Guid MonitoredServiceId { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public HealthStatus Status { get; set; }
    public string AggregateStatus { get; set; } = string.Empty;
    public int? HttpStatusCode { get; set; }
    public TimeSpan ResponseDuration { get; set; }
    public string? RawResponseJson { get; set; }
    public string? Error { get; set; }
    public List<HealthCheckResult> Checks { get; set; } = [];
}
