# Standalone Health Monitoring Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone .NET 10 Blazor Server monitoring application backed by SQL Server that registers services, polls ASP.NET health endpoints, stores normalized health history, calculates incidents, and presents operational dashboards.

**Architecture:** Create a new `HealthMonitoring` modular-monolith solution in this repository. Keep domain/status logic, persistence, polling, REST endpoints, and Blazor UI in focused folders with interfaces between them. Use EF Core SQL Server for configuration/history and a hosted worker for per-service polling.

**Tech Stack:** .NET 10, ASP.NET Core Blazor Server, EF Core 10 SQL Server, xUnit 2.9, `Microsoft.NET.Test.Sdk`, SQL Server integration tests where available.

**Spec:** `docs/superpowers/specs/2026-09-18-health-monitoring-dashboard-design.md`

## Global Constraints

- Standalone ASP.NET Core application.
- Blazor Server UI.
- SQL Server persistence through EF Core.
- Per-service polling interval and retention.
- Idempotent registration by stable `serviceId`.
- No authentication or authorization in the first version.
- Default non-critical dependency failure is Degraded; critical checks can make a service Unhealthy.
- Preserve raw ASP.NET responses and dependency-level details.
- Failed polls are persisted as observations and do not erase the last successful raw response.
- Concrete notification channels are deferred; alerting uses an interface and logging/no-op implementation.

---

### Task 1: Create the standalone solution and test harness

**Files:**
- Create: `HealthMonitoring/HealthMonitoring.sln`
- Create: `HealthMonitoring/HealthMonitoring.csproj`
- Create: `HealthMonitoring.Tests/HealthMonitoring.Tests.csproj`
- Create: `HealthMonitoring.Tests/Usings.cs`
- Create: `HealthMonitoring/appsettings.json`
- Create: `HealthMonitoring/appsettings.Development.json`
- Create: `HealthMonitoring/Program.cs`
- Modify: `README.md`

**Interfaces:**
- Produces a buildable ASP.NET Core host and an xUnit test project that later tasks reference.

- [ ] **Step 1: Write a host smoke test**

Create `HealthMonitoring.Tests/HostSmokeTests.cs` with a test that asserts the new project assembly can be loaded and that the test project references the application project.

- [ ] **Step 2: Run the test to verify the scaffold is absent**

Run `dotnet test HealthMonitoring.Tests/HealthMonitoring.Tests.csproj --no-restore`.
Expected: fail because the new project files do not exist.

- [ ] **Step 3: Create the project files**

Use `Microsoft.NET.Sdk.Web`, target `net10.0`, enable nullable and implicit usings, and reference `Microsoft.EntityFrameworkCore.SqlServer` version `10.0.12`, `Microsoft.EntityFrameworkCore.Design` version `10.0.12` with private assets, and `Microsoft.AspNetCore.Components.Web` through the shared framework. The test project targets `net10.0`, references the application project, `Microsoft.NET.Test.Sdk` `17.12.0`, `xunit` `2.9.2`, and `xunit.runner.visualstudio` `2.8.2`.

Add a minimal `Program.cs` that creates a web application, maps a root placeholder endpoint, and runs. Add SQL Server configuration with a development connection string placeholder using `ConnectionStrings:HealthMonitoring` without embedding credentials.

- [ ] **Step 4: Run the smoke test and build**

Run `dotnet test HealthMonitoring.Tests/HealthMonitoring.Tests.csproj` and `dotnet build HealthMonitoring/HealthMonitoring.csproj`.
Expected: PASS and a clean build.

- [ ] **Step 5: Update repository documentation**

Add the new application to the root README with local build/run commands and the required `ConnectionStrings:HealthMonitoring` setting.

- [ ] **Step 6: Commit the scaffold**

Run `git add HealthMonitoring HealthMonitoring.Tests README.md` and commit with `feat: scaffold health monitoring application`.

### Task 2: Add domain models and status normalization

