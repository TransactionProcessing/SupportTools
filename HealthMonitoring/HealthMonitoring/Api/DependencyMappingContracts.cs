namespace HealthMonitoring.Api;

public sealed record DependencyLinkRequest(string DependencyName, Guid TargetMonitoredServiceId);

public sealed record DependencyLinkResponse(
    Guid Id,
    string DependencyName,
    Guid TargetMonitoredServiceId,
    string TargetServiceId,
    string TargetServiceName);
