using HealthMonitoring.Domain;

namespace HealthMonitoring.Reporting;

public sealed record DashboardOverview(IReadOnlyList<ServiceDashboardRow> Services, int Healthy, int Degraded, int Unhealthy, int OpenIncidents);
public sealed record ServiceDashboardRow(string ServiceId, string Name, string Environment, HealthStatus Status, TimeSpan? ResponseDuration, double UptimePercent, DateTimeOffset? LastChecked, string? LastError);
public sealed record ServiceDetailModel(MonitoredService Service, ServiceDashboardRow Summary, IReadOnlyList<HealthObservation> Observations, IReadOnlyList<ServiceIncident> Incidents, IReadOnlyList<HealthCheckResult> Checks);
