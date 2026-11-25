using System.Text;
using Microsoft.Extensions.Logging;

namespace CentralLogging.Shared;

public sealed class TraceFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly Object Sync = new();
    private readonly String DirectoryPath;
    private readonly String FilePrefix;
    private IExternalScopeProvider ScopeProvider = new LoggerExternalScopeProvider();
    private StreamWriter? Writer;
    private DateOnly? WriterDate;
    private Boolean IsDisposed;

    public TraceFileLoggerProvider(String directoryPath, String filePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePrefix);
        this.DirectoryPath = Path.GetFullPath(directoryPath);
        this.FilePrefix = filePrefix;
        Directory.CreateDirectory(this.DirectoryPath);
    }

    public ILogger CreateLogger(String categoryName) => new TraceFileLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeProvider);
        this.ScopeProvider = scopeProvider;
    }

    internal IDisposable? BeginScope<TState>(TState state) where TState : notnull => this.ScopeProvider.Push(state);

    public void Dispose()
    {
        lock (this.Sync)
        {
            if (this.IsDisposed)
                return;

            this.Writer?.Dispose();
            this.Writer = null;
            this.IsDisposed = true;
        }
    }

    private void Write<TState>(String categoryName,
                               LogLevel logLevel,
                               EventId eventId,
                               TState state,
                               Exception? exception,
                               Func<TState, Exception?, String> formatter)
    {
        String message = formatter(state, exception);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        StringBuilder scopeBuilder = new();
        this.ScopeProvider.ForEachScope(static (scope, builder) =>
        {
            if (builder.Length > 0)
                builder.Append(" => ");

            builder.Append(scope);
        }, scopeBuilder);
        String scopeText = scopeBuilder.Length > 0 ? $" [{scopeBuilder}]" : String.Empty;

        lock (this.Sync)
        {
            if (this.IsDisposed)
                return;

            this.EnsureWriter(DateOnly.FromDateTime(timestamp.UtcDateTime));
            this.Writer!.WriteLine($"{timestamp:yyyy-MM-dd HH:mm:ss.fff}Z [{FormatLevel(logLevel)}] {categoryName}{scopeText} ({eventId.Id}): {message}");
            if (exception is not null)
                this.Writer.WriteLine(exception);
        }
    }

    private void EnsureWriter(DateOnly logDate)
    {
        if (this.WriterDate == logDate && this.Writer is not null)
            return;

        this.Writer?.Dispose();
        String filePath = Path.Combine(this.DirectoryPath, $"{this.FilePrefix}-{logDate:yyyyMMdd}.log");
        FileStream stream = new(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        this.Writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        this.WriterDate = logDate;
    }

    private static String FormatLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERRO",
        LogLevel.Critical => "CRIT",
        _ => "NONE"
    };

    private sealed class TraceFileLogger(TraceFileLoggerProvider provider, String categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider.BeginScope(state);

        public Boolean IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel,
                                EventId eventId,
                                TState state,
                                Exception? exception,
                                Func<TState, Exception?, String> formatter)
        {
            if (!this.IsEnabled(logLevel))
                return;

            provider.Write(categoryName, logLevel, eventId, state, exception, formatter);
        }
    }
}
