using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace CentralLogging.Forwarder;

public sealed class LogForwarderWorker(IHttpClientFactory httpClientFactory,
                                       ForwarderCheckpointStore checkpointStore,
                                       IOptions<ForwarderOptions> options,
                                       ILogger<LogForwarderWorker> logger) : BackgroundService
{
    private readonly ForwarderOptions Settings = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (this.Settings.SourceDirectories.Length == 0 || this.Settings.FilePatterns.Length == 0 ||
            !Uri.TryCreate(this.Settings.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || String.IsNullOrWhiteSpace(this.Settings.ApiKey))
        {
            throw new InvalidOperationException("Configure LogForwarder:SourceDirectories, Endpoint, and ApiKey.");
        }

        Int32 batchSize = Math.Clamp(this.Settings.BatchSize, 1, 500);
        TimeSpan pollInterval = TimeSpan.FromSeconds(Math.Clamp(this.Settings.PollIntervalSeconds, 1, 300));
        HttpClient client = httpClientFactory.CreateClient("CentralLogs");
        client.DefaultRequestHeaders.Add("X-Log-Api-Key", this.Settings.ApiKey);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                HashSet<String> seenFileIdentities = new(StringComparer.OrdinalIgnoreCase);
                HashSet<String> discoveredPaths = new(StringComparer.OrdinalIgnoreCase);
                Boolean scanCompleted = true;
                foreach (String directory in this.Settings.SourceDirectories)
                {
                    if (!Directory.Exists(directory))
                    {
                        logger.LogWarning("Log source directory {LogDirectory} does not exist.", directory);
                        scanCompleted = false;
                        continue;
                    }

                    String[] files;
                    try
                    {
                        SearchOption searchOption = this.Settings.IncludeSubdirectories
                            ? SearchOption.AllDirectories
                            : SearchOption.TopDirectoryOnly;
                        files = this.Settings.FilePatterns
                            .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, searchOption))
                            .Select(Path.GetFullPath)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "Could not scan log source directory {LogDirectory}.", directory);
                        scanCompleted = false;
                        continue;
                    }

                    foreach (String filePath in files)
                    {
                        discoveredPaths.Add(filePath);
                        try
                        {
                            await using FileStream file = new(filePath,
                                                              FileMode.Open,
                                                              FileAccess.Read,
                                                              FileShare.ReadWrite | FileShare.Delete);
                            String fileIdentity = await GetFileIdentityAsync(filePath, file, stoppingToken);
                            seenFileIdentities.Add(fileIdentity);
                            await this.ForwardFileAsync(client, file, filePath, fileIdentity, batchSize, stoppingToken);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            logger.LogError(exception, "Could not forward log file {LogFile}; it will be retried.", filePath);
                            scanCompleted = false;
                        }
                    }
                }

                if (scanCompleted)
                    await checkpointStore.RemoveUnseenAsync(seenFileIdentities,
                                                             discoveredPaths,
                                                             this.Settings.CheckpointPath,
                                                             stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Log forwarding poll failed; files will be retried.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    private async Task ForwardFileAsync(HttpClient client,
                                        FileStream file,
                                        String filePath,
                                        String fileIdentity,
                                        Int32 batchSize,
                                        CancellationToken cancellationToken)
    {
        String fullFilePath = Path.GetFullPath(filePath);
        Int64 offset = await checkpointStore.GetOffsetAsync(fullFilePath,
                                                             fileIdentity,
                                                             this.Settings.CheckpointPath,
                                                             cancellationToken);
        if (file.Length < offset)
        {
            logger.LogWarning("Log file {LogFile} was truncated; restarting from its beginning.", filePath);
            offset = 0;
            await checkpointStore.SaveOffsetAsync(fullFilePath,
                                                  fileIdentity,
                                                  offset,
                                                  this.Settings.CheckpointPath,
                                                  cancellationToken);
        }

        while (true)
        {
            List<ParsedLogLine> lines = await ReadBatchAsync(file, offset, batchSize, cancellationToken);
            if (lines.Count == 0) return;

            List<LogEventInput> events = new(lines.Count);
            foreach (ParsedLogLine line in lines)
            {
                try
                {
                    LogEventInput? item = JsonSerializer.Deserialize<LogEventInput>(line.Content, JsonOptions);
                    if (item != null) item.Severity ??= item.Level;
                    if (item == null || item.EventId == Guid.Empty || item.TimestampUtc == default ||
                        String.IsNullOrWhiteSpace(item.Severity) || String.IsNullOrWhiteSpace(item.ServiceName) ||
                        String.IsNullOrWhiteSpace(item.Message))
                    {
                        logger.LogWarning("Skipping invalid structured log entry in {LogFile} at byte {Offset}.", filePath, line.NextOffset);
                        continue;
                    }

                    events.Add(item);
                }
                catch (JsonException exception)
                {
                    logger.LogWarning(exception, "Skipping malformed structured log entry in {LogFile} at byte {Offset}.", filePath, line.NextOffset);
                }
            }

            Int64 batchEnd = lines[^1].NextOffset;
            if (events.Count > 0)
            {
                using StringContent content = new(JsonSerializer.Serialize(events, JsonOptions), Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await client.PostAsync(this.Settings.Endpoint, content, cancellationToken);
                response.EnsureSuccessStatusCode();
                logger.LogInformation("Forwarded {EventCount} log events from {LogFile}.", events.Count, filePath);
            }

            offset = batchEnd;
            await checkpointStore.SaveOffsetAsync(fullFilePath,
                                                  fileIdentity,
                                                  offset,
                                                  this.Settings.CheckpointPath,
                                                  cancellationToken);
        }
    }

    private static async Task<String> GetFileIdentityAsync(String filePath,
                                                          FileStream file,
                                                          CancellationToken cancellationToken)
    {
        Int64 startingPosition = file.Position;
        file.Seek(0, SeekOrigin.Begin);
        Byte[] prefix = new Byte[1024];
        Int32 bytesRead = await file.ReadAsync(prefix, cancellationToken);
        file.Seek(startingPosition, SeekOrigin.Begin);

        // Unix creation-time values can change when a file is renamed, so only include them on Windows.
        Int32 creationTimeLength = OperatingSystem.IsWindows() ? sizeof(Int64) : 0;
        Byte[] fingerprintInput = new Byte[creationTimeLength + bytesRead];
        if (creationTimeLength > 0)
            BitConverter.GetBytes(File.GetCreationTimeUtc(filePath).Ticks).CopyTo(fingerprintInput, 0);
        prefix.AsSpan(0, bytesRead).CopyTo(fingerprintInput.AsSpan(creationTimeLength));
        return Convert.ToHexString(SHA256.HashData(fingerprintInput));
    }

    private static async Task<List<ParsedLogLine>> ReadBatchAsync(FileStream stream,
                                                                  Int64 offset,
                                                                  Int32 batchSize,
                                                                  CancellationToken cancellationToken)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        List<ParsedLogLine> lines = new(batchSize);
        using MemoryStream line = new();
        Byte[] buffer = new Byte[8192];
        Int64 lineStart = offset;

        while (lines.Count < batchSize)
        {
            Int32 count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;

            for (Int32 index = 0; index < count; index++)
            {
                Byte value = buffer[index];
                if (value == (Byte)'\n')
                {
                    Int64 nextOffset = stream.Position - count + index + 1;
                    String text = Encoding.UTF8.GetString(line.GetBuffer(), 0, checked((Int32)line.Length)).TrimEnd('\r');
                    if (text.Length > 0) lines.Add(new ParsedLogLine(text, nextOffset));
                    line.SetLength(0);
                    lineStart = nextOffset;
                    if (lines.Count == batchSize)
                    {
                        stream.Seek(lineStart, SeekOrigin.Begin);
                        return lines;
                    }
                }
                else
                {
                    line.WriteByte(value);
                }
            }
        }

        if (line.Length > 0)
        {
            // A final line without a newline may still be in the process of being written.
            stream.Seek(lineStart, SeekOrigin.Begin);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return lines;
    }

    private sealed record ParsedLogLine(String Content, Int64 NextOffset);

    private sealed class LogEventInput
    {
        public Guid EventId { get; init; }
        public DateTimeOffset TimestampUtc { get; init; }
        public String? Severity { get; set; }
        [JsonPropertyName("level")]
        public String? Level { get; init; }
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
}
