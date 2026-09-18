# Operational Support Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reframe the existing HealthWatch Blazor application as an Operational Support application with Health Monitoring as its first active section and future support sections visible but disabled.

**Architecture:** Keep the existing routes, APIs, database, monitoring workers, and page behavior unchanged. Replace the standalone layout navigation with a grouped application shell, update page branding, and add disabled future-section navigation items without creating routes for them.

**Tech Stack:** ASP.NET Core Blazor Server, Razor components, existing CSS, xUnit test suite, .NET 10.

**Spec:** `docs/superpowers/specs/2026-09-18-operational-support-shell-design.md`

## Global Constraints

- Health Monitoring remains the only active section.
- Existing UI routes remain unchanged: `/`, `/services/new`, `/services/{serviceId}`, `/services/{serviceId}/dependencies`, and `/initialise`.
- Future navigation items must not navigate or create placeholder routes.
- No API, database, client-library, domain, or monitoring-worker changes.
- No authentication or authorization changes.

---

### Task 1: Replace the standalone layout with the Operational Support shell

**Files:**
- Modify: `HealthMonitoring/Components/Layout/MainLayout.razor`
- Modify: `HealthMonitoring/wwwroot/app.css`

**Interfaces:**
- Consumes: Existing Blazor `LayoutComponentBase`, `NavLink`, and current page routes.
- Produces: Operational Support branding, grouped Health Monitoring navigation, and non-interactive future-section items.

- [ ] **Step 1: Add the failing markup expectation**

Add a focused test only if the existing test project can render Razor layouts without adding a new test framework. The expectation must verify that the layout contains the text `Operational Support`, an active `Health Monitoring` navigation item, and the future labels `Incidents`, `Service Catalogue`, `Runbooks`, `Configuration`, and `Alerting`.

If the current test project cannot render this component directly, record the verification through the host smoke/build checks in Task 4 instead of adding a new UI test dependency.

- [ ] **Step 2: Implement the navigation shell**

Update `MainLayout.razor` to render:

```razor
<div class="shell">
    <aside class="sidebar">
        <div class="brand">Operational Support<small>Service reliability workspace</small></div>
        <div class="nav-section-label">Workspace</div>
        <nav>
            <NavLink href="/" Match="NavLinkMatch.All">Health Monitoring</NavLink>
            <span class="nav-future" aria-disabled="true">Incidents <span>Soon</span></span>
            <span class="nav-future" aria-disabled="true">Service Catalogue <span>Soon</span></span>
            <span class="nav-future" aria-disabled="true">Runbooks <span>Soon</span></span>
        </nav>
        <div class="nav-section-label">Administration</div>
        <nav>
            <span class="nav-future" aria-disabled="true">Configuration <span>Soon</span></span>
            <span class="nav-future" aria-disabled="true">Alerting <span>Soon</span></span>
        </nav>
        <div class="sidebar-note">Operational Support<br />Health Monitoring</div>
    </aside>
    <main class="main-content">@Body</main>
</div>
```

Keep `NavLink` only for the active Health Monitoring route. Use spans for future items so they cannot be activated or navigate.

- [ ] **Step 3: Add shell styles**

Extend `app.css` with styles for `.brand small`, `.nav-section-label`, `.nav-future`, and the `Soon` badge. Preserve the existing `.shell`, `.sidebar`, `nav`, `.main-content`, and responsive rules unless a selector must be adjusted to support the grouped navigation. Keep text labels visible below the existing 900px breakpoint.

- [ ] **Step 4: Run the focused verification**

Run:

```powershell
dotnet build HealthMonitoring/HealthMonitoring.csproj --no-restore
```

Expected: build succeeds with zero errors.

### Task 2: Update page-level Operational Support branding

**Files:**
- Modify: `HealthMonitoring/Components/Pages/Overview.razor`
- Modify: `HealthMonitoring/Components/Pages/ServiceRegister.razor`
- Modify: `HealthMonitoring/Components/Pages/ServiceDetail.razor`
- Modify: `HealthMonitoring/Components/Pages/DependencyLinks.razor`
- Modify: `HealthMonitoring/Components/Pages/Initialise.razor`

