# Standalone ASP.NET Health Monitoring Dashboard

## Status

Design approved in conversation; implementation has not started.

## Goal

Build a standalone ASP.NET Core monitoring application that polls ASP.NET health-check endpoints, presents current and historical service health, stores health history in SQL Server, and provides an extensible foundation for alerting.

The first version is intended for a trusted internal network and does not include authentication or authorization.

## Product decisions

- Application type: standalone ASP.NET Core application.
- UI: Blazor Server.
- Persistence: SQL Server through EF Core.
- Architecture: modular monolith.
- Service configuration: managed through the dashboard and persisted in SQL Server.
- Service onboarding: idempotent REST self-registration API.
- Polling: configurable per service.
- Retention: configurable per service.
- Authentication: explicitly deferred from the first version.
- Alerting: extensible foundation; concrete notification channels are deferred.

## User experience

### Overview

The overview is optimized for operational scanning and follows the approved visual direction:

- Summary counters for Healthy, Degraded, Unhealthy, and open incidents.
- Service list showing service name, environment, endpoint, current status, response time, uptime, and last check.
- Clear status colors and text labels; status must not rely on color alone.
- Navigation from a service row to its detail page.
- Controls for adding and configuring monitored services.

### Service detail

The detail page shows:

- Current status and last-check information.
- Current incident duration, when applicable.
- Uptime for selectable time ranges.
- Average response time and response-time history.
- Status timeline for the selected range.
- Dependency/check results including status, duration, description, exception, and data.
- Raw ASP.NET health response JSON for troubleshooting.
- Service configuration and status-policy editing.

## Architecture

The application is a modular monolith with these boundaries:

1. **Service Registry**
   - Owns monitored-service configuration and lifecycle.
   - Supports dashboard editing and self-registration updates.

2. **Registration API**
   - Provides idempotent service self-registration.
   - Returns whether registration created, updated, or acknowledged an existing service.

3. **Polling Worker**
   - Schedules checks independently per enabled service.
   - Applies per-service interval and timeout settings.
   - Uses an HTTP client with cancellation and bounded execution.

4. **Health Normalizer**
   - Parses standard ASP.NET health responses.
   - Preserves the original response while deriving dashboard status.

5. **History Store**
   - Persists observations and dependency-level results.
   - Applies per-service retention cleanup.

6. **Incident Calculator**
   - Maintains current service snapshots.
   - Opens, extends, changes severity within, and closes incidents.

7. **Dashboard Query Layer**
   - Provides read models for overview cards, service details, timelines, metrics, and raw responses.

8. **Alerting Adapter**
   - Receives incident state changes through an interface.
   - Initially supports a no-op or logging implementation.

9. **Blazor Server UI**
   - Provides overview, detail, configuration, registration, and history screens.

The runtime flow is:

```text
Service startup
    -> idempotent registration API
    -> service configuration in SQL Server

Polling worker
    -> health endpoint
    -> normalize response
    -> store observation
    -> update current snapshot
    -> open/close/update incident
    -> dispatch alert event

Blazor UI
    -> query current state and history
    -> display overview, details, and configuration
```

## Service configuration

The service record will include:

- Stable `serviceId`.
- Display name.
- Environment.
- Optional group and description.
- Health URL.
- Enabled state.
- Polling interval.
- Request timeout.
- Retention period.
- Status policy.
- Registration metadata such as version, host, and last registration time.

Configuration is stored in SQL Server. JSON is used for REST request/response payloads and optional application-level defaults only.

Dashboard changes are authoritative for dashboard-owned fields. Self-registration may update mutable runtime metadata and endpoint information, but must not unintentionally overwrite dashboard-owned settings such as retention, alerting, or status policy unless the API contract explicitly allows that field.

## Status policy

The normalized dashboard status is configurable per service.

Default behavior:

- Healthy: all reported checks are healthy.
- Degraded: the endpoint responds and a non-critical dependency is non-healthy.
- Unhealthy: the endpoint is unreachable, times out, returns invalid health JSON, or a critical check fails.
- Unknown: no successful observation has ever been received.

Per-service policy supports:

