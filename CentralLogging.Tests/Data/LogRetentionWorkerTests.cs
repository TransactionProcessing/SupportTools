using CentralLogging.Api.Data;
using CentralLogging.Api.Models;
using CentralLogging.Tests.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;

namespace CentralLogging.Tests.Data;

[Collection("CentralLoggingApi")]
public sealed class LogRetentionWorkerTests(SqlServerApiFixture fixture)
{
    [SqlServerFact]
    public async Task Worker_deletes_expired_events_in_batches_and_preserves_recent_events()
    {
        await fixture.ResetDatabaseAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await AddEventsAsync(
            NewEvent(now.AddDays(-100)),
            NewEvent(now.AddDays(-91)),
            NewEvent(now.AddDays(-20)));

        await using ServiceProvider provider = CreateServiceProvider();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(_ => { });
        LogRetentionWorker worker = new(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new LogRetentionOptions { Enabled = true, RetentionDays = 90, BatchSize = 1, IntervalMinutes = 60 }),
            loggerFactory.CreateLogger<LogRetentionWorker>());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(async () => await fixture.ReadDatabaseAsync(database => database.LogEvents.CountAsync()) == 1);
        }
        finally
        {
            await StopAsync(worker);
        }

        DateTimeOffset[] remaining = await fixture.ReadDatabaseAsync(database => database.LogEvents
            .Select(log => log.TimestampUtc).ToArrayAsync());
        DateTimeOffset remainingTimestamp = remaining.ShouldHaveSingleItem();
        remainingTimestamp.ShouldBeGreaterThan(now.AddDays(-20).AddSeconds(-2));
        remainingTimestamp.ShouldBeLessThan(now.AddDays(-20).AddSeconds(2));
    }

    [SqlServerFact]
    public async Task Disabled_worker_leaves_expired_events_untouched()
    {
        await fixture.ResetDatabaseAsync();
        await AddEventsAsync(NewEvent(DateTimeOffset.UtcNow.AddDays(-100)));
        await using ServiceProvider provider = CreateServiceProvider();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(_ => { });
        LogRetentionWorker worker = new(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new LogRetentionOptions { Enabled = false, RetentionDays = 90, BatchSize = 1, IntervalMinutes = 60 }),
            loggerFactory.CreateLogger<LogRetentionWorker>());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(100);
            (await fixture.ReadDatabaseAsync(database => database.LogEvents.CountAsync())).ShouldBe(1);
        }
        finally
        {
            await StopAsync(worker);
        }
    }

    [Fact]
    public async Task Worker_logs_database_failures_without_stopping_the_background_service()
    {
        await using ServiceProvider provider = CreateServiceProvider("Server=127.0.0.1,1;Database=CentralLogs;User Id=test;Password=test;Encrypt=False;Connect Timeout=1");
        RecordingLogger logger = new();
        LogRetentionWorker worker = new(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new LogRetentionOptions { Enabled = true, RetentionDays = 90, BatchSize = 100, IntervalMinutes = 60 }),
            logger);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitUntilAsync(() => Task.FromResult(logger.Messages.Any(message => message.Contains("Log retention run failed", StringComparison.Ordinal))));
            logger.Messages.ShouldContain(message => message.Contains("Log retention run failed", StringComparison.Ordinal));
        }
        finally
        {
            await StopAsync(worker);
        }
    }

    private async Task AddEventsAsync(params CentralLogEvent[] events) => await fixture.ReadDatabaseAsync(async database =>
    {
        database.LogEvents.AddRange(events);
        await database.SaveChangesAsync();
        return true;
    });

    private ServiceProvider CreateServiceProvider(String? connectionString = null)
    {
        ServiceCollection services = new();
        services.AddDbContext<CentralLogsDbContext>(options => options.UseSqlServer(connectionString ?? fixture.ConnectionString));
        return services.BuildServiceProvider();
    }

    private static async Task StopAsync(LogRetentionWorker worker)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
    }

    private static async Task WaitUntilAsync(Func<Task<Boolean>> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(50);
        }

        (await condition()).ShouldBeTrue("The worker did not finish its retention run before the timeout.");
    }

    private static CentralLogEvent NewEvent(DateTimeOffset timestamp) => new()
    {
        EventId = Guid.NewGuid(),
        TimestampUtc = timestamp,
        Severity = "Information",
        ServiceName = "RetentionTest",
        Message = "retention test event"
    };

    private sealed class RecordingLogger : ILogger<LogRetentionWorker>
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
                this.Messages.Add(formatter(state, exception));
        }
    }
}
