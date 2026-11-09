using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
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
        String apiKey = ValidateConfiguration(this.Settings);

        Int32 batchSize = Math.Clamp(this.Settings.BatchSize, 1, 500);
        TimeSpan pollInterval = TimeSpan.FromSeconds(Math.Clamp(this.Settings.PollIntervalSeconds, 1, 300));
        HttpClient client = httpClientFactory.CreateClient("CentralLogs");
        client.DefaultRequestHeaders.Add("X-Log-Api-Key", apiKey);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await this.PollAsync(client, batchSize, stoppingToken);
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

    private static String ValidateConfiguration(ForwarderOptions settings)
    {
        String? apiKey = settings.ApiKey;
        if (settings.SourceDirectories.Length == 0 || settings.FilePatterns.Length == 0 ||
            !Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            String.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configure LogForwarder:SourceDirectories, Endpoint, and ApiKey.");

        return apiKey;
    }

    private async Task PollAsync(HttpClient client, Int32 batchSize, CancellationToken cancellationToken)
    {
        HashSet<String> seenFileIdentities = new(StringComparer.OrdinalIgnoreCase);
        HashSet<String> discoveredPaths = new(StringComparer.OrdinalIgnoreCase);
        Boolean scanCompleted = await this.ProcessDirectoriesAsync(client,
                                                                   batchSize,
                                                                   seenFileIdentities,
                                                                   discoveredPaths,
                                                                   cancellationToken);
        if (scanCompleted)
            await checkpointStore.RemoveUnseenAsync(seenFileIdentities,
                                                     discoveredPaths,
                                                     this.Settings.CheckpointPath,
                                                     cancellationToken);
    }

    private async Task<Boolean> ProcessDirectoriesAsync(HttpClient client,
                                                        Int32 batchSize,
                                                        HashSet<String> seenFileIdentities,
                                                        HashSet<String> discoveredPaths,
                                                        CancellationToken cancellationToken)
    {
        Boolean scanCompleted = true;
        foreach (String directory in this.Settings.SourceDirectories)
        {
            Boolean directoryCompleted = await this.ProcessDirectoryAsync(client,
                                                                           directory,
                                                                           batchSize,
                                                                           seenFileIdentities,
                                                                           discoveredPaths,
                                                                           cancellationToken);
            scanCompleted = directoryCompleted && scanCompleted;
        }

        return scanCompleted;
    }

    private async Task<Boolean> ProcessDirectoryAsync(HttpClient client,
                                                      String directory,
                                                      Int32 batchSize,
                                                      HashSet<String> seenFileIdentities,
                                                      HashSet<String> discoveredPaths,
                                                      CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Log source directory {LogDirectory} does not exist.", directory);
            return false;
        }

        if (!TryEnumerateFiles(directory, out String[] files))
            return false;

        Boolean scanCompleted = true;
        foreach (String filePath in files)
        {
            discoveredPaths.Add(filePath);
            try
            {
                await this.ForwardDiscoveredFileAsync(client, filePath, batchSize, seenFileIdentities, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not forward log file {LogFile}; it will be retried.", filePath);
                scanCompleted = false;
            }
        }

        return scanCompleted;
    }

    private Boolean TryEnumerateFiles(String directory, out String[] files)
    {
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
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not scan log source directory {LogDirectory}.", directory);
            files = [];
            return false;
        }
    }

    private async Task ForwardDiscoveredFileAsync(HttpClient client,
                                                   String filePath,
                                                   Int32 batchSize,
                                                   HashSet<String> seenFileIdentities,
                                                   CancellationToken cancellationToken)
    {
        await using FileStream file = new(filePath,
                                          FileMode.Open,
                                          FileAccess.Read,
                                          FileShare.ReadWrite | FileShare.Delete);
        String fileIdentity = await GetFileIdentityAsync(filePath, file, cancellationToken);
        seenFileIdentities.Add(fileIdentity);
        await this.ForwardFileAsync(client, file, filePath, fileIdentity, batchSize, cancellationToken);
    }

    private async Task ForwardFileAsync(HttpClient client,
                                        FileStream file,
                                        String filePath,
                                        String fileIdentity,
                                        Int32 batchSize,
                                        CancellationToken cancellationToken)
    {
        String fullFilePath = Path.GetFullPath(filePath);
        ForwarderCheckpointStore.FileCheckpoint checkpoint = await checkpointStore.GetCheckpointAsync(fullFilePath,
                                                                                                       fileIdentity,
                                                                                                       this.Settings.CheckpointPath,
                                                                                                       cancellationToken);
        Int64 offset = checkpoint.ByteOffset;
        Boolean contentChanged = offset > 0 &&
            (String.IsNullOrEmpty(checkpoint.ContentFingerprint) ||
             checkpoint.ContentFingerprint != await GetContentFingerprintAsync(file, offset, cancellationToken));
        if (file.Length < offset || contentChanged)
            offset = await ResetTruncatedFileAsync(fullFilePath, filePath, fileIdentity, cancellationToken);

        while (true)
        {
            List<ParsedLogLine> lines = await ReadBatchAsync(file, offset, batchSize, cancellationToken);
            if (lines.Count == 0) return;

            List<LogEventInput> events = ParseEvents(lines, filePath, fileIdentity, out Int32 retainedLineCount);

            Int64 batchEnd = lines[^1].NextOffset;
            String contentFingerprint = await GetContentFingerprintAsync(file, batchEnd, cancellationToken);
            await ForwardBatchAsync(client, events, retainedLineCount, filePath, cancellationToken);

            offset = batchEnd;
            await checkpointStore.SaveOffsetAsync(fullFilePath,
                                                  fileIdentity,
                                                  offset,
                                                  this.Settings.CheckpointPath,
                                                  cancellationToken,
                                                  contentFingerprint);
        }
    }

    private async Task<Int64> ResetTruncatedFileAsync(String fullFilePath,
                                                       String filePath,
                                                       String fileIdentity,
                                                       CancellationToken cancellationToken)
    {
        logger.LogWarning("Log file {LogFile} was truncated or rewritten; restarting from its beginning.", filePath);
        await checkpointStore.SaveOffsetAsync(fullFilePath,
                                              fileIdentity,
                                              0,
                                              this.Settings.CheckpointPath,
                                              cancellationToken);
        return 0;
    }

    private List<LogEventInput> ParseEvents(IReadOnlyList<ParsedLogLine> lines,
                                            String filePath,
                                            String fileIdentity,
                                            out Int32 retainedLineCount)
    {
        List<LogEventInput> events = new(lines.Count);
        retainedLineCount = 0;
        foreach (ParsedLogLine line in lines)
        {
            try
            {
                LogEventInput? item = JsonSerializer.Deserialize<LogEventInput>(line.Content, JsonOptions);
                if (item is not null) item.Severity ??= item.Level;
                if (item is null || item.EventId == Guid.Empty || item.TimestampUtc == default ||
                    String.IsNullOrWhiteSpace(item.Severity) || String.IsNullOrWhiteSpace(item.ServiceName) ||
                    String.IsNullOrWhiteSpace(item.Message) || HasFieldsExceedingApiLimits(item))
                {
                    events.Add(CreateRawLineEvent(line, fileIdentity, item));
                    retainedLineCount++;
                    logger.LogWarning("Retaining invalid structured log entry from {LogFile} at byte {Offset}.", filePath, line.NextOffset);
                    continue;
                }

                events.Add(item);
            }
            catch (JsonException exception)
            {
                events.Add(CreateRawLineEvent(line, fileIdentity, null));
                retainedLineCount++;
                logger.LogWarning(exception, "Retaining malformed structured log entry from {LogFile} at byte {Offset}.", filePath, line.NextOffset);
            }
        }

        return events;
    }

    private async Task ForwardBatchAsync(HttpClient client,
                                        IReadOnlyList<LogEventInput> events,
                                        Int32 retainedLineCount,
                                        String filePath,
                                        CancellationToken cancellationToken)
    {
        if (events.Count == 0) return;

        using StringContent content = new(JsonSerializer.Serialize(events, JsonOptions), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(this.Settings.Endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        logger.LogInformation("Forwarded {EventCount} log events from {LogFile}; retained {RetainedLineCount} invalid lines.",
                              events.Count, filePath, retainedLineCount);
    }

    private static LogEventInput CreateRawLineEvent(ParsedLogLine line, String fileIdentity, LogEventInput? source)
    {
        Dictionary<String, JsonElement> properties = source?.AdditionalProperties ?? new(StringComparer.OrdinalIgnoreCase);
        properties["rawLog"] = JsonSerializer.SerializeToElement(line.Content);
        if (source is null || source.EventId == Guid.Empty)
            properties["sourceEventId"] = JsonSerializer.SerializeToElement(source?.EventId.ToString());

        return new LogEventInput
        {
            EventId = CreateStableEventId(fileIdentity, line.NextOffset, line.Content),
            TimestampUtc = source is null || source.TimestampUtc == default ? DateTimeOffset.UtcNow : source.TimestampUtc,
            Severity = "Unknown",
            ServiceName = "Unknown",
            Message = line.Content,
            AdditionalProperties = properties
        };
    }

    private static Guid CreateStableEventId(String fileIdentity, Int64 nextOffset, String content)
    {
        Byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{fileIdentity}\n{nextOffset}\n{content}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static async Task<String> GetFileIdentityAsync(String filePath,
                                                          FileStream file,
                                                          CancellationToken cancellationToken)
    {
        Int64 startingPosition = file.Position;
        String prefixFingerprint;
        try
        {
            file.Seek(0, SeekOrigin.Begin);
            Byte[] prefix = new Byte[1024];
            Int32 bytesRead;
            Int32 totalBytesRead = 0;
            while (totalBytesRead < prefix.Length &&
                   (bytesRead = await file.ReadAsync(prefix.AsMemory(totalBytesRead), cancellationToken)) > 0)
                totalBytesRead += bytesRead;
            prefixFingerprint = Convert.ToHexString(SHA256.HashData(prefix.AsSpan(0, totalBytesRead)));
        }
        finally
        {
            file.Seek(startingPosition, SeekOrigin.Begin);
        }

        if (OperatingSystem.IsWindows() &&
            GetFileInformationByHandle(file.SafeFileHandle, out ByHandleFileInformation information) &&
            (information.FileIndexHigh != 0 || information.FileIndexLow != 0))
            return $"windows:{information.VolumeSerialNumber:X8}:{information.FileIndexHigh:X8}{information.FileIndexLow:X8}:{prefixFingerprint}";

        if (OperatingSystem.IsLinux() && IntPtr.Size == sizeof(Int64) &&
            FStat(file.SafeFileHandle.DangerousGetHandle().ToInt32(), out LinuxStat stat) == 0 && stat.Inode != 0)
            return $"linux:{stat.Device:X16}:{stat.Inode:X16}:{prefixFingerprint}";

        // Without a platform file ID, avoid mutable length and write-time values so append-only logs stay incremental.
        return $"fallback:{Path.GetFullPath(filePath)}:{File.GetCreationTimeUtc(filePath).Ticks}:{prefixFingerprint}";
    }

    private static async Task<String> GetContentFingerprintAsync(FileStream file,
                                                                 Int64 offset,
                                                                 CancellationToken cancellationToken)
    {
        Int64 startingPosition = file.Position;
        try
        {
            Int64 fingerprintStart = Math.Max(0, offset - 1024);
            file.Seek(fingerprintStart, SeekOrigin.Begin);
            Byte[] buffer = new Byte[checked((Int32)(offset - fingerprintStart))];
            Int32 totalBytesRead = 0;
            while (totalBytesRead < buffer.Length)
            {
                Int32 bytesRead = await file.ReadAsync(buffer.AsMemory(totalBytesRead), cancellationToken);
                if (bytesRead == 0) break;
                totalBytesRead += bytesRead;
            }

            return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, totalBytesRead)));
        }
        finally
        {
            file.Seek(startingPosition, SeekOrigin.Begin);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean GetFileInformationByHandle(SafeFileHandle fileHandle,
                                                              out ByHandleFileInformation fileInformation);

    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern Int32 FStat(Int32 fileDescriptor, out LinuxStat fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxStat : IEquatable<LinuxStat>
    {
        public UInt64 Device;
        public UInt64 Inode;
        public UInt64 LinkCount;
        public UInt32 Mode;
        public UInt32 UserId;
        public UInt32 GroupId;
        public Int32 Padding;
        public UInt64 SpecialDevice;
        public Int64 Size;
        public Int64 BlockSize;
        public Int64 Blocks;
        public Int64 AccessSeconds;
        public Int64 AccessNanoseconds;
        public Int64 ModifiedSeconds;
        public Int64 ModifiedNanoseconds;
        public Int64 ChangedSeconds;
        public Int64 ChangedNanoseconds;
        public Int64 Reserved0;
        public Int64 Reserved1;
        public Int64 Reserved2;

        public Boolean Equals(LinuxStat other) => this.Device == other.Device && this.Inode == other.Inode;
        public override Boolean Equals(Object? obj) => obj is LinuxStat other && this.Equals(other);
        public override Int32 GetHashCode() => HashCode.Combine(this.Device, this.Inode);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation : IEquatable<ByHandleFileInformation>
    {
        public UInt32 FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public UInt32 VolumeSerialNumber;
        public UInt32 FileSizeHigh;
        public UInt32 FileSizeLow;
        public UInt32 NumberOfLinks;
        public UInt32 FileIndexHigh;
        public UInt32 FileIndexLow;

        public Boolean Equals(ByHandleFileInformation other) =>
            this.VolumeSerialNumber == other.VolumeSerialNumber &&
            this.FileIndexHigh == other.FileIndexHigh &&
            this.FileIndexLow == other.FileIndexLow;
        public override Boolean Equals(Object? obj) => obj is ByHandleFileInformation other && this.Equals(other);
        public override Int32 GetHashCode() => HashCode.Combine(this.VolumeSerialNumber, this.FileIndexHigh, this.FileIndexLow);
    }

    private static Boolean HasFieldsExceedingApiLimits(LogEventInput item) =>
        item.Severity!.Length > 16 || item.ServiceName!.Length > 200 ||
        item.TraceId?.Length > 32 || item.SpanId?.Length > 16 || item.RequestId?.Length > 200 ||
        item.AdditionalProperties.Keys.Any(name => name.Length > 200);

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
        public Guid EventId { get; init; } = Guid.Empty;
        public DateTimeOffset TimestampUtc { get; init; } = default;
        public String? Severity { get; set; }
        [JsonPropertyName("level")]
        public String? Level { get; init; }
        public String? ServiceName { get; init; } = null;
        public Guid? CorrelationId { get; init; }
        public Guid? TenantId { get; init; }
        public Guid? MerchantId { get; init; }
        public String? TraceId { get; init; } = null;
        public String? SpanId { get; init; } = null;
        public String? RequestId { get; init; } = null;
        public String? Message { get; init; } = null;
        public String? MessageTemplate { get; init; }
        public String? Exception { get; init; }

        [JsonExtensionData]
        public Dictionary<String, JsonElement> AdditionalProperties { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    }
}
