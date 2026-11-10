using System.Text.Json;
using CentralLogging.Api.Data;
using CentralLogging.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CentralLogging.Api.Endpoints;

public static partial class LogEndpoints
{
    private const Int32 MaximumAggregateRangeDays = 31;
    private const Int32 DefaultExportRows = 10_000;
    private const Int32 MaximumExportRows = 100_000;
    private const Int32 MaximumAggregateGroups = 500;
    private static readonly Byte[] NewLine = [(Byte)'\n'];
    private static readonly JsonSerializerOptions NdjsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IResult> SearchPageAsync(CentralLogsDbContext database,
                                                      [AsParameters] LogSearchRequest request,
                                                      CancellationToken cancellationToken)
    {
        SearchCriteria? criteria = TryResolveSearch(request, requireExplicitRange: true, maximumRangeDays: null,
            out IResult? problem);
        if (criteria is null)
            return problem!;

        LogCursor? position = TryResolveCursor(request.Cursor, out problem);
        if (problem is not null)
            return problem;

        Int32 pageSize = Math.Clamp(request.Take ?? 100, 1, MaximumPageSize);
        IQueryable<CentralLogEvent> query = BuildEventQuery(database, criteria, request.ServiceName, request.TenantId,
            request.CorrelationId);
        List<CentralLogEvent> rows = await ReadPageRowsAsync(query, position, pageSize, cancellationToken);
        return CreatePageResponse(rows, pageSize);
    }

    private static LogCursor? TryResolveCursor(String? cursor, out IResult? problem)
    {
        problem = null;
        if (String.IsNullOrWhiteSpace(cursor))
            return null;

        if (cursor.Length > 512 || !TryDecodeCursor(cursor, out LogCursor? position))
        {
            problem = Results.Problem("cursor is not a valid continuation token.",
                statusCode: StatusCodes.Status400BadRequest);
            return null;
        }

        return position;
    }