**Files:**
- Create: `HealthMonitoring/Domain/HealthStatus.cs`
- Create: `HealthMonitoring/Domain/StatusPolicy.cs`
- Create: `HealthMonitoring/Domain/MonitoredService.cs`
- Create: `HealthMonitoring/Domain/HealthObservation.cs`
- Create: `HealthMonitoring/Domain/HealthCheckResult.cs`
- Create: `HealthMonitoring/Domain/ServiceIncident.cs`
- Create: `HealthMonitoring/Domain/ServiceStatusSnapshot.cs`
- Create: `HealthMonitoring/Domain/HealthResponseModels.cs`
- Create: `HealthMonitoring/Domain/IHealthStatusNormalizer.cs`
- Create: `HealthMonitoring/Domain/HealthStatusNormalizer.cs`
- Test: `HealthMonitoring.Tests/Domain/HealthStatusNormalizerTests.cs`

**Interfaces:**
- `HealthStatusNormalizer.Normalize(HealthResponseEnvelope response, HttpStatusCode? httpStatusCode, TimeSpan duration)` returns `NormalizedHealthResult` containing normalized status, aggregate status, error, and check results.
- `StatusPolicy` contains `UnhealthyDependencyBehavior`, `CriticalChecks`, `CriticalTags`, and `EndpointFailureStatus`.

- [ ] **Step 1: Write failing normalizer tests**

Cover: all checks Healthy => Healthy; non-critical Unhealthy check with a responding endpoint => Degraded by default; named critical check failure => Unhealthy; HTTP/request failure => configured endpoint-failure status; malformed response => Unhealthy with error; empty/no successful response => Unknown at snapshot initialization.

- [ ] **Step 2: Run the focused tests**

Run `dotnet test HealthMonitoring.Tests --filter FullyQualifiedName~HealthStatusNormalizerTests`.
Expected: fail because domain types and normalizer do not exist.

- [ ] **Step 3: Implement the domain records and normalizer**

Model ASP.NET health JSON with `status` and `entries` containing `status`, `description`, `duration`, `exception`, and `data`. Match critical checks case-insensitively. Preserve unrecognized data as `JsonElement` or serialized JSON. Keep the normalizer independent of EF and HTTP clients.

- [ ] **Step 4: Run focused and full tests**

Run the focused filter, then `dotnet test HealthMonitoring.Tests`.
Expected: PASS.

- [ ] **Step 5: Commit the domain slice**

Run `git add HealthMonitoring/Domain HealthMonitoring.Tests/Domain` and commit with `feat: add configurable health status normalization`.

### Task 3: Implement EF Core persistence and migrations

**Files:**
- Create: `HealthMonitoring/Persistence/HealthMonitoringDbContext.cs`
- Create: `HealthMonitoring/Persistence/EntityConfigurations.cs`
- Create: `HealthMonitoring/Persistence/HealthMonitoringRepository.cs`
- Create: `HealthMonitoring/Persistence/IHealthMonitoringRepository.cs`
- Create: `HealthMonitoring/Persistence/RetentionCleanupService.cs`
- Create: `HealthMonitoring/Migrations/` generated migration files
- Create: `HealthMonitoring.Tests/Persistence/RepositoryTests.cs`
- Create: `HealthMonitoring.Tests/Persistence/SqlServerTestDatabase.cs`
- Modify: `HealthMonitoring/Program.cs`

**Interfaces:**
- `IHealthMonitoringRepository` supports service upsert/get/list/archive, observation append, current snapshot upsert, incident open/update/close, history queries, and retention cleanup.
- `HealthMonitoringDbContext` exposes `MonitoredServices`, `HealthObservations`, `HealthCheckResults`, `ServiceIncidents`, and `ServiceStatusSnapshots`.

- [ ] **Step 1: Write repository tests**

Test unique `serviceId` behavior, observation plus dependency persistence, current snapshot replacement, incident persistence, archive behavior, and queries ordered newest first. Use a SQL Server test connection from `HealthMonitoring__TestConnectionString`; if unavailable, the tests must be clearly skipped rather than silently using SQLite because SQL Server behavior is required.

- [ ] **Step 2: Run tests to verify they fail**

