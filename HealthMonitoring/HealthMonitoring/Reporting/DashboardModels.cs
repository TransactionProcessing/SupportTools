using HealthMonitoring.Domain;

namespace HealthMonitoring.Reporting;

public sealed record DashboardOverview(IReadOnlyList<ServiceDashboardRow> Services, int Healthy, int Degraded, int Unhealthy, int OpenIncidents);
public sealed record ServiceDashboardRow(string ServiceId, string Name, string Environment, string? Version, HealthStatus Status, TimeSpan? ResponseDuration, double UptimePercent, DateTimeOffset? LastChecked, string? LastError);
public sealed record ResolvedDependencyLink(string DependencyName, string TargetServiceId, string TargetServiceName);
public sealed record ServiceDetailModel(MonitoredService Service, ServiceDashboardRow Summary, int ObservationCount, IReadOnlyList<HealthObservation> Observations, IReadOnlyList<ServiceIncident> Incidents, IReadOnlyList<HealthCheckResult> Checks, IReadOnlyList<ResolvedDependencyLink> DependencyLinks);
