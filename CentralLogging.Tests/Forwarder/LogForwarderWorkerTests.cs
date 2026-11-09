using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CentralLogging.Forwarder;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CentralLogging.Tests.Forwarder;

public sealed class LogForwarderWorkerTests
{
    [Fact]
    public async Task Worker_forwards_complete_lines_skips_malformed_lines_and_leaves_a_partial_tail_uncheckpointed()
    {
        using TemporaryDirectory directory = new();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        String firstLine = EventLine(firstId, "Payments", "first", "Warning", useLevel: true);
        String malformedLine = "{broken json";
        String secondLine = EventLine(secondId, "Payments", "second", "Information");
        String partialLine = EventLine(Guid.NewGuid(), "Payments", "not complete");
        String completeContent = $"{firstLine}\n{malformedLine}\n{secondLine}\n";
        String sourceFile = Path.Combine(directory.Path, "events.jsonl");
        String checkpointPath = Path.Combine(directory.Path, "checkpoint.json");
        await File.WriteAllTextAsync(sourceFile, completeContent + partialLine, new UTF8Encoding(false));

        using RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        using HttpClient client = new(handler);
        LogForwarderWorker worker = CreateWorker(directory.Path, checkpointPath, client);
        using CancellationTokenSource cancellation = new();

        await worker.StartAsync(cancellation.Token);
        try
        {
            CapturedRequest request = await handler.ReadRequestAsync();
            request.ApiKey.ShouldBe("forwarder-test-key");
            JsonDocument events = JsonDocument.Parse(request.Body);
            using (events)
            {
                events.RootElement.GetArrayLength().ShouldBe(2);
                events.RootElement[0].GetProperty("eventId").GetGuid().ShouldBe(firstId);
                events.RootElement[0].GetProperty("severity").GetString().ShouldBe("Warning");
                events.RootElement[1].GetProperty("eventId").GetGuid().ShouldBe(secondId);
            }

            Int64 completedOffset = Encoding.UTF8.GetByteCount(completeContent);
            await WaitForCheckpointAsync(checkpointPath, sourceFile, completedOffset);
            (await ReadCheckpointOffsetAsync(checkpointPath, sourceFile)).ShouldBe((Int64?)completedOffset);
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
        String line = EventLine(eventId, "Payments", "retry me");
        String sourceFile = Path.Combine(directory.Path, "events.jsonl");
        String checkpointPath = Path.Combine(directory.Path, "checkpoint.json");
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
        String firstLine = EventLine(firstId, "Payments", new String('x', 1400));
        String secondLine = EventLine(secondId, "Payments", "second line");
        String originalPath = Path.Combine(directory.Path, "events.jsonl");
        String renamedPath = Path.Combine(directory.Path, "events.jsonl.1.jsonl");
        String checkpointPath = Path.Combine(directory.Path, "checkpoint.json");
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
    public async Task Worker_restarts_from_the_beginning_when_a_checkpointed_file_is_truncated()
    {
        using TemporaryDirectory directory = new();
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        String largeMessage = new('x', 1400);
        String firstLine = EventLine(firstId, "Payments", largeMessage);
        String secondLine = EventLine(secondId, "Payments", "second");
        String sourceFile = Path.Combine(directory.Path, "events.jsonl");
        String checkpointPath = Path.Combine(directory.Path, "checkpoint.json");
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

    private static LogForwarderWorker CreateWorker(String sourceDirectory, String checkpointPath, HttpClient client)
    {
        ForwarderOptions options = new()
        {
            SourceDirectories = [sourceDirectory],
            FilePatterns = ["*.jsonl"],
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
                                      NullLogger<LogForwarderWorker>.Instance);
    }

    private static String EventLine(Guid eventId,
                                    String serviceName,
                                    String message,
                                    String severity = "Information",
                                    Boolean useLevel = false)
    {
        Dictionary<String, Object> value = new()
        {
            ["eventId"] = eventId,
            ["timestampUtc"] = DateTimeOffset.UtcNow,
            [useLevel ? "level" : "severity"] = severity,
            ["serviceName"] = serviceName,
            ["message"] = message
        };
        return JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
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
        public HttpClient CreateClient(String name) => client;
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, String ApiKey, String Body);

    private sealed class TemporaryDirectory : IDisposable
    {
        public String Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(this.Path);

        public void Dispose() => Directory.Delete(this.Path, recursive: true);
    }
}
