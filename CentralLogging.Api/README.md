# Central logging API starter

This API is the first receiving and query component for the central logging design. It accepts batches of NLog JSON events, stores searchable fields in SQL Server, preserves additional structured properties as JSON, and provides log search plus service-scoped structured field discovery. A basic search UI is served from `/`. The Windows forwarder is in the separate `CentralLogging.Forwarder` project.

## Start locally

1. Provision the `CentralLogs` database and configure the connection string. The API applies pending EF Core migrations during startup, creating the schema or adopting tables from the former SQL scripts. Its SQL Server identity must have permission to create/alter the schema and migration history table. For production environments where the application identity should not have DDL permission, apply EF migrations through a separately controlled deployment step instead.
2. The API is configured for the local SQL Server instance (`Server=localhost`) with Windows Integrated Security in `appsettings.json`. If your instance uses another name, override `ConnectionStrings__CentralLogs`. Set `LogIngestion__ApiKey` to a secret value. In PowerShell, for example:

   ```powershell
   $env:ConnectionStrings__CentralLogs = "Server=YOUR-SQL-SERVER;Database=CentralLogs;Integrated Security=true;Encrypt=true;TrustServerCertificate=true"
   $env:LogIngestion__ApiKey = "replace-with-a-secret"
   dotnet run --project CentralLogging.Api
   ```

3. Requests to `/api/logs` must send the configured secret in the `X-Log-Api-Key` header. The browser UI prompts for this key at runtime, keeps it in memory only, and sends it with API requests.

Open `http://localhost:<port>/` to use the search UI. It provides time, service, tenant, correlation ID, and service-specific structured-field filters. `GET /health/ready` checks database connectivity. `POST /api/logs` accepts a JSON array of 1 to 500 events and returns the accepted and duplicate counts. Reposting an `eventId` is safe. `GET /api/logs/services` lists service names seen in stored events.

The UI can upload an existing newline-delimited JSON (`.json` or `.jsonl`) log file. Every nonblank line is retained, including malformed JSON and records missing fields. The API stores the original line in `rawLog`, fills unavailable required fields with safe fallbacks, and ingests in batches of up to 500. The response includes `accepted`, `duplicates`, and `invalidLines` (zero when persistence succeeds). Uploads use `POST /api/logs/upload` as `multipart/form-data` with a `file` field and the same `X-Log-Api-Key` header. If a later batch fails, earlier batches may already be stored; retrying the file is safe because event IDs are deduplicated. Search results are database-wide and can include previously ingested events; after an upload, run a new search to reload the results.

`GET /api/logs` supports `fromUtc`, `toUtc`, `serviceName`, `tenantId`, `correlationId`, `take`, and repeated `filter` query parameters. It defaults to the last 24 hours and caps results at 500. Each `filter` is URL-encoded JSON with `field`, `type`, and `value`; up to 10 exact-match filters are supported, and all must match. Supported types are `string`, `number`, and `boolean`. For example: `filter={"field":"amount","type":"number","value":"12.5"}`. String and boolean values are exact matches; number values match the exact JSON numeric representation, preserving distinctions in high-precision values (for example, `12.50` is distinct from `12.5`). The API checks the JSON property's name, scalar type, and value through SQL parameters.

`GET /api/logs/fields?serviceName=Payments` returns the structured fields ever observed for the selected service, with observed JSON types, event counts, and first/last event timestamps. The catalog is updated in the same SQL transaction as newly accepted events. Duplicate event IDs do not increase its counts. This endpoint supplies the UI’s service-specific filter picker; array and object fields are shown in event details but are not available as exact-match filter controls.

Example response:

```json
{
  "serviceName": "Payments",
  "fields": [
    {
      "name": "transactionId",
      "types": ["string"],
      "eventCount": 420,
      "firstSeenUtc": "2026-10-22T13:10:00Z",
      "lastSeenUtc": "2026-10-23T11:45:00Z"
    },
    {
      "name": "amount",
      "types": ["number"],
      "eventCount": 180,
      "firstSeenUtc": "2026-10-22T13:10:00Z",
      "lastSeenUtc": "2026-10-23T11:45:00Z"
    }
  ]
}
```

The endpoint is intentionally protected by a configured API key from the first version. Use HTTPS, and replace or extend this with your chosen service authentication before exposing it beyond the trusted network.