    private static async Task<List<CentralLogEvent>> ReadPageRowsAsync(IQueryable<CentralLogEvent> query,
                                                                       LogCursor? position,
                                                                       Int32 pageSize,
                                                                       CancellationToken cancellationToken)
    {
        if (position is { } after)
        {
            query = query.Where(log => log.TimestampUtc < after.TimestampUtc ||
                                       (log.TimestampUtc == after.TimestampUtc &&
                                        log.EventId.CompareTo(after.EventId) < 0));
        }

        return await query
            .OrderByDescending(log => log.TimestampUtc)
            .ThenByDescending(log => log.EventId)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);
    }

    private static IResult CreatePageResponse(List<CentralLogEvent> rows, Int32 pageSize)
    {
        Boolean hasMore = rows.Count > pageSize;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        String? nextCursor = hasMore && rows.Count > 0 ? EncodeCursor(rows[^1]) : null;
        return Results.Ok(new LogPageResponse(rows, nextCursor));
    }

    public static async Task<IResult> ExportAsync(CentralLogsDbContext database,
                                      [AsParameters] LogSearchRequest request,
                                      HttpContext httpContext,
                                      CancellationToken cancellationToken)
    {
        SearchCriteria? criteria = TryResolveSearch(request, requireExplicitRange: true,
            maximumRangeDays: MaximumAggregateRangeDays, out IResult? problem);
        if (criteria is null)
            return problem!;

        Int32 rowLimit = request.MaxRows ?? DefaultExportRows;
        if (rowLimit is < 1 or > MaximumExportRows)
            return Results.Problem($"maxRows must be between 1 and {MaximumExportRows}.", statusCode: StatusCodes.Status400BadRequest);

        IQueryable<CentralLogEvent> filteredQuery = BuildEventQuery(database, criteria, request.ServiceName,
            request.TenantId, request.CorrelationId);
        Int32 rowsThroughLimit = await filteredQuery.Take(rowLimit + 1).CountAsync(cancellationToken);
        httpContext.Response.Headers.Append("X-Log-Export-Max-Rows", rowLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        httpContext.Response.Headers.Append("X-Log-Export-Truncated", (rowsThroughLimit > rowLimit).ToString().ToLowerInvariant());
        IQueryable<CentralLogEvent> query = filteredQuery
            .OrderByDescending(log => log.TimestampUtc)
            .ThenByDescending(log => log.EventId)
            .Take(rowLimit);

        return Results.Stream(async stream =>
        {
            CancellationToken token = cancellationToken;
            await foreach (CentralLogEvent log in query.AsAsyncEnumerable().WithCancellation(token))
            {
                await JsonSerializer.SerializeAsync(stream, log, NdjsonOptions, token);
                await stream.WriteAsync(NewLine, token);
            }
        }, contentType: "application/x-ndjson", fileDownloadName: "logs.ndjson");
    }

    public static async Task<IResult> AggregateAsync(CentralLogsDbContext database,
                                                     [AsParameters] LogSearchRequest request,
                                                     CancellationToken cancellationToken)
    {
        SearchCriteria? criteria = TryResolveSearch(request, requireExplicitRange: true,
            maximumRangeDays: MaximumAggregateRangeDays, out IResult? problem);
        if (criteria is null)
            return problem!;

        String timeBucket = String.IsNullOrWhiteSpace(request.Bucket) ? "hour" : request.Bucket.Trim().ToLowerInvariant();
        if (timeBucket is not ("hour" or "day"))
            return Results.Problem("bucket must be either hour or day.", statusCode: StatusCodes.Status400BadRequest);

        Int32 groupLimit = request.GroupTake ?? 100;
        if (groupLimit is < 1 or > MaximumAggregateGroups)
            return Results.Problem($"groupTake must be between 1 and {MaximumAggregateGroups}.",
                statusCode: StatusCodes.Status400BadRequest);

        IQueryable<CentralLogEvent> query = BuildEventQuery(database, criteria, request.ServiceName, request.TenantId,
            request.CorrelationId);
        var severityRows = await query
            .GroupBy(log => log.Severity)
            .Select(group => new { Value = group.Key, Count = group.LongCount() })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Value)
            .Take(groupLimit)
            .ToArrayAsync(cancellationToken);
        LogCountByValue[] bySeverity = severityRows
            .Select(row => new LogCountByValue(row.Value, row.Count))
            .ToArray();

        var serviceRows = await query
            .GroupBy(log => log.ServiceName)
            .Select(group => new { Value = group.Key, Count = group.LongCount() })
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Value)
            .Take(groupLimit)
            .ToArrayAsync(cancellationToken);
        LogCountByValue[] byService = serviceRows
            .Select(row => new LogCountByValue(row.Value, row.Count))
            .ToArray();

        Boolean byHour = timeBucket == "hour";
        var bucketRows = await query
            .GroupBy(log => new
            {
                Year = log.TimestampUtc.Year,
                Month = log.TimestampUtc.Month,
                Day = log.TimestampUtc.Day,
                Hour = byHour ? log.TimestampUtc.Hour : 0
            })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                group.Key.Day,
                group.Key.Hour,
                Count = group.LongCount()
            })
            .OrderBy(row => row.Year)
            .ThenBy(row => row.Month)
            .ThenBy(row => row.Day)
            .ThenBy(row => row.Hour)
            .ToArrayAsync(cancellationToken);

        LogTimeBucketCount[] byTimeBucket = bucketRows
            .Select(row => new LogTimeBucketCount(
                new DateTimeOffset(row.Year, row.Month, row.Day, row.Hour, 0, 0, TimeSpan.Zero),
                row.Count))
            .ToArray();

        return Results.Ok(new LogAggregateResponse(bySeverity, byService, byTimeBucket, timeBucket));
    }

    private static SearchCriteria? TryResolveSearch(LogSearchRequest request,
                                                    Boolean requireExplicitRange,
                                                    Int32? maximumRangeDays,
                                                    out IResult? problem)
    {
        problem = null;

        if (requireExplicitRange && (!request.FromUtc.HasValue || !request.ToUtc.HasValue))
        {
            problem = Results.Problem("fromUtc and toUtc are required.", statusCode: StatusCodes.Status400BadRequest);
            return null;
        }

        DateTimeOffset end = request.ToUtc?.ToUniversalTime() ?? DateTimeOffset.UtcNow;
        DateTimeOffset start = request.FromUtc?.ToUniversalTime() ?? end.AddHours(-24);
        if (start >= end)
        {
            problem = Results.Problem("fromUtc must be earlier than toUtc.", statusCode: StatusCodes.Status400BadRequest);
            return null;
        }

        if (maximumRangeDays.HasValue && end - start > TimeSpan.FromDays(maximumRangeDays.Value))
        {
            problem = Results.Problem($"The requested time range cannot exceed {maximumRangeDays.Value} days.",
                statusCode: StatusCodes.Status400BadRequest);
            return null;
        }

        if (!TryParseFilters(request.Filter, out StructuredLogFilter[] filters, out problem))
            return null;

        return new SearchCriteria(start, end, filters);
    }

    private static Boolean TryParseFilters(String[]? filter,
                                          out StructuredLogFilter[] filters,
                                          out IResult? problem)
    {
        filters = Array.Empty<StructuredLogFilter>();
        problem = null;
        try
        {
            filters = (filter ?? Array.Empty<String>())
                .Select(value => JsonSerializer.Deserialize<StructuredLogFilter>(value,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true })
                    ?? throw new JsonException("Filter cannot be null."))
                .ToArray();
        }
        catch (JsonException)
        {
            problem = Results.Problem("Each filter must be a JSON object with field, type, and value properties.",
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        if (!HasValidFilterDefinitions(filters))
        {
            problem = Results.Problem("Provide up to 10 filters with a field name (max 200 characters), type (string, number, boolean), and value.",
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        if (!HasValidFilterValues(filters))
        {
            problem = Results.Problem("Numeric and boolean filters must use valid values for their selected type.",
                statusCode: StatusCodes.Status400BadRequest);
            return false;
        }

        filters = NormalizeFilters(filters);
        return true;
    }

    private static Boolean HasValidFilterDefinitions(IReadOnlyList<StructuredLogFilter> filters) =>
        filters.Count <= 10 && filters.All(item => !String.IsNullOrWhiteSpace(item.Field) && item.Field.Length <= 200 &&
                                                   (item.Type is "string" or JsonNumberType or JsonBooleanType) && item.Value is not null);

    private static Boolean HasValidFilterValues(IReadOnlyList<StructuredLogFilter> filters) =>
        filters.All(item => item.Type != JsonNumberType || NormalizeJsonNumber(item.Value) is not null)
        && filters.All(item => item.Type != JsonBooleanType || Boolean.TryParse(item.Value, out _));

    private static StructuredLogFilter[] NormalizeFilters(IEnumerable<StructuredLogFilter> filters) => filters.Select(item => item.Type switch
        {
            JsonNumberType => item with { Value = NormalizeJsonNumber(item.Value)! },
            JsonBooleanType => item with { Value = Boolean.Parse(item.Value).ToString().ToLowerInvariant() },
            _ => item
        }).ToArray();

    private static IQueryable<CentralLogEvent> BuildEventQuery(CentralLogsDbContext database,
                                                               SearchCriteria criteria,
                                                               String? serviceName,
                                                               Guid? tenantId,
                                                               Guid? correlationId)
    {
        DateOnly firstDateUtc = DateOnly.FromDateTime(criteria.Start.UtcDateTime);
        DateOnly lastDateUtc = DateOnly.FromDateTime(criteria.End.AddTicks(-1).UtcDateTime);
        IQueryable<CentralLogEvent> query = BuildStructuredFilterQuery(database, criteria.Filters).AsNoTracking()
            .Where(log => log.LogDateUtc >= firstDateUtc && log.LogDateUtc <= lastDateUtc &&
                          log.TimestampUtc >= criteria.Start && log.TimestampUtc < criteria.End);
        if (!String.IsNullOrWhiteSpace(serviceName)) query = query.Where(log => log.ServiceName == serviceName);
        if (tenantId.HasValue) query = query.Where(log => log.TenantId == tenantId);
        if (correlationId.HasValue) query = query.Where(log => log.CorrelationId == correlationId);
        return query;
    }

    private static String EncodeCursor(CentralLogEvent log)
    {
        Byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new LogCursor
        {
            TimestampUtc = log.TimestampUtc,
            EventId = log.EventId
        });
        return Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static Boolean TryDecodeCursor(String cursor, out LogCursor? position)
    {
        position = null;
        try
        {
            String base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            LogCursor? parsed = JsonSerializer.Deserialize<LogCursor>(Convert.FromBase64String(base64));
            if (parsed is null || parsed.TimestampUtc == default || parsed.EventId == Guid.Empty)
                return false;

            position = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class LogCursor
    {
        public DateTimeOffset TimestampUtc { get; set; }
        public Guid EventId { get; set; }

        public LogCursor() { }
    }

    private sealed record SearchCriteria(DateTimeOffset Start, DateTimeOffset End, StructuredLogFilter[] Filters);
}
