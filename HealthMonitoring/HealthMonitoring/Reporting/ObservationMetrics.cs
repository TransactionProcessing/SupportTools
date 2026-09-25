namespace HealthMonitoring.Reporting;

public sealed record ObservationMetrics(int TotalCount, int HealthyCount)
{
    public double UptimePercent => TotalCount == 0 ? 0 : HealthyCount * 100d / TotalCount;
}
