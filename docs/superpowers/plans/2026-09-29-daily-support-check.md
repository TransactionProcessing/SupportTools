# Daily Support Check Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a reusable PowerShell daily support-check template that runs independent checks, creates JSON/HTML reports, and leaves report delivery configurable.

**Architecture:** A single PowerShell entry point will contain the orchestration and template checks, with a common result contract and isolated report/transport functions. Pester tests will invoke the script in a temporary output directory and validate aggregation, failure isolation, status precedence, and generated artifacts.

**Tech Stack:** PowerShell 7-compatible scripting, Pester, JSON serialization, HTML generation, Windows Task Scheduler-compatible parameters.

**Spec:** `docs/superpowers/specs/2026-09-29-daily-support-check-design.md`

## Global Constraints

- The first version is a template, not a production-specific monitoring implementation.
- Do not add external module or package dependencies.
- Checks must return `Passed`, `Warning`, or `Failed` results with the shared fields defined in the spec.
- A failed check must not prevent later checks or report generation.
- The initial transport must only confirm local report files; final transport remains out of scope.

## Review Focus

- A check throws unexpectedly: the runner must record `Failed` and continue; covered by `Invoke-DailySupportCheck.Tests.ps1` failure-isolation test.
- Output directory does not exist: the script must create it; covered by report-generation test.
- A check returns a warning alongside a passing check: overall status must be `Warning`; covered by status-precedence test.
- Report content contains structured details or error text: JSON must preserve it and HTML must render it safely; covered by report-content test.
- No explicit check list is supplied: all registered checks must run in stable registry order; covered by default orchestration test.

---

### Task 1: Add the daily support-check script

**Files:**
- Create: `ScheduledTasks/DailySupportCheck/Invoke-DailySupportCheck.ps1`

**Interfaces:**
- Produces script parameters `OutputPath`, `CheckName`, and `PassThru`.
- Produces functions `New-CheckResult`, `Invoke-SupportCheck`, `Get-OverallStatus`, `New-SupportReport`, `Write-SupportReports`, `Send-SupportReport`, and `Invoke-DailySupportCheck`.
- The script's final output is a report object when `-PassThru` is supplied; otherwise it writes a concise console summary and returns an exit status through the script flow.

- [ ] **Step 1: Implement result construction and the three safe template checks**

Create `New-CheckResult` with the fields `Name`, `Status`, `Summary`, `Details`, `StartedAt`, `CompletedAt`, `DurationMs`, and `Error`. Add placeholder checks for PowerShell/runtime availability, output-directory writability, and template configuration readiness. Each check must return the shared result contract and be easy to replace with a real operational check.

- [ ] **Step 2: Implement isolated check execution and overall status calculation**

Implement `Invoke-SupportCheck` so exceptions become `Failed` results and do not escape into the registry loop. Implement `Get-OverallStatus` with precedence `Failed` > `Warning` > `Passed`. Register checks in a stable ordered array and filter it when `CheckName` is supplied.

- [ ] **Step 3: Implement report model and JSON/HTML writers**

Implement `New-SupportReport` with run metadata, overall status, status counts, and check results. Implement `Write-SupportReports` to create the output directory and write timestamped `.json` and `.html` files. HTML must HTML-encode check summaries, details, and error text.

- [ ] **Step 4: Implement local transport and script orchestration**

Implement `Send-SupportReport` to return a local transport result containing the generated paths. Implement `Invoke-DailySupportCheck` to run selected checks, always write reports, print a summary, and return the report object for `-PassThru`. Preserve report generation when checks fail. Keep warnings non-fatal by default and expose a clear place for future strict behavior.

- [ ] **Step 5: Run a syntax/import smoke check**

Run: `pwsh -NoProfile -Command ". ./ScheduledTasks/DailySupportCheck/Invoke-DailySupportCheck.ps1; Get-Command Invoke-DailySupportCheck"`

Expected: the command resolves successfully and no parser errors are emitted.

### Task 2: Add Pester coverage

**Files:**
- Create: `ScheduledTasks/DailySupportCheck/Tests/Invoke-DailySupportCheck.Tests.ps1`

**Interfaces:**
- Consumes the functions and script parameters from Task 1.
- Produces deterministic tests using temporary directories and no external services.

- [ ] **Step 1: Add the default orchestration test**

Invoke the function with a temporary output path and assert that all registered template checks run in registry order, the report is returned, and both JSON and HTML files exist.

- [ ] **Step 2: Add failure-isolation coverage**

Invoke `Invoke-SupportCheck` with a deliberately throwing test scriptblock followed by a passing scriptblock. Assert that the first result is `Failed` and the second still runs and is `Passed`.

- [ ] **Step 3: Add status precedence and content coverage**

Assert `Failed` beats `Warning` and `Passed`, and `Warning` beats `Passed`. Generate a report containing warning details and an error string, then assert the JSON preserves them and the HTML contains encoded content.

- [ ] **Step 4: Run the Pester test file**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path './ScheduledTasks/DailySupportCheck/Tests/Invoke-DailySupportCheck.Tests.ps1' -Output Detailed"`

Expected: all tests pass with no external-service requirements.

### Task 3: Document extension, scheduling, and transport

**Files:**
- Create: `ScheduledTasks/DailySupportCheck/README.md`

**Interfaces:**
- Documents the script's parameters, result contract, report locations, exit behavior, and the exact location of the transport seam.

- [ ] **Step 1: Document normal invocation and scheduling**

Include examples for an interactive run, selecting checks, and a Task Scheduler action using `pwsh -NoProfile -File`.

- [ ] **Step 2: Document adding a real check**

Show the required function shape, result fields, registry entry, and focused Pester test expectation.

- [ ] **Step 3: Document future transport integration**

Explain that `Send-SupportReport` currently confirms local files and can later be replaced with email, Teams, webhook, or another approved transport without changing check code.

- [ ] **Step 4: Run final validation**

Run the Pester test file and the PowerShell smoke check from Tasks 1 and 2. Expected: all tests pass, the script imports cleanly, and the working tree contains only the intended spec, plan, script, tests, and README changes.