**Interfaces:**
- Consumes: Existing page routes and page content.
- Produces: Consistent document titles and breadcrumbs/copy using Operational Support and Health Monitoring terminology.

- [ ] **Step 1: Replace document titles**

Use titles with the following forms:

```text
Operational Support · Health Monitoring
Operational Support · Register service
Operational Support · Service detail
Operational Support · Dependency links
Operational Support · Initialise Health Monitoring
```

- [ ] **Step 2: Update overview context**

Change the overview eyebrow to `OPERATIONAL SUPPORT · HEALTH MONITORING`, retain the current service metrics, and keep the register-service action and existing `/services/new` route unchanged.

- [ ] **Step 3: Update setup and configuration copy**

Use `Initialise Health Monitoring` in the initialisation page heading and keep its existing backend behavior. Update service registration, detail, and dependency pages to use Operational Support branding without changing API calls, forms, or route parameters.

- [ ] **Step 4: Run the focused verification**

Run:

```powershell
dotnet build HealthMonitoring/HealthMonitoring.csproj --no-restore
```

Expected: build succeeds with zero errors.

### Task 3: Add route and future-section regression coverage

**Files:**
- Modify or create: `HealthMonitoring.Tests/Components/MainLayoutTests.cs` only if direct component testing is already supported.
- Modify: `HealthMonitoring.Tests/HostSmokeTests.cs` only if existing smoke-test patterns can verify page routes.

**Interfaces:**
- Consumes: Existing application routes and layout markup.
- Produces: Regression coverage that active Health Monitoring navigation remains available and future items are not links.

- [ ] **Step 1: Inspect the existing test host**

Use the existing test project references and test helpers. Do not add a browser-testing framework or a new UI test dependency solely for this shell change.

- [ ] **Step 2: Add the smallest viable assertions**

Verify, using the existing supported test approach, that:

```text
Operational Support is present.
Health Monitoring remains an active navigation route.
Incidents, Service Catalogue, Runbooks, Configuration, and Alerting are displayed as non-link future items.
```

- [ ] **Step 3: Run the focused tests**

Run:

```powershell
dotnet test HealthMonitoring.Tests/HealthMonitoring.Tests.csproj --no-restore --filter "FullyQualifiedName~HostSmokeTests|FullyQualifiedName~MainLayoutTests"
```

Expected: all selected tests pass. If `MainLayoutTests` is not created because the project lacks a supported renderer, use the build and broader regression test as the verification instead.

### Task 4: Full verification and review

**Files:**
- Inspect: all files changed by Tasks 1–3.

**Interfaces:**
- Consumes: Completed shell and branding changes.
- Produces: Verified implementation with no API, database, or monitoring regressions.

- [ ] **Step 1: Run the complete applicable test suite**

Run:

```powershell
dotnet test HealthMonitoring.Tests/HealthMonitoring.Tests.csproj --no-restore --filter "FullyQualifiedName!~ConfigurationTests"
```

Expected: all applicable tests pass.

- [ ] **Step 2: Verify the application build**

Run:

```powershell
dotnet build HealthMonitoring/HealthMonitoring.csproj --no-restore
```

Expected: build succeeds with zero warnings and zero errors.

- [ ] **Step 3: Review the diff**

Run:

```powershell
git diff --check
git diff -- HealthMonitoring/Components/Layout/MainLayout.razor HealthMonitoring/Components/Pages/Overview.razor HealthMonitoring/Components/Pages/ServiceRegister.razor HealthMonitoring/Components/Pages/ServiceDetail.razor HealthMonitoring/Components/Pages/DependencyLinks.razor HealthMonitoring/Components/Pages/Initialise.razor HealthMonitoring/wwwroot/app.css
```

Confirm that only presentation/navigation files changed and that future-section items cannot navigate.
