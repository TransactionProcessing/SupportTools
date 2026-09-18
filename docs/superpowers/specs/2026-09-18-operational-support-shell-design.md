# Operational Support Application Shell Design

**Status:** Design approved for review

## Goal

Present the existing HealthWatch functionality as the first section of a broader standalone Operational Support application, while making the navigation ready for future support capabilities.

## Scope

This change is a presentation and navigation restructuring only. It does not add incident workflows, service catalogue data, runbooks, alert delivery, authentication, or new persistence.

The first release will expose:

- Operational Support application branding.
- Health Monitoring as the active workspace section.
- Existing overview, service registration, service detail, dependency mapping, and first-run initialisation flows unchanged in behavior.
- Future navigation entries for Incidents, Service Catalogue, Runbooks, Configuration, and Alerting, clearly marked as coming soon and non-interactive.

## User experience

The main layout will use a dark left navigation rail and the existing light content area.

The navigation hierarchy is:

```text
Operational Support
├── Workspace
│   ├── Health Monitoring       active
│   ├── Incidents               coming soon
│   ├── Service Catalogue       coming soon
│   └── Runbooks                coming soon
└── Administration
    ├── Configuration           coming soon
    └── Alerting                coming soon
```

Health Monitoring remains the only active section. Its existing navigation entries will be grouped beneath it without changing their routes:

- Overview: `/`
- Register service: `/services/new`
- Service detail: `/services/{serviceId}`
- Dependency links: `/services/{serviceId}/dependencies`
- Initialisation: `/initialise`

The overview page will identify itself as `Operational Support · Health Monitoring` and retain the current health metrics and service list. Page titles will use `Operational Support` branding rather than `HealthWatch`.

Future entries will be rendered as disabled-looking navigation items with a `Soon` badge. They must not navigate or create placeholder routes, avoiding false functionality while making the intended application roadmap visible.

## Components and files

- Modify `HealthMonitoring/Components/Layout/MainLayout.razor` to implement the application shell, grouped navigation, branding, and future-section placeholders.
- Modify `HealthMonitoring/Components/Pages/Overview.razor` to use Operational Support / Health Monitoring copy.
- Modify `HealthMonitoring/Components/Pages/ServiceRegister.razor`, `ServiceDetail.razor`, `DependencyLinks.razor`, and `Initialise.razor` to use consistent Operational Support page titles where they currently mention HealthWatch.
- Modify `HealthMonitoring/wwwroot/app.css` for grouped navigation, disabled future links, section labels, and responsive behavior while preserving existing component styles.
- Add `HealthMonitoring.Tests/Components/MainLayoutTests.cs` only if the existing test setup can render the layout without introducing a new test framework; otherwise verify the rendered markup through the existing host smoke/build tests.

No API, database, client-library, domain, or monitoring-worker changes are required.

## Interaction and accessibility

- Active Health Monitoring navigation must use the existing `NavLink` behavior so the current route is visibly selected.
- Future items must be non-links or have `aria-disabled="true"` and must not be keyboard-activatable as navigation targets.
- Text labels must remain visible without relying on icons.
- Existing responsive layout behavior must continue to work below the current 900px breakpoint.

## Verification

1. Build `HealthMonitoring/HealthMonitoring.csproj`.
2. Run the HealthMonitoring test suite, excluding only the existing environment-specific `ConfigurationTests` if it still requires the default output layout.
3. Confirm the existing routes still render and the Health Monitoring navigation remains active for overview, registration, service detail, dependency links, and initialisation.
4. Confirm future navigation entries are visible but do not navigate.
5. Run `git diff --check`.

## Out of scope

- Incident history screens or acknowledgement.
- External alert providers.
- Service ownership and catalogue records.
- Runbook storage or execution.
- Authentication and authorization.
- Environment switching or tenant-specific navigation.
