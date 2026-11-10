using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CentralLogging.Forwarder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CentralLogging.Tests.Forwarder;

public sealed class LogForwarderWorkerTests
{
    private const String PaymentsServiceName = "Payments";
    private const String SourceFileName = "events.jsonl";
    private const String CheckpointFileName = "checkpoint.json";

    [Fact]
    public async Task Worker_retains_invalid_lines_and_continues_forwarding_valid_lines()
    {
        using TemporaryDirectory directory = new();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        String firstLine = EventLine(firstId, PaymentsServiceName, "first", "Warning", useLevel: true);
        String malformedLine = "{broken json";
        String secondLine = EventLine(secondId, PaymentsServiceName, "second", "Information");
        String partialLine = EventLine(Guid.NewGuid(), PaymentsServiceName, "not complete");
        String incompleteLine = "{\"eventId\":\"not-a-guid\",\"message\":\"missing fields\"}";
        String completeContent = $"{firstLine}\n{malformedLine}\n{incompleteLine}\n{secondLine}\n";
        String sourceFile = Path.Combine(directory.Path, SourceFileName);
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        await File.WriteAllTextAsync(sourceFile, completeContent + partialLine, new UTF8Encoding(false));

        RecordingLogger logger = new();
        using RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient client = new(handler);
        LogForwarderWorker worker = CreateWorker(directory.Path, checkpointPath, client, logger);
        using CancellationTokenSource cancellation = new();

        await worker.StartAsync(cancellation.Token);
        try
        {
            CapturedRequest request = await handler.ReadRequestAsync();
            request.ApiKey.ShouldBe("forwarder-test-key");
            JsonDocument events = JsonDocument.Parse(request.Body);
            using (events)
            {
                events.RootElement.GetArrayLength().ShouldBe(4);
                events.RootElement[0].GetProperty("eventId").GetGuid().ShouldBe(firstId);
                events.RootElement[0].GetProperty("severity").GetString().ShouldBe("Warning");
                events.RootElement[1].GetProperty("serviceName").GetString().ShouldBe("Unknown");
                events.RootElement[1].GetProperty("severity").GetString().ShouldBe("Unknown");
                events.RootElement[1].GetProperty("message").GetString().ShouldBe(malformedLine);
                events.RootElement[1].GetProperty("rawLog").GetString().ShouldBe(malformedLine);
                events.RootElement[2].GetProperty("serviceName").GetString().ShouldBe("Unknown");
                events.RootElement[2].GetProperty("rawLog").GetString().ShouldBe(incompleteLine);
                events.RootElement[3].GetProperty("eventId").GetGuid().ShouldBe(secondId);
            }

            Int64 completedOffset = Encoding.UTF8.GetByteCount(completeContent);
            await WaitForCheckpointAsync(checkpointPath, sourceFile, completedOffset);
            (await ReadCheckpointOffsetAsync(checkpointPath, sourceFile)).ShouldBe((Int64?)completedOffset);
            logger.Messages.ShouldContain(message => message.Contains("retained 2 invalid lines", StringComparison.Ordinal));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_skips_events_with_fields_that_exceed_the_ingestion_api_limits()
    {
        using TemporaryDirectory directory = new();
        Guid invalidId = Guid.NewGuid();
        Guid validId = Guid.NewGuid();
        String invalidLine = EventLine(invalidId, PaymentsServiceName, "invalid", requestId: new String('r', 201));
        String validLine = EventLine(validId, PaymentsServiceName, "valid");
        String sourceFile = Path.Combine(directory.Path, SourceFileName);
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        String content = invalidLine + "\n" + validLine + "\n";
        await File.WriteAllTextAsync(sourceFile, content, new UTF8Encoding(false));

        using RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient client = new(handler);
        LogForwarderWorker worker = CreateWorker(directory.Path, checkpointPath, client);
        using CancellationTokenSource cancellation = new();

        await worker.StartAsync(cancellation.Token);
        try
        {
            CapturedRequest request = await handler.ReadRequestAsync();
            using JsonDocument events = JsonDocument.Parse(request.Body);
            events.RootElement.GetArrayLength().ShouldBe(2);
            events.RootElement[0].GetProperty("rawLog").GetString().ShouldBe(invalidLine);
            events.RootElement[1].GetProperty("eventId").GetGuid().ShouldBe(validId);
            await WaitForCheckpointAsync(checkpointPath, sourceFile, Encoding.UTF8.GetByteCount(content));
            handler.RequestCount.ShouldBe(1);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_reuses_the_same_event_id_when_a_retained_line_is_retried()
    {
        using TemporaryDirectory directory = new();
        String malformedLine = "{broken json";
        String sourceFile = Path.Combine(directory.Path, SourceFileName);
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        await File.WriteAllTextAsync(sourceFile, malformedLine + "\n", new UTF8Encoding(false));

        using RecordingHandler handler = new(attempt => new HttpResponseMessage(
            attempt == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted));
        using HttpClient client = new(handler);
        LogForwarderWorker worker = CreateWorker(directory.Path, checkpointPath, client);
        using CancellationTokenSource cancellation = new();

        await worker.StartAsync(cancellation.Token);
        try
        {
            CapturedRequest failedAttempt = await handler.ReadRequestAsync();
            CapturedRequest retryAttempt = await handler.ReadRequestAsync();
            Guid[] firstIds = ReadEventIds(failedAttempt.Body);
            Guid[] retryIds = ReadEventIds(retryAttempt.Body);
            firstIds.Length.ShouldBe(1);
            retryIds.ShouldBe(firstIds);
            await WaitForCheckpointAsync(checkpointPath, sourceFile, Encoding.UTF8.GetByteCount(malformedLine + "\n"));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_retries_after_api_failure_and_advances_checkpoint_only_after_success()
    {
        using TemporaryDirectory directory = new();
        Guid eventId = Guid.NewGuid();
        String line = EventLine(eventId, PaymentsServiceName, "retry me");
        String sourceFile = Path.Combine(directory.Path, SourceFileName);
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        await File.WriteAllTextAsync(sourceFile, line + "\n", new UTF8Encoding(false));

        TaskCompletionSource<HttpResponseMessage> allowRetrySuccess = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using RecordingHandler handler = new(async (attempt, _, cancellationToken) => attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : await allowRetrySuccess.Task.WaitAsync(cancellationToken));
        using HttpClient client = new(handler);
        LogForwarderWorker worker = CreateWorker(directory.Path, checkpointPath, client);
        using CancellationTokenSource cancellation = new();

        await worker.StartAsync(cancellation.Token);
        try
        {
            CapturedRequest failedAttempt = await handler.ReadRequestAsync();
            CapturedRequest retryAttempt = await handler.ReadRequestAsync();
            ReadEventIds(failedAttempt.Body).ShouldBe([eventId]);
            ReadEventIds(retryAttempt.Body).ShouldBe([eventId]);

            (await ReadCheckpointOffsetAsync(checkpointPath, sourceFile)).ShouldBeNull();
            allowRetrySuccess.SetResult(new HttpResponseMessage(HttpStatusCode.Accepted));
            await WaitForCheckpointAsync(checkpointPath, sourceFile, Encoding.UTF8.GetByteCount(line + "\n"));
            handler.RequestCount.ShouldBe(2);
        }
        finally
        {
            allowRetrySuccess.TrySetCanceled();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_recovers_checkpoint_after_restart_and_file_rename()
    {
        using TemporaryDirectory directory = new();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        String firstLine = EventLine(firstId, PaymentsServiceName, new String('x', 1400));
        String secondLine = EventLine(secondId, PaymentsServiceName, "second line");
        String originalPath = Path.Combine(directory.Path, SourceFileName);
        String renamedPath = Path.Combine(directory.Path, "events.jsonl.1.jsonl");
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        await File.WriteAllTextAsync(originalPath, firstLine + "\n", new UTF8Encoding(false));

        using (RecordingHandler firstHandler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted)))
        using (HttpClient firstClient = new(firstHandler))
        {
            LogForwarderWorker firstWorker = CreateWorker(directory.Path, checkpointPath, firstClient);
            using CancellationTokenSource firstCancellation = new();
            await firstWorker.StartAsync(firstCancellation.Token);
            try
            {
                CapturedRequest firstRequest = await firstHandler.ReadRequestAsync();
                ReadEventIds(firstRequest.Body).ShouldBe([firstId]);
                await WaitForCheckpointAsync(checkpointPath, originalPath, Encoding.UTF8.GetByteCount(firstLine + "\n"));
            }
            finally
            {
                await firstWorker.StopAsync(CancellationToken.None);
            }
        }

        File.Move(originalPath, renamedPath);
        await File.AppendAllTextAsync(renamedPath, secondLine + "\n", new UTF8Encoding(false));

        using RecordingHandler restartedHandler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient restartedClient = new(restartedHandler);
        LogForwarderWorker restartedWorker = CreateWorker(directory.Path, checkpointPath, restartedClient);
        using CancellationTokenSource restartedCancellation = new();
        await restartedWorker.StartAsync(restartedCancellation.Token);
        try
        {
            CapturedRequest resumedRequest = await restartedHandler.ReadRequestAsync();
            ReadEventIds(resumedRequest.Body).ShouldBe([secondId]);
            await WaitForCheckpointAsync(checkpointPath,
                                         renamedPath,
                                         Encoding.UTF8.GetByteCount(firstLine + "\n" + secondLine + "\n"));
            using JsonDocument checkpoint = JsonDocument.Parse(await File.ReadAllTextAsync(checkpointPath));
            checkpoint.RootElement.TryGetProperty(originalPath, out _).ShouldBeFalse();
            checkpoint.RootElement.TryGetProperty(renamedPath, out _).ShouldBeTrue();
        }
        finally
        {
            await restartedWorker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_tracks_distinct_files_with_identical_first_kilobytes_separately()
    {
        using TemporaryDirectory directory = new();
        String firstPath = Path.Combine(directory.Path, "first.jsonl");
        String secondPath = Path.Combine(directory.Path, "second.jsonl");
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        Guid eventId = Guid.NewGuid();
        String commonMessage = new('x', 1400);
        String firstLine = EventLineWithTrailingProperty(eventId, commonMessage, "first");
        String secondLine = EventLineWithTrailingProperty(eventId, commonMessage, "second");
        firstLine[..1024].ShouldBe(secondLine[..1024]);
        await File.WriteAllTextAsync(firstPath, firstLine + "\n", new UTF8Encoding(false));
        await File.WriteAllTextAsync(secondPath, secondLine + "\n", new UTF8Encoding(false));
        File.SetCreationTimeUtc(firstPath, DateTime.UnixEpoch);
        File.SetCreationTimeUtc(secondPath, DateTime.UnixEpoch);
        File.SetLastWriteTimeUtc(firstPath, DateTime.UnixEpoch);
        File.SetLastWriteTimeUtc(secondPath, DateTime.UnixEpoch);

        using RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient client = new(handler);
        LogForwarderWorker worker = CreateWorker(directory.Path, checkpointPath, client);
        using CancellationTokenSource cancellation = new();

        await worker.StartAsync(cancellation.Token);
        try
        {
            String[] received = [(await handler.ReadRequestAsync()).Body, (await handler.ReadRequestAsync()).Body];
            received.SelectMany(ReadEventIds).Count().ShouldBe(2);
            await WaitForCheckpointAsync(checkpointPath, firstPath, Encoding.UTF8.GetByteCount(firstLine + "\n"));
            await WaitForCheckpointAsync(checkpointPath, secondPath, Encoding.UTF8.GetByteCount(secondLine + "\n"));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_replays_when_a_file_is_rewritten_past_the_saved_offset()
    {
        using TemporaryDirectory directory = new();
        String sourceFile = Path.Combine(directory.Path, SourceFileName);
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        Guid eventId = Guid.NewGuid();
        String message = new('x', 1400);
        String originalLine = EventLineWithTrailingProperty(eventId, message, "first");
        String rewrittenLine = EventLineWithTrailingProperty(eventId, message, "other");
        originalLine[..1024].ShouldBe(rewrittenLine[..1024]);
        originalLine.Length.ShouldBe(rewrittenLine.Length);
        await File.WriteAllTextAsync(sourceFile, originalLine + "\n", new UTF8Encoding(false));

        using (RecordingHandler firstHandler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted)))
        using (HttpClient firstClient = new(firstHandler))
        {
            LogForwarderWorker firstWorker = CreateWorker(directory.Path, checkpointPath, firstClient);
            using CancellationTokenSource firstCancellation = new();
            await firstWorker.StartAsync(firstCancellation.Token);
            try
            {
                ReadEventIds((await firstHandler.ReadRequestAsync()).Body).ShouldBe([eventId]);
                await WaitForCheckpointAsync(checkpointPath, sourceFile, Encoding.UTF8.GetByteCount(originalLine + "\n"));
            }
            finally
            {
                await firstWorker.StopAsync(CancellationToken.None);
            }
        }

        await File.WriteAllTextAsync(sourceFile, rewrittenLine + "\n", new UTF8Encoding(false));

        using RecordingHandler restartedHandler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient restartedClient = new(restartedHandler);
        LogForwarderWorker restartedWorker = CreateWorker(directory.Path, checkpointPath, restartedClient);
        using CancellationTokenSource restartedCancellation = new();
        await restartedWorker.StartAsync(restartedCancellation.Token);
        try
        {
            ReadEventIds((await restartedHandler.ReadRequestAsync()).Body).ShouldBe([eventId]);
        }
        finally
        {
            await restartedWorker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_restarts_from_the_beginning_when_a_checkpointed_file_is_truncated()
    {
        using TemporaryDirectory directory = new();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        String largeMessage = new('x', 1400);
        String firstLine = EventLine(firstId, PaymentsServiceName, largeMessage);
        String secondLine = EventLine(secondId, PaymentsServiceName, "second");
        String sourceFile = Path.Combine(directory.Path, SourceFileName);
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        String originalContent = firstLine + "\n" + secondLine + "\n";
        await File.WriteAllTextAsync(sourceFile, originalContent, new UTF8Encoding(false));

        using (RecordingHandler firstHandler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted)))
        using (HttpClient firstClient = new(firstHandler))
        {
            LogForwarderWorker firstWorker = CreateWorker(directory.Path, checkpointPath, firstClient);
            using CancellationTokenSource firstCancellation = new();
            await firstWorker.StartAsync(firstCancellation.Token);
            try
            {
                CapturedRequest firstRequest = await firstHandler.ReadRequestAsync();
                ReadEventIds(firstRequest.Body).ShouldBe([firstId, secondId]);
                await WaitForCheckpointAsync(checkpointPath, sourceFile, Encoding.UTF8.GetByteCount(originalContent));
            }
            finally
            {
                await firstWorker.StopAsync(CancellationToken.None);
            }
        }

        String truncatedContent = firstLine + "\n";
        await File.WriteAllTextAsync(sourceFile, truncatedContent, new UTF8Encoding(false));
        (new FileInfo(sourceFile).Length).ShouldBeLessThan(Encoding.UTF8.GetByteCount(originalContent));

        using RecordingHandler restartedHandler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient restartedClient = new(restartedHandler);
        LogForwarderWorker restartedWorker = CreateWorker(directory.Path, checkpointPath, restartedClient);
        using CancellationTokenSource restartedCancellation = new();
        await restartedWorker.StartAsync(restartedCancellation.Token);
        try
        {
            CapturedRequest restartedRequest = await restartedHandler.ReadRequestAsync();
            ReadEventIds(restartedRequest.Body).ShouldBe([firstId]);
            await WaitForCheckpointAsync(checkpointPath, sourceFile, Encoding.UTF8.GetByteCount(truncatedContent));
        }
        finally
        {
            await restartedWorker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_keeps_checkpoint_cleanup_incomplete_when_a_source_directory_is_missing()
    {
        using TemporaryDirectory directory = new();
        String missingDirectory = Path.Combine(directory.Path, "missing");
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        RecordingLogger logger = new();
        using RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient client = new(handler);
        LogForwarderWorker worker = CreateWorker(missingDirectory, checkpointPath, client, logger);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            logger.Messages.ShouldContain(message => message.Contains("does not exist", StringComparison.Ordinal));
            handler.RequestCount.ShouldBe(0);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_logs_a_scan_failure_when_a_file_pattern_is_invalid()
    {
        using TemporaryDirectory directory = new();
        String checkpointPath = Path.Combine(directory.Path, CheckpointFileName);
        RecordingLogger logger = new();
        using RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient client = new(handler);
        LogForwarderWorker worker = CreateWorker(directory.Path,
                                                 checkpointPath,
                                                 client,
                                                 logger,
                                                 [null!]);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            logger.Messages.ShouldContain(message => message.Contains("Could not scan log source directory", StringComparison.Ordinal));
            handler.RequestCount.ShouldBe(0);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static LogForwarderWorker CreateWorker(String sourceDirectory,
                                                   String checkpointPath,
                                                   HttpClient client,
                                                   ILogger<LogForwarderWorker>? logger = null,
                                                   String[]? filePatterns = null)
    {
        ForwarderOptions options = new()
        {
            SourceDirectories = [sourceDirectory],
            FilePatterns = filePatterns ?? ["*.jsonl"],
            IncludeSubdirectories = false,
            Endpoint = "http://localhost/api/logs",
            ApiKey = "forwarder-test-key",
            CheckpointPath = checkpointPath,
            BatchSize = 20,
            PollIntervalSeconds = 1
        };
        return new LogForwarderWorker(new SingleClientFactory(client),
                                      new ForwarderCheckpointStore(),
                                      Options.Create(options),
                                      logger ?? NullLogger<LogForwarderWorker>.Instance);
    }

    private static String EventLine(Guid eventId,
                                    String serviceName,
                                    String message,
                                    String severity = "Information",
                                    Boolean useLevel = false,
                                    String? requestId = null)
    {
        Dictionary<String, Object> value = new()
        {
            ["eventId"] = eventId,
            ["timestampUtc"] = DateTimeOffset.UtcNow,
            [useLevel ? "level" : "severity"] = severity,
            ["serviceName"] = serviceName,
            ["message"] = message
        };
        if (requestId is not null)
            value["requestId"] = requestId;
        return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static String EventLineWithTrailingProperty(Guid eventId, String message, String value)
    {
        Dictionary<String, Object> item = new()
        {
            ["eventId"] = eventId,
            ["timestampUtc"] = DateTimeOffset.UnixEpoch,
            ["severity"] = "Information",
            ["serviceName"] = PaymentsServiceName,
            ["message"] = message,
            ["trailing"] = value
        };
        return JsonSerializer.Serialize(item, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static Guid[] ReadEventIds(String body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("eventId").GetGuid())
            .ToArray();
    }

    private static async Task WaitForCheckpointAsync(String checkpointPath, String filePath, Int64 expectedOffset)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(checkpointPath) && await ReadCheckpointOffsetAsync(checkpointPath, filePath) == expectedOffset)
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException($"Checkpoint for {filePath} did not reach byte offset {expectedOffset}.");
    }

    private static async Task<Int64?> ReadCheckpointOffsetAsync(String checkpointPath, String filePath)
    {
        if (!File.Exists(checkpointPath)) return null;
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(checkpointPath));
        return document.RootElement.TryGetProperty(filePath, out JsonElement entry)
            ? entry.GetProperty("byteOffset").GetInt64()
            : null;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Channel<CapturedRequest> Requests = Channel.CreateUnbounded<CapturedRequest>();
        private Int32 requestCount;
        private readonly Func<Int32, CapturedRequest, CancellationToken, Task<HttpResponseMessage>> ResponseFactory;

        public RecordingHandler(Func<Int32, HttpResponseMessage> responseFactory)
            : this((attempt, _, _) => Task.FromResult(responseFactory(attempt)))
        {
        }

        public RecordingHandler(Func<Int32, CapturedRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            this.ResponseFactory = responseFactory;
        }

        public Int32 RequestCount => Volatile.Read(ref this.requestCount);

        public Task<CapturedRequest> ReadRequestAsync() => this.Requests.Reader.ReadAsync().AsTask()
            .WaitAsync(TimeSpan.FromSeconds(10));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                      CancellationToken cancellationToken)
        {
            String body = request.Content is null
                ? String.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            String apiKey = request.Headers.TryGetValues("X-Log-Api-Key", out IEnumerable<String>? values)
                ? values.Single()
                : String.Empty;
            CapturedRequest capturedRequest = new(request.Method, request.RequestUri, apiKey, body);
            Int32 attempt = Interlocked.Increment(ref this.requestCount);
            await this.Requests.Writer.WriteAsync(capturedRequest, cancellationToken);
            return await this.ResponseFactory(attempt, capturedRequest, cancellationToken);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(String name)
        {
            name.ShouldBe("CentralLogs");
            return client;
        }
    }

    private sealed class RecordingLogger : ILogger<LogForwarderWorker>
    {
        public List<String> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            _ = state;
            return null;
        }

        public Boolean IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel,
                                EventId eventId,
                                TState state,
                                Exception? exception,
                                Func<TState, Exception?, String> formatter)
        {
            lock (this.Messages)
                this.Messages.Add($"{logLevel} {eventId.Id}: {formatter(state, exception)}");
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, String ApiKey, String Body);

    private sealed class TemporaryDirectory : IDisposable
    {
        public String Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(this.Path);

        public void Dispose() => Directory.Delete(this.Path, recursive: true);
    }
}
