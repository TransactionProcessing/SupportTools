using System.Net;

namespace HealthMonitoring.Domain;

public interface IHealthStatusNormalizer
{
    NormalizedHealthResult Normalize(
        HealthResponseEnvelope response,
        HttpStatusCode? httpStatusCode,
        TimeSpan duration,
        StatusPolicy? policy = null);

    NormalizedHealthResult NormalizeInvalid(
        HttpStatusCode? httpStatusCode,
        string rawResponse,
        TimeSpan duration,
        StatusPolicy? policy = null);
}
