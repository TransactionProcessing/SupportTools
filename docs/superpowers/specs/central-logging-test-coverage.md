# Central Logging Test Coverage Design

## Goal

Expand issue #845 coverage with durable API/database assertions and focused forwarder tests, all using Shouldly.

## Design

- Run the API against SQL Server in GitHub Actions and host it with `WebApplicationFactory<Program>` so tests cover routing, authentication, serialization, migrations, EF Core, and SQL Server-specific queries.
- Use an isolated integration database and serialize database-mutating API tests. Reset stored rows between cases.
- Test the forwarder against temporary log files and a deterministic fake HTTP handler, asserting posted events and persisted checkpoint offsets.
- Keep the existing unit-level validation and checkpoint tests. Avoid production changes unless a test reveals a necessary seam.

## Coverage

- Ingestion success, duplicate IDs within and across requests, durable event and structured-field outcomes, and validation limits.
- Upload malformed/incomplete lines, generated IDs, accepted/duplicate/invalid counts, duplicate re-uploads, and partial batch failure behavior.
- Search time, service, tenant, correlation, structured field type and exact-value filters, page limits, and invalid filter inputs.
- API key enforcement, readiness, and static UI access.
- Forwarder complete newline-delimited records, incomplete trailing lines, malformed records, truncation/rotation, retry after API errors, and checkpoint recovery.

## CI

Run the Central Logging test project on .NET 10 with a SQL Server service container and a dedicated integration-test connection string.
