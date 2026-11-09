namespace CentralLogging.Api.Data;

public sealed class LogRetentionOptions
{
    public const String SectionName = "LogRetention";

    public Boolean Enabled { get; set; }
    public Int32 RetentionDays { get; set; } = 90;
    public Int32 BatchSize { get; set; } = 1_000;
    public Int32 IntervalMinutes { get; set; } = 60;
}
