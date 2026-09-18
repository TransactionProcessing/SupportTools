namespace HealthMonitoring.Domain;

public enum UnhealthyDependencyBehavior
{
    Degraded,
    Unhealthy
}

public sealed class StatusPolicy
{
    public UnhealthyDependencyBehavior UnhealthyDependencyBehavior { get; init; } = UnhealthyDependencyBehavior.Degraded;
    public IReadOnlyCollection<string> CriticalChecks { get; init; } = [];
    public string EndpointFailureStatus { get; init; } = nameof(HealthStatus.Unhealthy);
}