- `unhealthyDependencyBehavior`, defaulting to `Degraded`.
- A list of `criticalChecks`.
- Optional critical tags if tags are available in the health response contract.
- `endpointFailureStatus`, defaulting to `Unhealthy`.

The raw ASP.NET aggregate status and the derived dashboard status are both stored.

## Data model

### MonitoredService

Stores service identity and configuration. `serviceId` is unique.

### HealthObservation

Stores one poll result:

- Service ID.
- Observation timestamp.
- Normalized status.
- ASP.NET aggregate status.
- HTTP status code.
- Response duration.
- Raw response JSON when available.
- Error or timeout details when the request fails.

### HealthCheckResult

Stores one dependency/check result associated with an observation:

- Check name.
- Status.
- Description.
- Duration.
- Exception/error details.
- Serialized `data` fields.

### ServiceIncident

Stores an outage/degradation interval:

- Service ID.
- Start and end timestamps.
- Current/open or recovered state.
- Incident severity.
- Duration.
- First and last observation references.

A Degraded/Unhealthy transition opens an incident. Continued non-healthy observations extend it. A severity change is recorded within the incident. A Healthy observation closes it and records recovery time.

### ServiceStatusSnapshot

Stores one current-state row per service for fast overview queries:

- Current normalized status.
- Last observation time.
- Last response duration.
- Current incident ID.
- Last error summary.

## REST API

### `POST /api/services/register`

Creates or updates a monitored service using `serviceId` as the idempotency key.

- New service: `201 Created`.
- Existing service acknowledged or updated: `200 OK`.
- Duplicate requests never create duplicate records.
- Mutable registration metadata may be updated.
- Protected dashboard-owned configuration must not be overwritten accidentally.

### `GET /api/services`

Lists registered services and current status summaries.

### `GET /api/services/{serviceId}`

Returns service configuration, status, and summary metrics.

### `PUT /api/services/{serviceId}`

Updates dashboard-managed configuration and status policy.

### `DELETE /api/services/{serviceId}`

Soft-deletes or archives a service while preserving its history.

## Polling and retention

The polling worker maintains an independent schedule for each enabled service. Polls use the configured timeout and record failures as observations rather than silently dropping them.

Retention cleanup runs per service using its configured retention period. Cleanup must preserve aggregate incident records and any references required for audit/history views, even when detailed observations are removed.

## Incidents and alerting

The incident calculator must handle:

- Healthy to Degraded.
- Healthy to Unhealthy.
- Continued non-healthy checks.
- Degraded to Unhealthy and Unhealthy to Degraded severity changes.
- Non-healthy to Healthy recovery.
- Timeouts and connection failures.
- Invalid health responses.

Alerting is represented by an interface such as `IAlertDispatcher`. The first version can use a no-op or logging dispatcher. Future adapters may support email, Teams, Slack, webhooks, or other channels.

Future per-service alert settings may include enabled state, trigger status, delay before alerting, repeat interval, recovery notification, and destination/channel.

## Failure handling

The monitor distinguishes HTTP failures, timeouts, connection/DNS failures, invalid JSON, valid Degraded/Unhealthy responses, and persistence failures.

A failed poll creates an observation with timestamp, elapsed time, and failure detail. It does not erase the last successful raw response. The current snapshot applies the service policy to determine whether the failure is Unhealthy or Unknown.

The monitoring application exposes its own health endpoint, including SQL Server connectivity, so it can be monitored externally.

## Verification strategy

- Unit tests for status normalization and configurable critical checks.
- Unit tests for idempotent registration and protected configuration fields.
- Unit tests for incident creation, continuation, severity changes, and recovery.
- SQL Server integration tests for persistence, queries, and retention.
- API tests for register, duplicate register, update, list, detail, and archive flows.
- Blazor component tests for overview, detail, dependency, and configuration views.
- Polling-worker tests using mocked HTTP and controlled time.
- Application health-check tests covering SQL Server availability.

## Deferred scope

- Authentication and authorization.
- Concrete email, Teams, Slack, or webhook delivery.
- Advanced alert routing and escalation policies.
- Distributed collectors or queue-based event ingestion.
- Multi-tenant isolation.
- High-volume telemetry aggregation beyond the SQL Server history model.
