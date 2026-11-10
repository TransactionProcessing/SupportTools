namespace CentralLogging.Api.Models;

public sealed record LogPageResponse(IReadOnlyList<CentralLogEvent> Items, String? NextCursor);

public sealed record LogCountByValue(String Value, Int64 Count);

public sealed record LogTimeBucketCount(DateTimeOffset BucketStartUtc, Int64 Count);

public sealed record LogAggregateResponse(
    IReadOnlyList<LogCountByValue> BySeverity,
    IReadOnlyList<LogCountByValue> ByService,
    IReadOnlyList<LogTimeBucketCount> ByTimeBucket,
    String TimeBucket);
