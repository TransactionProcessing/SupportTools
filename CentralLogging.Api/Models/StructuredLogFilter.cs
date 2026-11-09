namespace CentralLogging.Api.Models;

public sealed record StructuredLogFilter(String Field, String Type, String Value);

public sealed record ServicesResponse(String[] Services);
