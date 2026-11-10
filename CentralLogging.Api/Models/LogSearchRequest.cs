namespace CentralLogging.Api.Models;

public sealed class LogSearchRequest
{
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public String? ServiceName { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? CorrelationId { get; set; }
    public String[]? Filter { get; set; }
    public String? Cursor { get; set; }
    public Int32? Take { get; set; }
    public Int32? MaxRows { get; set; }
    public String? Bucket { get; set; }
    public Int32? GroupTake { get; set; }
}
