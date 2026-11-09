namespace CentralLogging.Api.Models;

public sealed record StructuredFieldInfo(String Name,
                                         String[] Types,
                                         Int64 EventCount,
                                         DateTimeOffset FirstSeenUtc,
                                         DateTimeOffset LastSeenUtc);

public sealed record StructuredFieldsResponse(String ServiceName,
                                              StructuredFieldInfo[] Fields);
