using System.Net;
using System.Text.Json;
using HealthMonitoring.Domain;

namespace HealthMonitoring.Tests.Domain;

public sealed class HealthStatusNormalizerTests
{
    private const string UnhealthyStatus = "Unhealthy";

    [Fact]
    public void All_healthy_checks_produce_healthy_status()
    {
        var normalizer = new HealthStatusNormalizer();
        var response = Response("Healthy", ("database", "Healthy"));

        var result = normalizer.Normalize(response, HttpStatusCode.OK, TimeSpan.FromMilliseconds(4));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void Non_critical_unhealthy_check_produces_degraded_status_by_default()
    {
        var normalizer = new HealthStatusNormalizer();
        var response = Response(UnhealthyStatus, ("cache", UnhealthyStatus));

        var result = normalizer.Normalize(response, HttpStatusCode.ServiceUnavailable, TimeSpan.FromMilliseconds(9));

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public void Critical_check_failure_produces_unhealthy_status()
    {
        var normalizer = new HealthStatusNormalizer();
        var response = Response(UnhealthyStatus, ("database", UnhealthyStatus));
        var policy = new StatusPolicy { CriticalChecks = ["database"] };

        var result = normalizer.Normalize(response, HttpStatusCode.ServiceUnavailable, TimeSpan.FromMilliseconds(9), policy);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public void Invalid_response_produces_unhealthy_status_with_error()
    {
        var normalizer = new HealthStatusNormalizer();
        var result = normalizer.NormalizeInvalid(HttpStatusCode.InternalServerError, "not-json", TimeSpan.FromMilliseconds(2));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("not-json", result.Error, StringComparison.Ordinal);
    }

    private static HealthResponseEnvelope Response(string status, params (string Name, string Status)[] checks)
    {
        var entries = checks.ToDictionary(
            check => check.Name,
            check => new HealthResponseEntry(check.Status, check.Status, TimeSpan.FromMilliseconds(1), null, new Dictionary<string, JsonElement>()));

        return new HealthResponseEnvelope(status, entries);
    }
}