Run `dotnet test HealthMonitoring.Tests --filter FullyQualifiedName~RepositoryTests`.
Expected: fail because the context/repository do not exist.

- [ ] **Step 3: Implement the EF model and repository**

Use SQL Server-compatible keys, indexes, row timestamps, JSON stored as `nvarchar(max)`, unique service identity, indexes on service/timestamp and incident state, and soft archive fields. Use UTC timestamps consistently. Configure cascade behavior so deleting a service is not required for history preservation.

- [ ] **Step 4: Add and apply the initial migration**

Run `dotnet ef migrations add InitialHealthMonitoring --project HealthMonitoring/HealthMonitoring.csproj --startup-project HealthMonitoring/HealthMonitoring.csproj --output-dir Migrations` and apply it only when a configured SQL Server connection is available.

- [ ] **Step 5: Run persistence tests and build**

Run the repository test filter and `dotnet build HealthMonitoring/HealthMonitoring.csproj`.
Expected: PASS with SQL Server integration tests executed when configured.

- [ ] **Step 6: Commit persistence**

Run `git add HealthMonitoring/Persistence HealthMonitoring/Migrations HealthMonitoring.Tests/Persistence HealthMonitoring/Program.cs` and commit with `feat: add sql server health monitoring persistence`.

### Task 4: Add idempotent registration and configuration APIs

**Files:**
- Create: `HealthMonitoring/Api/ServiceRegistrationContracts.cs`
- Create: `HealthMonitoring/Api/ServiceRegistrationEndpoints.cs`
- Create: `HealthMonitoring/Api/ServiceQueryEndpoints.cs`
- Create: `HealthMonitoring/Api/ServiceConfigurationValidator.cs`
- Test: `HealthMonitoring.Tests/Api/ServiceRegistrationApiTests.cs`
- Modify: `HealthMonitoring/Program.cs`

**Interfaces:**
- `POST /api/services/register` accepts `serviceId`, name, environment, health URL, interval, timeout, retention, policy, and runtime metadata.
- `GET /api/services`, `GET /api/services/{serviceId}`, `PUT /api/services/{serviceId}`, and `DELETE /api/services/{serviceId}` provide configuration and summaries.

- [ ] **Step 1: Write API tests**

Cover new registration returns `201`, repeated registration returns `200` and leaves one service, mutable metadata updates, dashboard-owned policy/retention is protected unless explicitly supplied by the dashboard API, invalid URLs/intervals are rejected, list/detail work, and delete archives rather than removing history.

- [ ] **Step 2: Run API tests to verify they fail**

Run `dotnet test HealthMonitoring.Tests --filter FullyQualifiedName~ServiceRegistrationApiTests`.
Expected: fail because endpoints and contracts do not exist.

- [ ] **Step 3: Implement contracts, validation, and endpoints**

Use minimal APIs with typed request/response records. Enforce stable non-empty `serviceId`, absolute HTTP/HTTPS health URLs, positive interval/timeout/retention values, and bounded timeout values. Make the database unique constraint the final duplicate guard and translate duplicate races into an acknowledged/upserted success response.

- [ ] **Step 4: Run API tests and build**

Run the focused tests, then `dotnet test HealthMonitoring.Tests` and `dotnet build HealthMonitoring/HealthMonitoring.csproj`.
Expected: PASS.

- [ ] **Step 5: Commit the API slice**

Run `git add HealthMonitoring/Api HealthMonitoring.Tests/Api HealthMonitoring/Program.cs` and commit with `feat: add idempotent service registration api`.

### Task 5: Implement polling, normalization persistence, and incident calculation

**Files:**
- Create: `HealthMonitoring/Monitoring/IHealthEndpointClient.cs`
- Create: `HealthMonitoring/Monitoring/HealthEndpointClient.cs`
- Create: `HealthMonitoring/Monitoring/IIncidentCalculator.cs`
- Create: `HealthMonitoring/Monitoring/IncidentCalculator.cs`
- Create: `HealthMonitoring/Monitoring/HealthPollingWorker.cs`
- Create: `HealthMonitoring/Monitoring/Alerting.cs`
- Test: `HealthMonitoring.Tests/Monitoring/IncidentCalculatorTests.cs`
- Test: `HealthMonitoring.Tests/Monitoring/HealthEndpointClientTests.cs`
- Test: `HealthMonitoring.Tests/Monitoring/HealthPollingWorkerTests.cs`
- Modify: `HealthMonitoring/Program.cs`

