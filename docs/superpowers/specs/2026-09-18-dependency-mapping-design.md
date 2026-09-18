# Per-Service Dependency Mapping

## Status

Design approved in conversation; implementation begins after this specification is reviewed.

## Goal

Allow operators to map dependency names returned by a monitored service's ASP.NET health response to other monitored services, so dependency checks can link to the target service's detail page.

## User experience

- The service detail page contains a **Manage dependency links** action.
- The mapping page is scoped to one parent monitored service at `/services/{serviceId}/dependencies`.
- Each mapping shows the health-check dependency name and the target monitored service.
- Operators can add, edit, and remove mappings.
- The target service is selected from active monitored services and cannot be the parent service itself.
- The dependency detail table renders a link when a mapping exists; unmapped checks remain plain text.
- The mapping page displays a clear empty state and validation errors without losing unsaved form values.

## Data model

Add `ServiceDependencyLink`:

- `Id` (`Guid`) primary key.
- `MonitoredServiceId` (`Guid`) parent service whose health response contains the dependency name.
- `DependencyName` (`string`) case-insensitive health response entry name.
- `TargetMonitoredServiceId` (`Guid`) monitored service opened by the drill-down link.
- `CreatedAtUtc` and `UpdatedAtUtc` timestamps.

Constraints:

- Unique `(MonitoredServiceId, DependencyName)` so one dependency has at most one target per parent service.
- Foreign keys to monitored services use restrictive behavior to avoid silently deleting mappings.
- Archived target services are not offered for new mappings, but existing mappings remain readable and render as unavailable rather than breaking the page.

## Persistence and API boundary

- Extend the repository with list, upsert, and delete operations scoped by parent service ID.
- Keep registration API behavior unchanged; dependency links are dashboard-managed configuration.
- Use SQL Server through the existing EF Core `HealthMonitoringDbContext` and the existing startup schema approach.

## Detail-page behavior

- Load dependency links with the service detail model.
- Match health-check names case-insensitively after trimming whitespace.
- Resolve the mapped target by monitored-service ID.
- Render the dependency name as a link to `/services/{targetServiceId}` when the target is active.
- Show an `Unmapped` label or plain dependency name when no mapping exists.
- Preserve the existing status, duration, and description/exception display.

## Validation and failure handling

- Dependency name is required and trimmed.
- Target service is required, active, and different from the parent service.
- Duplicate dependency names for the same parent return a validation error rather than creating duplicate rows.
- Deleting a mapping only removes the link; it does not alter health history or either monitored service.

## Verification

- Unit tests cover case-insensitive matching, duplicate prevention, self-link rejection, and archived target handling.
- Repository tests cover persistence and uniqueness behavior.
- UI/API tests cover adding, editing, removing, and rendering a mapped dependency link.
- Build and the existing test suite must pass after implementation.

## Out of scope

- Automatic mapping by dependency name.
- Changing service registration payloads.
- Dependency topology graphs.
- Authentication and authorization.
