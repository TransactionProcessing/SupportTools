# Central Logging Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add meaningful API/database and forwarder test coverage for issue #845.

**Architecture:** Keep API integration and forwarder unit coverage in `CentralLogging.Tests`. Run the API through `WebApplicationFactory<Program>` against a dedicated SQL Server database. Exercise the forwarder with temporary files and a fake `HttpMessageHandler`.

**Tech Stack:** .NET 10, xUnit, Shouldly, EF Core SQL Server, `Microsoft.AspNetCore.Mvc.Testing`, GitHub Actions SQL Server service container.

**Spec:** `docs/superpowers/specs/central-logging-test-coverage.md` and GitHub issue #845.

## Global Constraints

- Use Shouldly for every assertion; do not use `Assert.*`.
- Use real SQL Server for database-backed API and query behavior.
- Assert durable rows, response counts/statuses, posted payloads, and checkpoint offsets.
- Keep file-based tests isolated with temporary directories and deterministic fake HTTP responses.

## Review Focus

- Duplicate events must not inflate stored rows or structured-field counts; cover duplicate IDs in one request and a replay.
- Upload IDs derived from line position must remain stable on identical re-upload; cover malformed and incomplete input while checking raw lines and counts.
- Later upload batch failure may leave earlier batches committed; prove the first batch survives and document the failed batch outcome.
- Structured filters must enforce JSON scalar type and exact value, including a number representation boundary.
- Forwarder offsets advance only after a successful send; cover retry and recovery across file rename or truncation.

### Task 1: SQL Server integration host

**Files:**
- Modify: `CentralLogging.Tests/CentralLogging.Tests.csproj`
- Create: `CentralLogging.Tests/Api/SqlServerApiFixture.cs`
- Create: `CentralLogging.Tests/Api/CentralLoggingApiCollection.cs`
- Modify: `.github/workflows/pullrequest.yml`

**Interfaces:**
- Fixture provides one `HttpClient` from `WebApplicationFactory<Program>`, configured with `ConnectionStrings:CentralLogs` and `LogIngestion:ApiKey`.
- Fixture applies migrations on startup and clears `ServiceStructuredFields` and `LogEvents` before each test.
- CI sets the fixture's connection string to the SQL Server service container.

- [x] Add WebApplicationFactory dependency and a SQL Server service to the Central Logging workflow job.
- [x] Add a collection fixture that starts the real API and serializes integration tests.
- [x] Add a readiness integration test that verifies host startup, migrations, and database readiness.

### Task 2: Ingestion and upload behavior

**Files:**
- Create: `CentralLogging.Tests/Api/IngestionIntegrationTests.cs`
- Create: `CentralLogging.Tests/Api/UploadIntegrationTests.cs`

**Interfaces:**
- Use the fixture's `HttpClient` and test API key; query persisted outcomes through a scoped `CentralLogsDbContext` helper.

- [x] Test accepted event persistence and duplicate ID counts for duplicates in one batch and a replay.
- [x] Test structured-field catalog counts remain durable and unchanged for duplicate events.
- [x] Test malformed and incomplete upload lines are retained with raw text and safe fallbacks.
- [x] Test accepted, duplicate, and invalid line counts plus deterministic generated IDs across re-upload.
- [x] Test a later batch failure leaves the earlier batch durable and records the documented failure behavior.

### Task 3: Search and access behavior

**Files:**
- Create: `CentralLogging.Tests/Api/SearchIntegrationTests.cs`
- Create: `CentralLogging.Tests/Api/AuthorizationIntegrationTests.cs`

**Interfaces:**
- Seed logs through the public ingestion route so search tests cross the API and database boundary.

- [x] Test time, service, tenant, and correlation filters and result ordering/limits.
- [x] Test exact structured string, number, and boolean matches, including a type mismatch and precise numeric text.
- [x] Test invalid filters return 400 without querying stored data.
- [x] Test missing and incorrect API keys are rejected while readiness and the static UI follow their intended access model.

### Task 4: Forwarder file and checkpoint behavior

**Files:**
- Create: `CentralLogging.Tests/Forwarder/LogForwarderWorkerTests.cs`
- Modify: `CentralLogging.Tests/ForwarderCheckpointStoreTests.cs`

**Interfaces:**
- Start `LogForwarderWorker` with temporary source/checkpoint paths and an `IHttpClientFactory` backed by a fake `HttpMessageHandler`.
- Observe requests, response attempts, and checkpoint JSON as durable outcomes.

- [x] Test complete newline-delimited events are posted, malformed records are skipped, and an incomplete trailing line remains uncheckpointed.
- [x] Test non-success API responses leave the offset unchanged and the next successful poll retries the event.
- [x] Test checkpoint recovery after restart and file rename, and restarting from the beginning after truncation/replacement.

### Task 5: Full verification

**Files:**
- Modify: `README.md` if the test command or SQL Server setup needs documentation.

- [ ] Run `dotnet test CentralLogging.sln --configuration Release` with the SQL Server test connection available. Locally, 11 tests passed and 11 SQL Server cases skipped because no connection is configured.
- [x] Confirm no `Assert.*` usage remains, run `git diff --check`, and inspect test results for skipped cases.