**Interfaces:**
- `IHealthEndpointClient.CheckAsync(MonitoredService service, CancellationToken cancellationToken)` returns an observation candidate containing HTTP result, parsed response, raw JSON, duration, and failure details.
- `IIncidentCalculator.ApplyAsync(HealthObservation observation, CancellationToken cancellationToken)` updates snapshot and incident state.
- `IAlertDispatcher.DispatchAsync(AlertEvent alertEvent, CancellationToken cancellationToken)` receives incident transitions.

- [ ] **Step 1: Write incident tests**

Cover Healthy->Degraded opens an incident, continued Degraded keeps one incident, Degraded->Unhealthy changes severity without opening a second incident, Unhealthy->Healthy closes and records duration, and failed polls create observations with error details.

- [ ] **Step 2: Run incident tests to verify they fail**

Run `dotnet test HealthMonitoring.Tests --filter FullyQualifiedName~IncidentCalculatorTests`.
Expected: fail because the calculator does not exist.

- [ ] **Step 3: Implement incident calculation**

Load the current snapshot, compare normalized status, create/update/close one incident, update first/last observation references, and persist the new snapshot in one repository operation. Use an injected clock abstraction so duration tests are deterministic.

- [ ] **Step 4: Write HTTP client and worker tests**

Test valid ASP.NET JSON, non-success HTTP status with valid JSON, timeout, connection exception, malformed JSON, and cancellation. Test that the worker schedules enabled services using each service interval and does not poll archived/disabled services.

- [ ] **Step 5: Run the new tests to verify they fail**

Run `dotnet test HealthMonitoring.Tests --filter FullyQualifiedName~Monitoring`.
Expected: fail because the HTTP client and worker do not exist.

- [ ] **Step 6: Implement client, worker, and alerting adapter**

Use `IHttpClientFactory`, per-request timeout cancellation, UTC timestamps, the status normalizer, repository persistence, and a logging/no-op alert dispatcher. Re-read enabled services between scheduling cycles so dashboard changes take effect without restarting.

- [ ] **Step 7: Run monitoring tests and build**

Run the monitoring test filter, all tests, and build. Expected: PASS.

- [ ] **Step 8: Commit monitoring**

Run `git add HealthMonitoring/Monitoring HealthMonitoring.Tests/Monitoring HealthMonitoring/Program.cs` and commit with `feat: add health polling and incident tracking`.

### Task 6: Add dashboard query services and Blazor Server UI

**Files:**
- Create: `HealthMonitoring/Reporting/DashboardModels.cs`
- Create: `HealthMonitoring/Reporting/DashboardQueryService.cs`
- Create: `HealthMonitoring/Components/App.razor`
- Create: `HealthMonitoring/Components/Routes.razor`
- Create: `HealthMonitoring/Components/Layout/MainLayout.razor`
- Create: `HealthMonitoring/Components/Pages/Overview.razor`
- Create: `HealthMonitoring/Components/Pages/ServiceDetail.razor`
- Create: `HealthMonitoring/Components/Pages/ServiceEdit.razor`
- Create: `HealthMonitoring/Components/Pages/ServiceRegister.razor`
- Create: `HealthMonitoring/wwwroot/app.css`
- Test: `HealthMonitoring.Tests/Reporting/DashboardQueryServiceTests.cs`
- Modify: `HealthMonitoring/Program.cs`

**Interfaces:**
- `IDashboardQueryService.GetOverviewAsync(...)` returns counters and service rows.
- `IDashboardQueryService.GetServiceDetailAsync(serviceId, range, ...)` returns current status, uptime, response metrics, timeline, incidents, checks, and raw response.

