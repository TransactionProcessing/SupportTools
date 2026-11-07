# Daily Support Check script simplification

## Intent

Make repeated presentation and configuration-validation logic easier to maintain in `Invoke-DailySupportCheck.ps1` without changing report output, configuration behavior, or how the standalone script is deployed.

## Goals

- Replace the three duplicated status-to-icon switches in HTML report generation with one helper that preserves the current mappings: `Passed`/`Healthy` → green, `Warning`/`Degraded` → orange, `Failed`/`Unhealthy` → red, and all other values → white.
- Replace the repeated absolute-URI and positive-timeout validation for `HealthMonitoring`, `SubscriptionService`, and `KurrentDbProjections` with one helper.
- Preserve the current validation order, messages, optional-section behavior, and early return when a present section is disabled.
- Keep `Invoke-DailySupportCheck.ps1` as a single self-contained script.

## Non-goals

- Split the script into modules or change installation/sourcing requirements.
- Change any check's request, status calculation, fallback, report or email behavior.
- Rewrite the entire configuration validator as a table-driven framework.
- Change KurrentDB write-activity validation, which has no matching timeout requirement.

## Chosen approach

Add a small `Get-SupportStatusIcon` helper for the existing icon mappings and an `Assert-HttpEndpointConfiguration` helper that accepts the section name, base URL, and timeout. Call the endpoint helper from the three existing validation blocks while leaving their order, enable checks, optional settings, and early returns in place. Replace only the repeated icon switches with helper calls; leave HTML structure and text encoding as they are.

## Alternatives considered

1. **Extract the repeated helpers in the existing script (chosen).** Reduces duplicated logic with minimal behavioral and packaging risk.
2. **Split checks, reporting, and transports into separate files.** Could improve navigation further, but changes how callers load and deploy the script and exceeds this focused cleanup.
3. **Rewrite all validation as declarative metadata.** More compact, but risks changing validation order and disabled-section short-circuit behavior for little added value.

## Constraints and implementation calls

- Preserve current user-visible messages and configuration behavior byte-for-byte where applicable.
- Keep the current script API and deployment layout.
- The existing PR #843 is the target branch and should receive this cleanup as a follow-up commit.
- Do not add or run tests; use static inspection and syntax parsing if needed.
