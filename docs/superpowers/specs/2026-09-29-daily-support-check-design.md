# Daily Support Check Script Design

## Purpose

Provide a reusable PowerShell template for a daily operational support check. The script will run a configurable set of independent checks, collect consistent results, build a human-readable report, and expose a transport seam for sending the report once the delivery mechanism is confirmed.

## Scope

The first version will be a template rather than a production-specific monitoring implementation. It will include safe example checks and clear extension points for adding real checks.

In scope:

- A single orchestration script under `ScheduledTasks/DailySupportCheck`.
- Independent check functions with a shared result contract.
- Continued execution when an individual check fails.
- Console summary plus JSON and HTML report files.
- Configurable output directory and check selection.
- A transport function that currently supports local output and can later be adapted for email, Teams, webhook, or another destination.
- Pester tests for orchestration, failure isolation, and report generation.
- README documentation covering extension and scheduling.

Out of scope:

- Choosing or configuring the final report transport.
- Implementing environment-specific health checks against production systems.
- Adding external module or package dependencies.

## Design

### Entry point and configuration

`Invoke-DailySupportCheck.ps1` will expose parameters for output path, optional check selection, and optional strict behavior. The default run will execute all registered checks. A check registry will make the execution order and available checks visible in one place.

### Check contract

Each check will be a PowerShell function accepting a context object and returning a result object with:

- `Name`: stable check name.
- `Status`: `Passed`, `Warning`, or `Failed`.
- `Summary`: concise operator-facing result.
- `Details`: optional structured or textual diagnostic information.
- `StartedAt`, `CompletedAt`, and `DurationMs`.
- `Error`: optional error text.

The runner will catch terminating and non-terminating check failures, convert them to `Failed` results, and continue with subsequent checks.

### Reporting

The report model will contain run metadata, overall status, counts by status, and the individual check results. JSON will preserve the structured model. HTML will provide a readable table and detail sections suitable for attaching to an email. Report generation will not change the check results.

The overall status will be:

- `Failed` if any check failed.
- `Warning` if no check failed but at least one check warned.
- `Passed` otherwise.

### Transport seam

`Send-SupportReport` will receive the completed report and generated file paths. For the initial template it will confirm local files are available and return a transport result. A future transport implementation can replace or extend this function without changing check or report code.

### Failure handling and exit behavior

The script will always attempt to write the report, including when checks fail. It will return a non-zero exit code when the overall status is `Failed`; warnings will remain successful by default so scheduled-task operators can choose their own alerting policy. Unexpected orchestration errors will be written to the error stream and return a non-zero exit code.

## Testing

Pester tests will verify:

1. A normal run aggregates registered checks and creates JSON/HTML output.
2. A failing check does not prevent later checks from running.
3. Overall status precedence is correct.
4. The generated report contains check names and statuses.
5. The local transport reports the generated files.

Tests will use temporary output directories and avoid external services.

## Extension path

To add a real support action, an operator will:

1. Add a function implementing the common result contract.
2. Add the function to the check registry.
3. Add a focused Pester test for its behavior.
4. Update the README with required configuration and operational assumptions.

