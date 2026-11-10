using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CentralLogging.Api.Data;
using CentralLogging.Api.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CentralLogging.Api.Endpoints;

public static partial class LogEndpoints
{
    private const Int32 MaximumBatchSize = 500;
    private const Int32 MaximumPageSize = 500;
    private const String UnknownValue = "Unknown";
    private const String JsonNumberType = "number";
    private const String JsonBooleanType = "boolean";

    public static async Task<IResult> IngestAsync(IReadOnlyList<LogEventInput> events,
                                                   CentralLogsDbContext database,
                                                   CancellationToken cancellationToken)
    {
        if (events.Count is < 1 or > MaximumBatchSize)
            return Results.Problem($"A batch must contain between 1 and {MaximumBatchSize} events.", statusCode: StatusCodes.Status400BadRequest);

        String? validationError = ValidateEvents(events);
        if (validationError is not null)
            return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);

        IngestionCounts counts = await IngestBatchAsync(events, database, cancellationToken);
        return Results.Accepted(value: new { accepted = counts.Accepted, duplicates = counts.Duplicates });
    }

    public static async Task<IResult> UploadAsync(IFormFile? file,
                                                   CentralLogsDbContext database,
                                                   Microsoft.Extensions.Options.IOptions<LogIngestionOptions> options,
                                                   CancellationToken cancellationToken)
    {
        if (file is null)
            return Results.Problem("Choose a log file to upload.", statusCode: StatusCodes.Status400BadRequest);
        if (file.Length > options.Value.MaximumUploadBytes)
            return Results.Problem($"The uploaded file exceeds the {options.Value.MaximumUploadBytes} byte limit.",
                statusCode: StatusCodes.Status413PayloadTooLarge);

        Int32 accepted = 0;
        Int32 retainedLines = 0;
        Int32 duplicates = 0;
        DateTimeOffset uploadTimestamp = DateTimeOffset.UtcNow;
        List<UploadedLogEvent> batch = new(MaximumBatchSize);

        await using Stream stream = file.OpenReadStream();
        String? line;
        Int32 lineNumber = 0;
        BoundedLineReader lineReader = new(stream, options.Value.MaximumLineBytes);
        try
        {
            while ((line = await lineReader.ReadLineAsync(cancellationToken)) is not null)
            {
                lineNumber++;
                if (String.IsNullOrWhiteSpace(line))
                    continue;

                UploadedLogEvent uploadedEvent = ParseUploadLine(line, lineNumber, uploadTimestamp);
                batch.Add(uploadedEvent);
                if (batch.Count == MaximumBatchSize)
                {
                    IngestionCounts counts = await IngestBatchAsync(batch.Select(item => item.Input).ToArray(), database, cancellationToken);
                    database.ChangeTracker.Clear();
                    CountUploadResults(batch, counts, ref accepted, ref retainedLines);
                    duplicates += counts.Duplicates;
                    batch.Clear();
                }
            }
        }
        catch (UploadLimitExceededException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (batch.Count > 0)
        {
            IngestionCounts counts = await IngestBatchAsync(batch.Select(item => item.Input).ToArray(), database, cancellationToken);
            database.ChangeTracker.Clear();
            CountUploadResults(batch, counts, ref accepted, ref retainedLines);
            duplicates += counts.Duplicates;
        }

        return Results.Ok(new { accepted, retainedLines, duplicates });
    }

    private static void CountUploadResults(IEnumerable<UploadedLogEvent> batch,
                                            IngestionCounts counts,
                                            ref Int32 accepted,
                                            ref Int32 retainedLines)
    {
        HashSet<Guid> insertedIds = counts.InsertedIds;
        foreach (UploadedLogEvent item in batch)
        {
            if (!insertedIds.Remove(item.Input.EventId))
                continue;

            if (item.IsRetainedLine)
                retainedLines++;
            else
                accepted++;
        }
    }

    private sealed class BoundedLineReader(Stream stream, Int32 maximumLineBytes)
    {
        private readonly Byte[] Buffer = new Byte[8192];
        private Int32 Offset;
        private Int32 Count;
        private Boolean IsFirstLine = true;

        public async Task<String?> ReadLineAsync(CancellationToken cancellationToken)
        {
            using MemoryStream line = new(Math.Min(maximumLineBytes, 8192));
            while (true)
            {
                if (this.Offset == this.Count)
                {
                    this.Count = await stream.ReadAsync(this.Buffer, cancellationToken);
                    this.Offset = 0;
                    if (this.Count == 0)
                    {
                        if (line.Length == 0)
                            return null;
                        break;
                    }
                }

                Byte value = this.Buffer[this.Offset++];
                if (value == (Byte)'\n')
                    break;
                if (line.Length == maximumLineBytes)
                    throw new UploadLimitExceededException(maximumLineBytes);
                line.WriteByte(value);
            }

            Byte[] bytes = line.ToArray();
            Int32 length = bytes.Length > 0 && bytes[^1] == (Byte)'\r' ? bytes.Length - 1 : bytes.Length;
            String result = Encoding.UTF8.GetString(bytes, 0, length);
            if (this.IsFirstLine && result.StartsWith('\uFEFF'))
                result = result[1..];
            this.IsFirstLine = false;
            return result;
        }
    }

    private sealed class UploadLimitExceededException(Int32 maximumLineBytes)
        : Exception($"An uploaded line exceeds the {maximumLineBytes} byte limit.");

    private static UploadedLogEvent ParseUploadLine(String line, Int32 lineNumber, DateTimeOffset uploadTimestamp)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? ParseObjectUploadLine(document.RootElement, line, lineNumber, uploadTimestamp)
                : new UploadedLogEvent(CreateRawLineEvent(line, lineNumber, uploadTimestamp), true);
        }
        catch (JsonException)
        {
            return new UploadedLogEvent(CreateRawLineEvent(line, lineNumber, uploadTimestamp), true);
        }
    }

    private static UploadedLogEvent ParseObjectUploadLine(JsonElement root,
                                                           String line,
                                                           Int32 lineNumber,
                                                           DateTimeOffset uploadTimestamp)
    {
        UploadIdentity identity = ReadUploadIdentity(root);
        JsonElement severity = FindUploadSeverity(root);
        JsonElement service = FindUploadService(root);
        JsonElement message = FindUploadMessage(root);
        Boolean isIncomplete = identity.EventId is null ||
                               identity.TimestampUtc is null ||
                               String.IsNullOrWhiteSpace(GetNonEmptyScalarText(severity)) ||
                               String.IsNullOrWhiteSpace(GetNonEmptyScalarText(service)) ||
                               String.IsNullOrWhiteSpace(GetScalarText(message));
        LogEventInput input = new()
        {
            EventId = identity.EventId ?? CreateStableEventId(line, lineNumber),
            TimestampUtc = identity.TimestampUtc ?? uploadTimestamp,
            Severity = LimitLength(GetNonEmptyScalarText(severity) ?? UnknownValue, 16),
            ServiceName = LimitLength(GetNonEmptyScalarText(service) ?? UnknownValue, 200),
            CorrelationId = GetGuid(FindProperty(root, "correlationId")),
            TenantId = GetGuid(FindProperty(root, "tenantId")),
            MerchantId = GetGuid(FindProperty(root, "merchantId")),
            TraceId = LimitLength(GetScalarText(FindProperty(root, "traceId")), 32),
            SpanId = LimitLength(GetScalarText(FindProperty(root, "spanId")), 16),
            RequestId = LimitLength(GetScalarText(FindProperty(root, "requestId")), 200),
            Message = GetScalarText(message) ?? line,
            MessageTemplate = GetScalarText(FindProperty(root, "messageTemplate")),
            Exception = GetScalarText(FindProperty(root, "exception")),
            AdditionalProperties = ReadUploadProperties(root, line, identity.EventId.HasValue)
        };
        return new UploadedLogEvent(input, isIncomplete);
    }

    private readonly record struct UploadedLogEvent(LogEventInput Input, Boolean IsRetainedLine);

    private static UploadIdentity ReadUploadIdentity(JsonElement root)
    {
        Guid? eventId = GetGuid(FindProperty(root, "eventId"));
        if (eventId == Guid.Empty) eventId = null;
        JsonElement timestampElement = FindProperty(root, "timestampUtc");
        DateTimeOffset? timestamp = timestampElement.ValueKind == JsonValueKind.String &&
                                    timestampElement.TryGetDateTimeOffset(out DateTimeOffset parsedTimestamp)
            ? parsedTimestamp
            : null;
        return new UploadIdentity(eventId, timestamp);
    }

    private static JsonElement FindUploadSeverity(JsonElement root) =>
        FirstDefinedProperty(FindProperty(root, "severity"), FindProperty(root, "level"));

    private static JsonElement FindUploadService(JsonElement root) =>
        FirstDefinedProperty(FindProperty(root, "serviceName"),
                             FirstProperty(root, "loggerName", "sourceContext", "category", "fileName", "eventName", "serviceId"));

    private static JsonElement FindUploadMessage(JsonElement root)
    {
        JsonElement message = FindProperty(root, "message");
        return message.ValueKind == JsonValueKind.Undefined || IsEmptyString(message)
            ? FindProperty(root, "messageTemplate")
            : message;
    }

    private static JsonElement FirstDefinedProperty(JsonElement first, JsonElement second) =>
        first.ValueKind == JsonValueKind.Undefined ? second : first;

    private static Dictionary<String, JsonElement> ReadUploadProperties(JsonElement root, String line, Boolean hasEventId)
    {
        Dictionary<String, JsonElement> properties = new(StringComparer.OrdinalIgnoreCase);
        HashSet<String> mappedFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "eventId", "timestampUtc", "severity", "level", "serviceName", "loggerName", "sourceContext",
            "category", "fileName", "eventName", "serviceId", "correlationId", "tenantId", "merchantId",
            "traceId", "spanId", "requestId", "message", "messageTemplate", "exception"
        };
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!mappedFields.Contains(property.Name) && property.Name.Length <= 200)
                properties[property.Name] = property.Value.Clone();
        }

        if (!hasEventId)
        {
            JsonElement sourceEventId = FindProperty(root, "eventId");
            properties["sourceEventId"] = sourceEventId.ValueKind == JsonValueKind.Undefined
                ? JsonSerializer.SerializeToElement((String?)null)
                : sourceEventId.Clone();
        }
        properties["rawLog"] = JsonSerializer.SerializeToElement(line);
        return properties;
    }

    private readonly record struct UploadIdentity(Guid? EventId, DateTimeOffset? TimestampUtc);

    private static LogEventInput CreateRawLineEvent(String line, Int32 lineNumber, DateTimeOffset uploadTimestamp)
    {
        Dictionary<String, JsonElement> properties = new(StringComparer.OrdinalIgnoreCase)
        {
            ["rawLog"] = JsonSerializer.SerializeToElement(line)
        };
        return new LogEventInput
        {
            EventId = CreateStableEventId(line, lineNumber),
            TimestampUtc = uploadTimestamp,
            Severity = UnknownValue,
            ServiceName = UnknownValue,
            Message = line,
            AdditionalProperties = properties
        };
    }

    private static Guid CreateStableEventId(String line, Int32 lineNumber)
    {
        Byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{lineNumber}\n{line}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static JsonElement FindProperty(JsonElement root, String name)
    {
        foreach (JsonProperty property in root.EnumerateObject())
            if (String.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        return default;
    }

    private static JsonElement FirstProperty(JsonElement root, params String[] names)
    {
        foreach (String name in names)
        {
            JsonElement property = FindProperty(root, name);
            if (property.ValueKind != JsonValueKind.Undefined)
                return property;
        }
        return default;
    }

    private static String? GetScalarText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
        _ => null
    };

    private static String? GetNonEmptyScalarText(JsonElement value)
    {
        String? text = GetScalarText(value);
        return String.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static Guid? GetGuid(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.TryGetGuid(out Guid guid) ? guid : null;

    private static Boolean IsEmptyString(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && String.IsNullOrWhiteSpace(value.GetString());

    private static String? LimitLength(String? value, Int32 maximumLength) =>
        value is null || value.Length <= maximumLength ? value : value[..maximumLength];

    private static String? ValidateEvents(IReadOnlyList<LogEventInput> events)
    {
        if (events.Any(HasMissingRequiredFields))
            return "Every event must include eventId, timestampUtc, severity, serviceName, and message.";

        if (events.Any(HasFieldsExceedingSupportedLength))
            return "One or more event fields exceed the supported length.";

        if (events.Any(HasInvalidStructuredPropertyName))
            return "Structured property names cannot exceed 200 characters.";

        return null;
    }

    private static Boolean HasMissingRequiredFields(LogEventInput log) =>
        log.EventId == Guid.Empty || log.TimestampUtc == default ||
        String.IsNullOrWhiteSpace(log.Severity) || String.IsNullOrWhiteSpace(log.ServiceName) ||
        String.IsNullOrWhiteSpace(log.Message);

    private static Boolean HasFieldsExceedingSupportedLength(LogEventInput log) =>
        log.Severity!.Length > 16 || log.ServiceName!.Length > 200 ||
        log.TraceId?.Length > 32 || log.SpanId?.Length > 16 || log.RequestId?.Length > 200;

    private static Boolean HasInvalidStructuredPropertyName(LogEventInput log) =>
        log.AdditionalProperties.Keys.Any(name => name.Length > 200);

    private static async Task<IngestionCounts> IngestBatchAsync(IReadOnlyList<LogEventInput> events,
                                                                 CentralLogsDbContext database,
                                                                 CancellationToken cancellationToken)
    {
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction =
            await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        Guid[] requestedIds = events.Select(log => log.EventId).Distinct().ToArray();
        HashSet<Guid> existingIds = (await database.LogEvents.AsNoTracking()
                .Where(log => requestedIds.Contains(log.EventId))
                .Select(log => log.EventId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        HashSet<Guid> acceptedIds = new();
        List<CentralLogEvent> toInsert = new();
        List<LogEventInput> acceptedInputs = new();

        foreach (LogEventInput input in events)
        {
            if (existingIds.Contains(input.EventId) || !acceptedIds.Add(input.EventId))
                continue;

            NormalizeBodyProperties(input);

            toInsert.Add(new CentralLogEvent
            {
                EventId = input.EventId,
                TimestampUtc = input.TimestampUtc.ToUniversalTime(),
                Severity = input.Severity!,
                ServiceName = input.ServiceName!,
                CorrelationId = input.CorrelationId,
                TenantId = input.TenantId,
                MerchantId = input.MerchantId,
                TraceId = input.TraceId,
                SpanId = input.SpanId,
                RequestId = input.RequestId,
                Message = input.Message!,
                MessageTemplate = input.MessageTemplate,
                Exception = input.Exception,
                PropertiesJson = JsonSerializer.Serialize(input.AdditionalProperties)
            });
            acceptedInputs.Add(input);
        }

        await UpdateStructuredFieldCatalogAsync(database, acceptedInputs, cancellationToken);
        database.LogEvents.AddRange(toInsert);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new IngestionCounts(toInsert.Count, events.Count - toInsert.Count, acceptedIds);
    }

    private static void NormalizeBodyProperties(LogEventInput input)
    {
        foreach (String propertyName in input.AdditionalProperties.Keys.ToArray())
        {
            if (!String.Equals(propertyName, "requestBody", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(propertyName, "responseBody", StringComparison.OrdinalIgnoreCase))
                continue;

            JsonElement value = input.AdditionalProperties[propertyName];
            if (value.ValueKind != JsonValueKind.String)
                continue;

            JsonNode? body = ParseJsonObjectOrArray(value.GetString());
            if (body is null)
                continue;

            using JsonDocument normalizedBody = JsonDocument.Parse(NormalizeEmbeddedJson(body).ToJsonString());
            input.AdditionalProperties[propertyName] = normalizedBody.RootElement.Clone();
        }
    }

    private static JsonNode NormalizeEmbeddedJson(JsonNode node)
    {
        if (node is JsonObject objectNode) NormalizeObject(objectNode);
        else if (node is JsonArray arrayNode) NormalizeArray(arrayNode);

        return node;
    }

    private static void NormalizeObject(JsonObject objectNode)
    {
        foreach (String propertyName in objectNode.Select(property => property.Key).ToArray())
        {
            JsonNode? child = objectNode[propertyName];
            JsonNode? normalizedChild = NormalizeEmbeddedValue(child);
            if (!ReferenceEquals(child, normalizedChild))
                objectNode[propertyName] = normalizedChild;
        }
    }

    private static void NormalizeArray(JsonArray arrayNode)
    {
        for (Int32 index = 0; index < arrayNode.Count; index++)
        {
            JsonNode? child = arrayNode[index];
            JsonNode? normalizedChild = NormalizeEmbeddedValue(child);
            if (!ReferenceEquals(child, normalizedChild))
                arrayNode[index] = normalizedChild;
        }
    }

    private static JsonNode? NormalizeEmbeddedValue(JsonNode? child)
    {
        if (child is JsonValue valueNode && valueNode.TryGetValue(out String? textValue))
        {
            JsonNode? parsed = ParseJsonObjectOrArray(textValue);
            if (parsed is not null)
                return NormalizeEmbeddedJson(parsed);
        }

        if (child is not null)
            NormalizeEmbeddedJson(child);

        return child;
    }

    private static JsonNode? ParseJsonObjectOrArray(String? value)
    {
        if (String.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            JsonNode? node = JsonNode.Parse(value);
            return node is JsonObject or JsonArray ? node : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct IngestionCounts(Int32 Accepted, Int32 Duplicates, HashSet<Guid> InsertedIds);

    public static async Task<IResult> SearchAsync(CentralLogsDbContext database,
                                                   DateTimeOffset? fromUtc,
                                                   DateTimeOffset? toUtc,
                                                   String? serviceName,
                                                   Guid? tenantId,
                                                   Guid? correlationId,
                                                   String[]? filter,
                                                   Int32? take,
                                                   CancellationToken cancellationToken)
    {
        SearchCriteria? criteria = TryResolveSearch(new LogSearchRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Filter = filter
        }, requireExplicitRange: false, maximumRangeDays: null, out IResult? problem);
        if (criteria is null)
            return problem!;
        Int32 pageSize = Math.Clamp(take ?? 100, 1, MaximumPageSize);
        List<CentralLogEvent> results = await BuildEventQuery(database, criteria, serviceName, tenantId, correlationId)
            .OrderByDescending(log => log.TimestampUtc)
            .ThenByDescending(log => log.EventId)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return Results.Ok(results);
    }

    public static async Task<IResult> GetServicesAsync(CentralLogsDbContext database, CancellationToken cancellationToken)
    {
        String[] services = await database.LogEvents.AsNoTracking()
            .Select(log => log.ServiceName)
            .Distinct()
            .OrderBy(name => name)
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new ServicesResponse(services));
    }

    private static IQueryable<CentralLogEvent> BuildStructuredFilterQuery(CentralLogsDbContext database,
                                                                          IReadOnlyList<StructuredLogFilter> filters)
    {
        if (filters.Count == 0) return database.LogEvents;

        List<String> predicates = new(filters.Count);
        List<SqlParameter> parameters = new(filters.Count * 3);
        for (Int32 index = 0; index < filters.Count; index++)
        {
            StructuredLogFilter filter = filters[index];
            String fieldParameter = $"@filter{index}Field";
            String typeParameter = $"@filter{index}Type";
            String valueParameter = $"@filter{index}Value";
            Int32 jsonType = filter.Type switch { "string" => 1, JsonNumberType => 2, JsonBooleanType => 3, _ => throw new InvalidOperationException("Validated filter type expected.") };
            predicates.Add($"EXISTS (SELECT 1 FROM OPENJSON([PropertiesJson]) AS property WHERE property.[key] COLLATE Latin1_General_100_BIN2 = {fieldParameter} AND DATALENGTH(property.[key]) = DATALENGTH({fieldParameter}) AND property.[type] = {typeParameter} AND property.[value] COLLATE Latin1_General_100_BIN2 = {valueParameter} AND DATALENGTH(property.[value]) = DATALENGTH({valueParameter}))");
            parameters.Add(new SqlParameter(fieldParameter, System.Data.SqlDbType.NVarChar, 200) { Value = filter.Field });
            parameters.Add(new SqlParameter(typeParameter, System.Data.SqlDbType.Int) { Value = jsonType });
            parameters.Add(new SqlParameter(valueParameter, System.Data.SqlDbType.NVarChar, -1) { Value = filter.Value });
        }

        String sql = $"SELECT * FROM dbo.LogEvents WHERE {String.Join(" AND ", predicates)}";
        // The SQL structure uses only generated filter indexes; user values are exclusively SqlParameters.
        // nosemgrep: csharp_injection_rule-SQLInjection
        return database.LogEvents.FromSqlRaw(sql, parameters.Cast<Object>().ToArray());
    }

    private static String? NormalizeJsonNumber(String value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            return document.RootElement.ValueKind == JsonValueKind.Number ? document.RootElement.GetRawText() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<IResult> GetStructuredFieldsAsync(CentralLogsDbContext database,
                                                               String? serviceName,
                                                               CancellationToken cancellationToken)
    {
        if (String.IsNullOrWhiteSpace(serviceName))
            return Results.Problem("serviceName is required to discover structured fields.", statusCode: StatusCodes.Status400BadRequest);
        if (serviceName.Length > 200)
            return Results.Problem("serviceName cannot exceed 200 characters.", statusCode: StatusCodes.Status400BadRequest);

        List<ServiceStructuredField> rows = await database.ServiceStructuredFields.AsNoTracking()
            .Where(field => field.ServiceName == serviceName)
            .OrderBy(field => field.FieldName)
            .ThenBy(field => field.JsonType)
            .ToListAsync(cancellationToken);

        StructuredFieldInfo[] fields = rows
            .GroupBy(row => row.FieldName, StringComparer.Ordinal)
            .Select(group => new StructuredFieldInfo(group.Key,
                                                      group.Select(row => row.JsonType).Distinct(StringComparer.Ordinal).Order().ToArray(),
                                                      group.Sum(row => row.EventCount),
                                                      group.Min(row => row.FirstSeenUtc),
                                                      group.Max(row => row.LastSeenUtc)))
            .OrderBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Results.Ok(new StructuredFieldsResponse(serviceName, fields));
    }

    private static async Task UpdateStructuredFieldCatalogAsync(CentralLogsDbContext database,
                                                                IReadOnlyList<LogEventInput> acceptedEvents,
                                                                CancellationToken cancellationToken)
    {
        if (acceptedEvents.Count == 0) return;

        Dictionary<StructuredFieldKey, FieldObservation> observations = new(StructuredFieldKeyComparer.Instance);
        foreach (LogEventInput logEvent in acceptedEvents)
        {
            DateTimeOffset timestamp = logEvent.TimestampUtc.ToUniversalTime();
            foreach (KeyValuePair<String, JsonElement> property in logEvent.AdditionalProperties)
            {
                StructuredFieldKey key = new(logEvent.ServiceName!, property.Key, GetJsonType(property.Value.ValueKind));
                if (observations.TryGetValue(key, out FieldObservation? observation))
                {
                    observations[key] = observation with
                    {
                        EventCount = observation.EventCount + 1,
                        FirstSeenUtc = timestamp < observation.FirstSeenUtc ? timestamp : observation.FirstSeenUtc,
                        LastSeenUtc = timestamp > observation.LastSeenUtc ? timestamp : observation.LastSeenUtc
                    };
                }
                else
                {
                    observations.Add(key, new FieldObservation(1, timestamp, timestamp));
                }
            }
        }

        String[] serviceNames = observations.Keys.Select(key => key.ServiceName).Distinct(StringComparer.Ordinal).ToArray();
        List<ServiceStructuredField> existingFields = await database.ServiceStructuredFields
            .Where(field => serviceNames.Contains(field.ServiceName))
            .ToListAsync(cancellationToken);
        Dictionary<StructuredFieldKey, ServiceStructuredField> existingByKey = existingFields.ToDictionary(
            field => new StructuredFieldKey(field.ServiceName, field.FieldName, field.JsonType),
            StructuredFieldKeyComparer.Instance);

        foreach ((StructuredFieldKey key, FieldObservation observation) in observations)
        {
            if (existingByKey.TryGetValue(key, out ServiceStructuredField? field))
            {
                field.EventCount += observation.EventCount;
                if (observation.FirstSeenUtc < field.FirstSeenUtc) field.FirstSeenUtc = observation.FirstSeenUtc;
                if (observation.LastSeenUtc > field.LastSeenUtc) field.LastSeenUtc = observation.LastSeenUtc;
            }
            else
            {
                database.ServiceStructuredFields.Add(new ServiceStructuredField
                {
                    ServiceName = key.ServiceName,
                    FieldName = key.FieldName,
                    JsonType = key.JsonType,
                    EventCount = observation.EventCount,
                    FirstSeenUtc = observation.FirstSeenUtc,
                    LastSeenUtc = observation.LastSeenUtc
                });
            }
        }
    }

    private static String GetJsonType(JsonValueKind valueKind) => valueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.String => "string",
        JsonValueKind.Number => JsonNumberType,
        JsonValueKind.True or JsonValueKind.False => JsonBooleanType,
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        _ => "unknown"
    };

    private readonly record struct StructuredFieldKey(String ServiceName, String FieldName, String JsonType);

    private sealed class StructuredFieldKeyComparer : IEqualityComparer<StructuredFieldKey>
    {
        public static StructuredFieldKeyComparer Instance { get; } = new();

        public Boolean Equals(StructuredFieldKey left, StructuredFieldKey right) =>
            StringComparer.Ordinal.Equals(left.ServiceName, right.ServiceName) &&
            StringComparer.Ordinal.Equals(left.FieldName, right.FieldName) &&
            StringComparer.Ordinal.Equals(left.JsonType, right.JsonType);

        public Int32 GetHashCode(StructuredFieldKey key) => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(key.ServiceName),
            StringComparer.Ordinal.GetHashCode(key.FieldName),
            StringComparer.Ordinal.GetHashCode(key.JsonType));
    }

    private sealed record FieldObservation(Int64 EventCount, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);
}
