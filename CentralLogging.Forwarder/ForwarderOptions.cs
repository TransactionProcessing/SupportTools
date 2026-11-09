namespace CentralLogging.Forwarder;

public sealed class ForwarderOptions
{
    public const String SectionName = "LogForwarder";

    public String[] SourceDirectories { get; init; } = [];
    public String[] FilePatterns { get; init; } = ["structured-logger-*.json*"];
    public Boolean IncludeSubdirectories { get; init; } = true;
    public String Endpoint { get; init; } = "";
    public String? ApiKey { get; init; }
    public String CheckpointPath { get; init; } = "forwarder-checkpoint.json";
    public Int32 BatchSize { get; init; } = 100;
    public Int32 PollIntervalSeconds { get; init; } = 5;
}
