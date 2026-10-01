using KurrentDB.Client;

namespace SupportTools.KurrentDbClient;

public sealed record KurrentDbEvent(
    string EventId,
    string EventType,
    string Timestamp,
    string Title);

public sealed class KurrentDbEventReader
{
    public async Task<IReadOnlyList<KurrentDbEvent>> ReadRecentEventsAsync(
        string connectionString,
        string streamPrefix,
        int eventCount,
        TimeSpan timeout,
        string? username = null,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamPrefix);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(eventCount);

        var settings = KurrentDBClientSettings.Create(connectionString);
        using var client = new KurrentDBClient(settings);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        UserCredentials? credentials = null;
        if (!string.IsNullOrWhiteSpace(username))
            credentials = new UserCredentials(username, password ?? string.Empty);

        var events = new List<KurrentDbEvent>(eventCount);
        var filter = StreamFilter.Prefix(streamPrefix);

        await foreach (var resolvedEvent in client.ReadAllAsync(
            Direction.Backwards,
            Position.End,
            filter,
            eventCount,
            resolveLinkTos: false,
            deadline: timeout,
            userCredentials: credentials,
            cancellationToken: timeoutSource.Token))
        {
            var currentEvent = resolvedEvent.Event;
            events.Add(new KurrentDbEvent(
                currentEvent.EventId.ToString(),
                currentEvent.EventType,
                currentEvent.Created.ToUniversalTime().ToString("o"),
                currentEvent.EventStreamId));
        }

        return events;
    }
}