- [ ] **Step 1: Write query-service tests**

Cover overview counts, current snapshot selection, uptime percentage, response average, timeline ordering, incident duration, dependency result retrieval, and missing service behavior.

- [ ] **Step 2: Run query tests to verify they fail**

Run `dotnet test HealthMonitoring.Tests --filter FullyQualifiedName~DashboardQueryServiceTests`.
Expected: fail because query models/service do not exist.

- [ ] **Step 3: Implement query models and service**

Query snapshots for the overview, calculate uptime from persisted observations for the selected window, calculate average response only from successful observations, and return dependency details and raw JSON for the selected/latest observation.

- [ ] **Step 4: Implement the Blazor pages**

Build the approved visual direction: overview counters, service table, detail metrics, status timeline, dependency table, raw JSON disclosure, and configuration forms. Use text plus color for statuses, responsive layout, and explicit empty/error states. Use normal Blazor navigation and `IDashboardQueryService`; do not duplicate persistence logic in components.

- [ ] **Step 5: Run tests and build**

Run all tests and build the application. Expected: PASS.

- [ ] **Step 6: Commit the UI slice**

Run `git add HealthMonitoring/Reporting HealthMonitoring/Components HealthMonitoring/wwwroot HealthMonitoring.Tests/Reporting HealthMonitoring/Program.cs` and commit with `feat: add health monitoring dashboard ui`.

### Task 7: Add retention cleanup, application health, and deployment documentation

**Files:**
- Create: `HealthMonitoring/Monitoring/RetentionCleanupWorker.cs`
- Create: `HealthMonitoring/Health/MonitoringHealthChecks.cs`
- Create: `HealthMonitoring.Tests/Health/MonitoringHealthCheckTests.cs`
- Create: `HealthMonitoring/Dockerfile`
- Create: `HealthMonitoring/docker-compose.sqlserver.yml`
- Modify: `HealthMonitoring/Program.cs`
- Modify: `HealthMonitoring/appsettings.json`
- Modify: `README.md`

- [ ] **Step 1: Write cleanup and application-health tests**

Test that cleanup uses each service retention period and preserves incidents, and that the application health check reports SQL Server failure as Unhealthy.

- [ ] **Step 2: Run tests to verify they fail**

Run `dotnet test HealthMonitoring.Tests --filter FullyQualifiedName~Health`.
Expected: fail because cleanup worker and health check do not exist.

- [ ] **Step 3: Implement cleanup and application health**

Run cleanup as a hosted service on a bounded cadence, delete only detailed observations/check results older than each service retention period, and map SQL Server connectivity to the application health endpoint.

- [ ] **Step 4: Add deployment files and documentation**

Document SQL Server connection setup, migrations, local development, registration request examples, and Docker Compose usage without committing credentials. Add a Dockerfile that runs the published application.

- [ ] **Step 5: Run full verification**

Run `dotnet test HealthMonitoring.Tests`, `dotnet build HealthMonitoring/HealthMonitoring.csproj`, and `git diff --check`. Start the app against a configured development SQL Server and verify the overview, registration endpoint, health endpoint, and one poll cycle.

- [ ] **Step 6: Commit the operational slice**

Run `git add HealthMonitoring README.md` and commit with `feat: add monitoring retention and deployment support`.

## Plan self-review

- Spec coverage: architecture, UI, SQL Server persistence, service registration, idempotency, per-service polling/retention, status policy, incidents, raw responses, dependency details, failure handling, alerting abstraction, application health, and tests are covered by Tasks 1-7.
- Placeholder scan: no implementation step depends on an unspecified function or file; deferred alert channels and authentication are explicitly scoped out rather than left as implementation placeholders.
- Type consistency: `IHealthStatusNormalizer`, `IHealthMonitoringRepository`, `IHealthEndpointClient`, `IIncidentCalculator`, `IAlertDispatcher`, and `IDashboardQueryService` are defined before the tasks that consume them.
- Repository fit: the new app is isolated under `HealthMonitoring` and does not modify existing service runtime code.
