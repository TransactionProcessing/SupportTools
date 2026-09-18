using System.Net;

namespace HealthMonitoring.Domain;

public sealed class HealthStatusNormalizer : IHealthStatusNormalizer
{
    public NormalizedHealthResult Normalize(
        HealthResponseEnvelope response,
        HttpStatusCode? httpStatusCode,
        TimeSpan duration,
        StatusPolicy? policy = null)
    {
        policy ??= new StatusPolicy();
        var checks = response.Entries
            .Select(entry => new NormalizedCheckResult(
                entry.Key,
                ParseStatus(entry.Value.Status),
                entry.Value.Description,
                entry.Value.Duration,
                entry.Value.Exception,
                entry.Value.Data))
            .ToArray();

        var criticalChecks = policy.CriticalChecks.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasUnhealthy = checks.Any(check => check.Status == HealthStatus.Unhealthy);
        var hasCriticalFailure = checks.Any(check => criticalChecks.Contains(check.Name) && check.Status != HealthStatus.Healthy);
        var hasDegraded = checks.Any(check => check.Status == HealthStatus.Degraded);
        var status = hasCriticalFailure || (policy.UnhealthyDependencyBehavior == UnhealthyDependencyBehavior.Unhealthy && hasUnhealthy)
            ? HealthStatus.Unhealthy
            : hasUnhealthy || hasDegraded
                ? HealthStatus.Degraded
                : HealthStatus.Healthy;

        return new NormalizedHealthResult(status, response.Status, checks);
    }

    public NormalizedHealthResult NormalizeInvalid(
        HttpStatusCode? httpStatusCode,
        string rawResponse,
        TimeSpan duration,
        StatusPolicy? policy = null)
    {
        var status = string.Equals(policy?.EndpointFailureStatus, nameof(HealthStatus.Degraded), StringComparison.OrdinalIgnoreCase)
            ? HealthStatus.Degraded
            : HealthStatus.Unhealthy;

        return new NormalizedHealthResult(
            status,
            httpStatusCode?.ToString() ?? "NoResponse",
            [],
            $"Invalid health response: {rawResponse}",
            rawResponse);
    }

    private static HealthStatus ParseStatus(string status)
    {
        if (string.Equals(status, nameof(HealthStatus.Healthy), StringComparison.OrdinalIgnoreCase))
        {
            return HealthStatus.Healthy;
        }

        if (string.Equals(status, nameof(HealthStatus.Degraded), StringComparison.OrdinalIgnoreCase))
        {
            return HealthStatus.Degraded;
        }

        return HealthStatus.Unhealthy;
    }
}
