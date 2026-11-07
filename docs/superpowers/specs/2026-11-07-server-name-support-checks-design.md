# Server-name support checks design

## Intent

Accept daily support-check reports for a server without requiring that server to be registered as a monitored service. A server name is the stable, unique identity for a report. This fixes the current coupling where the ingestion handler looks up `serverId` in the monitored-service registry before saving an otherwise independent server report.

## Goals

- Replace `POST /api/services/{serverId}/support-checks` with `POST /api/servers/{serverName}/support-checks`.
- Use the server name as the report identity in the API and persistence layer.
- Remove the old route without a compatibility alias.
- Update the Daily Support Check sender, its configuration examples, and its documentation to use the new route.
- Keep the Server Checks UI able to display the latest saved report.

## Non-goals

- Register servers as monitored services or require service registration.
- Add a server registry, server list, per-server history, or a UI for selecting among servers.
- Change report check fields, status calculation, or the UI's existing behavior of showing the latest report overall.

## Contract

The replacement endpoint is `POST /api/servers/{serverName}/support-checks`. The decoded path segment is the canonical server name. The JSON body contains `overallStatus` and `checks`; it no longer contains `serverName` or `serverId`. The server calculates `overallStatus` from the check statuses, as it does today. Check validation and the existing response behavior remain: `201 Created` for the first report for a server and `200 OK` for later reports.

The endpoint does not query `IHealthMonitoringRepository.GetServiceAsync`. It validates that the server name is non-empty and within the persistence limit, then creates or updates the report for that name. Trim surrounding whitespace before storing and matching the name, including in repository methods. A missing/blank name or invalid check data returns a validation error.

## Persistence and migration

Use `ServerName` as the unique key for `ServerStatus` upserts. Remove the obsolete `ServerId` property/column and update API, reporting, and UI models to use `ServerName` as the identity. Add an EF Core migration that removes the old unique index on `ServerId` and enforces uniqueness on `ServerName`, preserving existing report contents. The migration must detect duplicate existing server names and fail with an actionable error rather than silently discarding a report; operators must reconcile those rows before retrying.

Server names are unique and stable by contract. A renamed server is a new identity and will have no prior report under its new name.

The migration's `Down` operation must refuse automatic rollback when report rows exist, because the removed legacy `ServerId` values cannot be reconstructed from server names. Its error must instruct operators to restore a pre-migration backup or manually restore the old ID mapping before retrying.

## Daily Support Check sender

Replace `ServerStatusApi.ServerId` and the `{serverId}` path token with `ServerStatusApi.ServerName` and `{serverName}` in the endpoint path. Keep the current configuration behavior of using the configured name or falling back to the machine's `COMPUTERNAME`; do not use the shared `Unknown Server` fallback because it can merge reports from unrelated machines. If neither name is available, report the API submission as failed with a clear configuration error. Send only `overallStatus` and `checks` in the JSON body.

Update `daily-support-check.json`, `daily-support-check.local.example.json`, the README, and sender tests. Remove coverage and documentation for the old route and `ServerId` setting.

## UI behavior

The Server Checks page continues reading stored reports through `IDashboardQueryService`; it does not call the ingestion route. Update its display model to show the server name as the identity and remove the obsolete server ID line. It continues showing the latest report overall; per-server selection is outside this change.

## Approaches considered

1. **New server-name route with server name as the stored key (chosen).** `POST /api/servers/{serverName}/support-checks`; remove the old route and `ServerId` identity. This makes the API and persisted identity match the domain and avoids duplicate identity fields in the body.
2. **New collection route with the server name in the body.** `POST /api/servers/support-checks`; this avoids a path/body duplication but leaves the target identity less visible in the URL.
3. **Keep the old service route and remove only the service lookup.** This is the smallest implementation, but leaves a service-shaped URL for server-owned data and retains the misleading `serverId` concept. It was rejected.

## Constraints and implementation calls

- Existing report JSON and check validation remain compatible.
- No compatibility endpoint is retained; callers must update with the API.
- The storage migration preserves rows and fails visibly if current server names are duplicated.
- The Daily Support Check sender is the known in-repository caller and must change in the same implementation.
- The work is a small API and caller change with a focused persistence migration; the implementer may settle routine naming details consistent with surrounding code.
