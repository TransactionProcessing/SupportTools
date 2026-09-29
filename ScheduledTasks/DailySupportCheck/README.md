# Daily Support Check

`Invoke-DailySupportCheck.ps1` is a PowerShell template for a daily operational support run. It executes independent checks, writes a structured JSON report and a readable HTML report, and leaves report delivery behind a replaceable transport function.

## Run it interactively

From the repository root:

```powershell
pwsh -NoProfile -File .\ScheduledTasks\DailySupportCheck\Invoke-DailySupportCheck.ps1
```

Choose an output directory or a subset of checks:

```powershell
pwsh -NoProfile -File .\ScheduledTasks\DailySupportCheck\Invoke-DailySupportCheck.ps1 `
    -OutputPath 'C:\SupportReports\Daily' `
    -CheckName 'PowerShell Runtime', 'Report Output Directory'
```

Use `-PassThru` when calling the script from another PowerShell script and you need the report object. Use `-Strict` when warnings should produce a non-zero process exit code. Failed checks always produce a non-zero exit code.

The default template checks are:

1. `PowerShell Runtime`
2. `Report Output Directory`
3. `Template Configuration`

Replace or extend these with checks that are meaningful for the target environment.

## Output

Each run writes timestamped files to the selected output directory:

- `.json`: structured run metadata, overall status, counts, and check results.
- `.html`: an operator-friendly report suitable for attaching to a message.

Each check returns:

```text
Name, Status, Summary, Details, StartedAt, CompletedAt, DurationMs, Error
```

`Status` is one of `Passed`, `Warning`, or `Failed`. Overall status uses the same precedence: `Failed`, then `Warning`, then `Passed`.

## Add a real check

Add a function that accepts the shared context and returns `New-CheckResult`:

```powershell
function Test-QueueBacklog {
    param([object] $Context)

    $backlog = Get-YourBacklogCount
    if ($backlog -gt 100) {
        return New-CheckResult -Name 'Queue Backlog' -Status Warning `
            -Summary "Backlog is $backlog" -Details @{ Backlog = $backlog }
    }

    New-CheckResult -Name 'Queue Backlog' -Status Passed `
        -Summary "Backlog is $backlog" -Details @{ Backlog = $backlog }
}
```

Register it in `Get-SupportCheckDefinitions` in the desired execution order:

```powershell
[pscustomobject]@{ Name = 'Queue Backlog'; Action = ${function:Test-QueueBacklog} }
```

Add a focused Pester test for the check, especially for its warning and failure thresholds. A check that throws is automatically converted to `Failed`, and later checks still run.

## Schedule it

For Windows Task Scheduler, use an action similar to:

```text
Program:  pwsh.exe
Arguments: -NoProfile -File "G:\Git\TransactionProcessing\SupportTools\ScheduledTasks\DailySupportCheck\Invoke-DailySupportCheck.ps1" -OutputPath "C:\SupportReports\Daily"
```

Run under an account that can access the systems checked and write to the configured report directory.

## Configure report delivery later

`Send-SupportReport` currently returns a `LocalFiles` transport result containing the JSON and HTML paths. Once the delivery mechanism is confirmed, replace or extend that function to send the report by email, Teams, webhook, or another approved transport. The check and report code should not need to change.

## Tests

The tests do not require external services:

```powershell
Import-Module Pester
$config = [PesterConfiguration]::Default
$config.TestRegistry.Enabled = $false
$config.TestDrive.Enabled = $false
$config.Run.Path = @('.\ScheduledTasks\DailySupportCheck\Tests\Invoke-DailySupportCheck.Tests.ps1')
Invoke-Pester -Configuration $config
```
