# Server-Name Support Checks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the service-keyed support-check endpoint with a server-name-keyed endpoint and update storage, sender, and UI identity accordingly.

**Architecture:** `ServerName` becomes the unique identity in `ServerStatus`, and `POST /api/servers/{serverName}/support-checks` upserts reports directly without consulting monitored services. The Daily Support Check sender calls that route; the existing Server Checks page continues reading the latest persisted report.

**Tech Stack:** ASP.NET Core minimal APIs, EF Core with SQL Server, C#, PowerShell, JSON configuration.

**Spec:** `docs/superpowers/specs/2026-11-07-server-name-support-checks-design.md`

## Global Constraints

- Server names are unique and stable identifiers; a renamed server is a new identity.
- Remove `POST /api/services/{serverId}/support-checks` with no compatibility alias.
- Report ingestion must not require a matching monitored service.
- Preserve report contents during migration and fail clearly if existing server names collide.
- Keep the UI's existing latest-report-overall behavior; do not add a server registry or selector.
- Do not add or run tests; update existing test fixtures/assertions only as needed to keep them aligned and compilable.

## Review Focus

- Duplicate existing names must stop migration with an actionable error, without deleting reports.
- Rolling back after reports exist must fail clearly instead of inventing replacement legacy IDs.
- A valid report must save for a server name absent from the monitored-service registry.
- URL-encoded names with spaces must reach the intended server identity after route decoding.
- Missing or whitespace-only configured/runtime server names must not be written under a shared fallback identity.
- The Sender must not include the removed `serverId` or duplicate `serverName` in the JSON body.

---

## File Map

- `HealthMonitoring/HealthMonitoring/Api/ServerStatusEndpoints.cs` — replace the route and remove service existence lookup; build response location from the new route.
- `HealthMonitoring/HealthMonitoring/Api/ServerStatusContracts.cs` — remove the body server name and obsolete response `ServerId`; validate the route server name.
- `HealthMonitoring/HealthMonitoring/Domain/ServerStatus.cs` — remove `ServerId`; retain `ServerName` as the identity.
- `HealthMonitoring/HealthMonitoring/Persistence/IHealthMonitoringRepository.cs` and `HealthMonitoring/HealthMonitoring/Persistence/HealthMonitoringRepository.cs` — query and upsert status by server name.
- `HealthMonitoring/HealthMonitoring/Persistence/HealthMonitoringDbContext.cs`, `HealthMonitoring/HealthMonitoring/Migrations/HealthMonitoringDbContextModelSnapshot.cs`, and a new migration — unique index on `ServerName`, remove `ServerId`, reject duplicate names before applying the index.
- `HealthMonitoring/HealthMonitoring/Reporting/ServerStatusDashboardModels.cs` (contains both dashboard model and mapper) and `HealthMonitoring/HealthMonitoring/Components/Pages/ServerChecks.razor` — remove obsolete ID display and use server name as identity.
- Existing related tests under `HealthMonitoring/HealthMonitoring.Tests/Api`, `Persistence`, and `Reporting` — update references/assertions to the new model and contract without adding test cases or running tests.
- `ScheduledTasks/DailySupportCheck/Invoke-DailySupportCheck.ps1` — construct the new route from configured `ServerName` or `COMPUTERNAME`, and remove identity fields from the body.
- `ScheduledTasks/DailySupportCheck/daily-support-check.json`, `ScheduledTasks/DailySupportCheck/daily-support-check.local.example.json`, `ScheduledTasks/DailySupportCheck/README.md`, and `ScheduledTasks/DailySupportCheck/Tests/Invoke-DailySupportCheck.Tests.ps1` — rename configuration path token/settings and update existing fixtures/assertions.

## Task 1: Make the API and persisted report identity the server name

**Files:** API contracts and endpoints, `ServerStatus`, repository interface/implementation, DbContext, migration/snapshot, and existing related C# test references.

**Interfaces:** `GetServerStatusAsync(string serverName, CancellationToken)` and `UpsertServerStatusAsync(ServerStatus status, CancellationToken)` both key by the trimmed server name. The request body has `OverallStatus` and `Checks`; `ServerName` comes from the route.

- [x] Replace the mapped route with `POST /api/servers/{serverName}/support-checks`; remove the service lookup; keep 201-on-create and 200-on-update behavior; set the Created location to the new path.
- [x] Move server-name validation to the route value, trim before persistence and lookup, and retain all current check validation and overall-status calculation.
- [x] Remove `ServerId` from `ServerStatus`; make repository get/upsert methods use `ServerName` and normalize names.
- [x] Change the EF model to require `ServerName` and apply a unique index to it. Add a migration that first throws an actionable SQL error if duplicate server names exist, then drops the old `ServerId` index/column and creates the unique `ServerName` index. Make `Down` refuse rollback with existing report rows because the old ID mapping cannot be reconstructed. Update the model snapshot.
- [x] Update existing C# fixtures and assertions for removed `ServerId` and body `ServerName` members.
- [x] Build `HealthMonitoring/HealthMonitoring/HealthMonitoring.csproj` and `HealthMonitoring/HealthMonitoring.Tests/HealthMonitoring.Tests.csproj` to check production and fixture compilation; do not run tests.

## Task 2: Update Daily Support Check delivery and configuration

**Files:** PowerShell sender, JSON config examples, README, existing sender tests.

**Interfaces:** `ServerStatusApi.ServerName` is the configured stable name; when empty, use `COMPUTERNAME`. Build `SupportChecksPath` by URL-encoding the name into `{serverName}`. JSON body contains only `overallStatus` and `checks`.

- [x] Replace `ServerId` and `{serverId}` use with `ServerName` and `{serverName}`; retain configured-name then computer-name fallback, but fail delivery clearly if both are blank (remove `Unknown Server`).
- [x] Update both JSON config files and README examples/instructions to the new path, setting, and body contract.
- [x] Update existing PowerShell sender fixtures/assertions for URL encoding and payload shape; implement clear delivery failure when both name sources are blank, without adding or running tests.
- [x] Parse the script and updated sender fixture with PowerShell's parser and validate both JSON configuration files.

## Task 3: Align dashboard models and display

**Files:** server status dashboard models/mapper, Server Checks page, and existing related C# fixture/assertion.

- [x] Remove the obsolete `ServerId` member from dashboard models/mapper; display only `ServerName` as the identity.
- [x] Preserve the page's current latest-report-overall query and existing check rendering.
- [x] Build the HealthMonitoring app and test project to check compilation; do not run tests.

## Task 4: Final consistency review

- [x] Search the repository for stale caller routes/settings and server-status uses of `ServerId`; remove stale references while leaving historical migration references intact.
- [x] Review `git diff --check` and the complete diff for migration safety, route/body consistency, and adherence to the spec.
- [x] Report build/parse/JSON-validation outcomes and any existing-name collision remediation required before deploying the migration.
