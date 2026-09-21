using System.Text.Json;

namespace HealthMonitoring.Domain;

public sealed record HealthResponseEnvelope(
    string Status,
    IReadOnlyDictionary<string, HealthResponseEntry> Entries);

public sealed record HealthResponseEntry(
    string Status,
    string? Description,
    TimeSpan Duration,
    string? Exception,
    IReadOnlyDictionary<string, JsonElement> Data);

public sealed record NormalizedHealthResult(
    HealthStatus Status,
    string AggregateStatus,
    IReadOnlyList<NormalizedCheckResult> Checks,
    string? Error = null,
    string? RawResponse = null);

public sealed record NormalizedCheckResult(
    string Name,
    HealthStatus Status,
    string? Description,
    TimeSpan Duration,
    string? Exception,
    IReadOnlyDictionary<string, JsonElement> Data);
