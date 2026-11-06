namespace CentralLogging.Api.Models;

public sealed class ServiceStructuredField
{
    public String ServiceName { get; set; } = String.Empty;
    public String FieldName { get; set; } = String.Empty;
    public String JsonType { get; set; } = String.Empty;
    public DateTimeOffset FirstSeenUtc { get; set; }
    public DateTimeOffset LastSeenUtc { get; set; }
    public Int64 EventCount { get; set; }
}
