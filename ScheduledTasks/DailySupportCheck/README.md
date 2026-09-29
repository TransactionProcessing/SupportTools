# Daily Support Check

`Invoke-DailySupportCheck.ps1` is a PowerShell template for a daily operational support run. It executes independent checks, reads operational settings from JSON, writes a structured JSON report and a readable HTML report, and leaves report delivery behind a replaceable transport function.

## Run it interactively

From the repository root:

```powershell
pwsh -NoProfile -File .\ScheduledTasks\DailySupportCheck\Invoke-DailySupportCheck.ps1
```

Choose an output directory, configuration file, or subset of checks:

```powershell
pwsh -NoProfile -File .\ScheduledTasks\DailySupportCheck\Invoke-DailySupportCheck.ps1 `
    -OutputPath 'C:\SupportReports\Daily' `
    -ConfigPath 'C:\SupportConfig\daily-support-check.json' `
    -CheckName 'PowerShell Runtime', 'Report Output Directory'
```

Use `-PassThru` when calling the script from another PowerShell script and you need the report object. Use `-Strict` when warnings should produce a non-zero process exit code. Failed checks always produce a non-zero exit code.

The default configuration file is `daily-support-check.json` beside the script:

```json
{
  "DiskSpace": {
    "Enabled": true,
    "DefaultMinimumFreePercent": 15,
    "DriveOverrides": {
      "C:": 10,
      "D:": 20
    }
  },
  "HealthMonitoring": {
    "Enabled": true,
    "BaseUrl": "http://localhost:9620",
    "ServicesPath": "/api/services",
    "TimeoutSeconds": 10
  },
  "SubscriptionService": {
    "Enabled": true,
    "BaseUrl": "http://localhost:8080",
    "StatusPath": "/subscriptions/status",
    "TimeoutSeconds": 10
  },
  "KurrentDbProjections": {
    "Enabled": false,
    "BaseUrl": "http://localhost:2113",
    "ProjectionsPath": "/projections/any",
    "ProjectionNames": [
      "TransactionProcessor",
      "MerchantAggregator"
    ],
    "TimeoutSeconds": 10
  }
}
```

`DefaultMinimumFreePercent` is the minimum free-space percentage applied to every filesystem drive. An entry in `DriveOverrides` replaces that threshold for the matching drive. Low free space produces a `Warning`; a configuration or inspection error produces a `Failed` result.

The `HealthMonitoring` check calls the configured services endpoint. All services must be `Healthy` for the check to pass. `Degraded` produces a `Warning`; `Unhealthy`, `Unknown`, an empty response, or an unavailable endpoint produces a `Failed` result, except that an empty service list is reported as a warning because the endpoint responded but has nothing registered.

The `Subscription Service` check calls the configured subscription status endpoint. It fails when any subscription is not running, warns when parked messages exist, and passes when all subscriptions are running with no parked messages. The report includes subscription ID, tag, running state, health, parked-event count, operational reason, and runtime failure reason.

The `KurrentDB Projections` check calls the configured `/projections/any` endpoint and checks only the names in `ProjectionNames`. It passes when every selected projection is `Running` and fails when a selected projection is missing or has another status. Authentication is optional for insecure development instances. For secured instances, add `Username` and `PasswordEnvironmentVariable`; the password is read from that environment variable and is not stored in JSON. The check is disabled by default until the projection names have been configured.

The default template checks are:

1. `PowerShell Runtime`
2. `Report Output Directory`
3. `Disk Space`
4. `HealthMonitoring`
5. `Subscription Service`
6. `KurrentDB Projections`
7. `Template Configuration`

Replace or extend these with checks that are meaningful for the target environment. Set `DiskSpace.Enabled` to `false` when disk-space monitoring is not required for a particular host.

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
Arguments: -NoProfile -File "G:\Git\TransactionProcessing\SupportTools\ScheduledTasks\DailySupportCheck\Invoke-DailySupportCheck.ps1" -OutputPath "C:\SupportReports\Daily" -ConfigPath "C:\SupportConfig\daily-support-check.json"
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
