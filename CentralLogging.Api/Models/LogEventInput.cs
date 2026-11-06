using System.Text.Json;
using System.Text.Json.Serialization;

namespace CentralLogging.Api.Models;

public sealed class LogEventInput
{
    public Guid EventId { get; init; }
    public DateTimeOffset TimestampUtc { get; init; }
    public String? Severity { get; set; }
    public String? ServiceName { get; init; }
    public Guid? CorrelationId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? MerchantId { get; init; }
    public String? TraceId { get; init; }
    public String? SpanId { get; init; }
    public String? RequestId { get; init; }
    public String? Message { get; init; }
    public String? MessageTemplate { get; init; }
    public String? Exception { get; init; }

    [JsonExtensionData]
    public Dictionary<String, JsonElement> AdditionalProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
