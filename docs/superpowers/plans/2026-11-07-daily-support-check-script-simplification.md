# Daily Support Check Script Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove repeated status-icon and HTTP endpoint configuration validation logic without changing script behavior.

**Architecture:** Keep the script standalone. Add one status-icon helper covering the current status aliases and one HTTP endpoint configuration helper used by the three sections that currently validate both `BaseUrl` and `TimeoutSeconds`.

**Tech Stack:** PowerShell.

**Spec:** `docs/superpowers/specs/2026-11-07-daily-support-check-script-simplification-design.md`

## Global Constraints

- Keep `Invoke-DailySupportCheck.ps1` as a single self-contained script.
- Preserve current validation order, messages, optional-section behavior, and disabled-section early returns.
- Preserve icon mappings: `Passed`/`Healthy` → green, `Warning`/`Degraded` → orange, `Failed`/`Unhealthy` → red, and all other values → white.
- Do not change any check's request, status calculation, fallback, report, or email behavior.
- Do not add or run tests.

## Review Focus

- A disabled optional endpoint section must retain the existing early return behavior.
- Invalid endpoint URI and non-positive timeout errors must preserve their current section-specific messages.
- Status values with and without aliases must render the same icon, including unknown values.

---

## Task 1: Extract repeated status and endpoint configuration helpers

**Files:** `ScheduledTasks/DailySupportCheck/Invoke-DailySupportCheck.ps1`

**Interfaces:** `Get-SupportStatusIcon([string] $Status) -> [string]`; `Assert-HttpEndpointConfiguration([string] $SectionName, [object] $Settings) -> void`.

- [x] Add `Get-SupportStatusIcon` with the approved mapping and replace only the three existing HTML status-icon switches.
- [x] Add `Assert-HttpEndpointConfiguration`; preserve the exact URI and timeout validation logic and error messages.
- [x] Replace the repeated validation blocks for `HealthMonitoring`, `SubscriptionService`, and `KurrentDbProjections` with helper calls while keeping their existing property checks, ordering, domain-specific validation, and early returns.
- [x] Parse the updated script with PowerShell's parser, review the diff for behavior changes, and run `git diff --check`; do not run tests.
