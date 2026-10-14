namespace HealthMonitoring.Domain;

public sealed class ServerStatus
{
    public Guid Id { get; set; }
    public string ServerId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string OverallStatus { get; set; } = string.Empty;
    public string ChecksJson { get; set; } = "[]";
    public DateTimeOffset ReceivedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
