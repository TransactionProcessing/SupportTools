namespace CentralLogging.Api.Models;

public sealed class CentralLogEvent
{
    public Guid EventId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public DateOnly LogDateUtc { get; private set; }
    public String Severity { get; set; } = String.Empty;
    public String ServiceName { get; set; } = String.Empty;
    public Guid? CorrelationId { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? MerchantId { get; set; }
    public String? TraceId { get; set; }
    public String? SpanId { get; set; }
    public String? RequestId { get; set; }
    public String Message { get; set; } = String.Empty;
    public String? MessageTemplate { get; set; }
    public String? Exception { get; set; }
    public String PropertiesJson { get; set; } = "{}";
}
