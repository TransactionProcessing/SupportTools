# Per-Service Dependency Mapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with review checkpoints.

**Goal:** Store per-service dependency links and render mapped health checks as drill-down links in the Blazor dashboard.

**Architecture:** Add a `ServiceDependencyLink` entity with a unique parent-service/dependency-name key and a target monitored-service foreign key. Extend the existing repository and dashboard query model, add a scoped Blazor management page, and render links from the existing service detail dependency table.

**Tech Stack:** ASP.NET Core, Blazor Server, EF Core, SQL Server, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-18-dependency-mapping-design.md`

## Global Constraints

- Keep authentication out of scope.
- Store mappings in SQL Server.
- Keep the self-registration API unchanged.
- Do not delete health history when a mapping is deleted.
- Use the existing repository, Razor, and test conventions.

---

### Task 1: Add the dependency-link domain and persistence model

**Files:**
- Create: `HealthMonitoring/Domain/ServiceDependencyLink.cs`
- Modify: `HealthMonitoring/Persistence/HealthMonitoringDbContext.cs`
- Modify: `HealthMonitoring/Persistence/HealthMonitoringSchemaInitializer.cs`
- Test: `HealthMonitoring.Tests/Persistence/DependencyMappingTests.cs`

- [ ] Write a failing model test asserting the entity has parent and target service keys and a unique parent/name index.
- [ ] Run the focused test and confirm it fails because the entity is not yet mapped.
- [ ] Add the entity and EF model configuration with restrictive foreign keys and the unique index.
- [ ] Add idempotent startup SQL to create the table when `EnsureCreated` is operating against an existing database.
- [ ] Run the focused test and confirm it passes.

### Task 2: Add repository operations and mapping read model

**Files:**
- Modify: `HealthMonitoring/Persistence/IHealthMonitoringRepository.cs`
- Modify: `HealthMonitoring/Persistence/HealthMonitoringRepository.cs`
- Modify: `HealthMonitoring/Reporting/DashboardModels.cs`
- Modify: `HealthMonitoring/Reporting/DashboardQueryService.cs`
- Test: `HealthMonitoring.Tests/Persistence/DependencyMappingTests.cs`

- [ ] Add list, upsert, and delete repository contracts scoped by parent service ID.
- [ ] Test duplicate dependency names are rejected and mappings are scoped to the parent service.
- [ ] Implement normalized trimmed name handling and target-service validation.
- [ ] Return resolved link targets in `ServiceDetailModel`.
- [ ] Run the persistence-focused tests.

### Task 3: Add dependency mapping HTTP endpoints

**Files:**
- Create: `HealthMonitoring/Api/DependencyMappingEndpoints.cs`
- Create: `HealthMonitoring/Api/DependencyMappingContracts.cs`
- Modify: `HealthMonitoring/Program.cs`
- Test: `HealthMonitoring.Tests/Api/DependencyMappingEndpointTests.cs`

- [ ] Define GET, POST/PUT, and DELETE routes scoped under `/api/services/{serviceId}/dependency-links`.
- [ ] Add validation for required names, active target services, self-links, and duplicate names.
- [ ] Test successful create/update/delete and validation failures.
- [ ] Map the endpoints in `Program.cs`.

### Task 4: Build the Blazor dependency mapping screen

**Files:**
- Create: `HealthMonitoring/Components/Pages/DependencyLinks.razor`
- Modify: `HealthMonitoring/Components/Pages/ServiceDetail.razor`
- Modify: `HealthMonitoring/wwwroot/app.css`

- [ ] Add the `/services/{ServiceId}/dependencies` route with parent-service context.
- [ ] Render existing mappings, active target-service choices, add/edit/delete controls, validation errors, and empty state.
- [ ] Add a `Manage dependency links` action to the service detail page.
- [ ] Match the existing dashboard panel, form, button, and table styles.
- [ ] Build the application and verify the route compiles.

### Task 5: Render drill-down links and verify the feature

**Files:**
- Modify: `HealthMonitoring/Components/Pages/ServiceDetail.razor`
- Modify: `HealthMonitoring.Tests/Monitoring/HealthEndpointClientTests.cs`
- Create or modify: `HealthMonitoring.Tests/Reporting/DashboardQueryServiceTests.cs`

- [ ] Add a case-insensitive mapping test for `Eventstore` and whitespace normalization.
- [ ] Render mapped dependency names as links to `/services/{targetServiceId}` and leave unmapped checks as plain text.
- [ ] Verify the supplied ASP.NET payload still produces both dependency rows.
- [ ] Run the complete test suite and a clean application build.
- [ ] Review the final diff for unrelated changes.
