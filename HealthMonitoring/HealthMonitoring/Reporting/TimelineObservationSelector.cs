using HealthMonitoring.Domain;

namespace HealthMonitoring.Reporting;

public static class TimelineObservationSelector
{
    public static int MaximumSegments => 100;

    public static IEnumerable<HealthObservation> Select(IEnumerable<HealthObservation> observations) => observations
        .OrderByDescending(observation => observation.ObservedAtUtc)
        .Take(MaximumSegments)
        .OrderBy(observation => observation.ObservedAtUtc);
}
