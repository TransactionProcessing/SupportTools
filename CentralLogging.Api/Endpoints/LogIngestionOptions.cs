namespace CentralLogging.Api.Endpoints;

public sealed class LogIngestionOptions
{
    public const String SectionName = "LogIngestion";

    public Int64 MaximumRequestBytes { get; set; } = 10 * 1024 * 1024;
    public Int64 MaximumUploadBytes { get; set; } = 100 * 1024 * 1024;
    public Int32 MaximumLineBytes { get; set; } = 1024 * 1024;
}
