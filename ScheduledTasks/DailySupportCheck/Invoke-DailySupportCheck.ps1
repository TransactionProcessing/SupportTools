[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'Reports'),
    [string[]] $CheckName,
    [switch] $PassThru,
    [switch] $Strict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function New-CheckResult {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [ValidateSet('Passed', 'Warning', 'Failed')] [string] $Status,
        [Parameter(Mandatory)] [string] $Summary,
        [object] $Details,
        [datetime] $StartedAt = ([datetime]::UtcNow),
        [datetime] $CompletedAt = ([datetime]::UtcNow),
        [double] $DurationMs = 0,
        [string] $Error
    )

    [pscustomobject]@{
        Name        = $Name
        Status      = $Status
        Summary     = $Summary
        Details     = $Details
        StartedAt   = $StartedAt.ToUniversalTime().ToString('o')
        CompletedAt = $CompletedAt.ToUniversalTime().ToString('o')
        DurationMs  = [math]::Round($DurationMs, 2)
        Error       = $Error
    }
}

function Invoke-SupportCheck {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [scriptblock] $Action,
        [object] $Context
    )

    $startedAt = [datetime]::UtcNow
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $result = & $Action $Context
        if ($null -eq $result) {
            throw "Check '$Name' did not return a result."
        }

        $result = @($result)[-1]
        if ($result.Status -notin @('Passed', 'Warning', 'Failed')) {
            throw "Check '$Name' returned an invalid status."
        }

        $stopwatch.Stop()
        if ($result.PSObject.Properties.Name -contains 'DurationMs') {
            $result.DurationMs = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 2)
        }
        return $result
    }
    catch {
        $stopwatch.Stop()
        return New-CheckResult -Name $Name -Status Failed -Summary 'The check raised an exception.' -Details $_.Exception.Message -StartedAt $startedAt -CompletedAt ([datetime]::UtcNow) -DurationMs $stopwatch.Elapsed.TotalMilliseconds -Error $_.Exception.ToString()
    }
}

function Get-OverallStatus {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object[]] $Results)

    if (@($Results | Where-Object Status -eq 'Failed').Count -gt 0) { return 'Failed' }
    if (@($Results | Where-Object Status -eq 'Warning').Count -gt 0) { return 'Warning' }
    return 'Passed'
}

function Test-PowerShellRuntime {
    param([object] $Context)

    New-CheckResult -Name 'PowerShell Runtime' -Status Passed -Summary "PowerShell $($PSVersionTable.PSVersion) is available." -Details ([pscustomobject]@{
            Edition = $PSVersionTable.PSEdition
            Version = $PSVersionTable.PSVersion.ToString()
        })
}

function Test-ReportOutputDirectory {
    param([object] $Context)

    $path = [string] $Context.OutputPath
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    $probe = Join-Path $path ('.daily-support-check-write-test-' + [guid]::NewGuid().ToString('N'))
    'write-test' | Set-Content -LiteralPath $probe -Encoding UTF8
    Remove-Item -LiteralPath $probe -Force

    New-CheckResult -Name 'Report Output Directory' -Status Passed -Summary "Report output directory is writable: $path" -Details $path
}

function Test-TemplateConfiguration {
    param([object] $Context)

    New-CheckResult -Name 'Template Configuration' -Status Passed -Summary 'Template configuration is ready for additional checks.' -Details 'Replace or extend the registered checks for environment-specific support actions.'
}

function Get-SupportCheckDefinitions {
    @(
        [pscustomobject]@{ Name = 'PowerShell Runtime'; Action = ${function:Test-PowerShellRuntime} }
        [pscustomobject]@{ Name = 'Report Output Directory'; Action = ${function:Test-ReportOutputDirectory} }
        [pscustomobject]@{ Name = 'Template Configuration'; Action = ${function:Test-TemplateConfiguration} }
    )
}

function New-SupportReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [datetime] $StartedAt,
        [Parameter(Mandatory)] [object[]] $Results
    )

    $completedAt = [datetime]::UtcNow
    $status = Get-OverallStatus -Results $Results
    [pscustomobject]@{
        ReportName  = 'Daily Support Check'
        RunId       = [guid]::NewGuid().ToString()
        StartedAt   = $StartedAt.ToUniversalTime().ToString('o')
        CompletedAt = $completedAt.ToUniversalTime().ToString('o')
        DurationMs  = [math]::Round(($completedAt - $StartedAt).TotalMilliseconds, 2)
        OverallStatus = $status
        Counts      = [pscustomobject]@{
            Passed  = @($Results | Where-Object Status -eq 'Passed').Count
            Warning = @($Results | Where-Object Status -eq 'Warning').Count
            Failed  = @($Results | Where-Object Status -eq 'Failed').Count
        }
        Checks      = @($Results)
    }
}

function ConvertTo-ReportText {
    param([object] $Value)

    if ($null -eq $Value) { return '' }
    if ($Value -is [string]) { return $Value }
    return ($Value | ConvertTo-Json -Depth 8 -Compress)
}

function Write-SupportReports {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Report,
        [Parameter(Mandatory)] [string] $OutputPath
    )

    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    $stamp = [datetime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
    $baseName = "daily-support-check-$stamp"
    $jsonPath = Join-Path $OutputPath "$baseName.json"
    $htmlPath = Join-Path $OutputPath "$baseName.html"

    $Report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $jsonPath -Encoding UTF8

    $rows = [System.Text.StringBuilder]::new()
    foreach ($check in @($Report.Checks)) {
        $status = [System.Net.WebUtility]::HtmlEncode([string] $check.Status)
        $name = [System.Net.WebUtility]::HtmlEncode([string] $check.Name)
        $summary = [System.Net.WebUtility]::HtmlEncode([string] $check.Summary)
        $details = [System.Net.WebUtility]::HtmlEncode((ConvertTo-ReportText $check.Details))
        $error = [System.Net.WebUtility]::HtmlEncode((ConvertTo-ReportText $check.Error))
        [void] $rows.AppendLine("<tr class=`"status-$status`"><td>$name</td><td>$status</td><td>$summary</td><td><pre>$details</pre></td><td><pre>$error</pre></td></tr>")
    }

    $overallStatus = [System.Net.WebUtility]::HtmlEncode([string] $Report.OverallStatus)
    $html = @"
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>Daily Support Check</title>
<style>body{font-family:Segoe UI,Arial,sans-serif;color:#222}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:8px;text-align:left;vertical-align:top}th{background:#eee}.status-Failed{background:#fde2e2}.status-Warning{background:#fff4cc}.status-Passed{background:#e4f4e4}pre{white-space:pre-wrap;margin:0}</style>
</head>
<body><h1>Daily Support Check</h1><p><strong>Overall status:</strong> $overallStatus</p>
<p><strong>Started:</strong> $([System.Net.WebUtility]::HtmlEncode([string] $Report.StartedAt))<br><strong>Completed:</strong> $([System.Net.WebUtility]::HtmlEncode([string] $Report.CompletedAt))</p>
<table><thead><tr><th>Check</th><th>Status</th><th>Summary</th><th>Details</th><th>Error</th></tr></thead><tbody>$rows</tbody></table>
</body></html>
"@
    $html | Set-Content -LiteralPath $htmlPath -Encoding UTF8

    [pscustomobject]@{ JsonPath = $jsonPath; HtmlPath = $htmlPath }
}

function Send-SupportReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Report,
        [Parameter(Mandatory)] [object] $ReportPaths
    )

    [pscustomobject]@{
        Transport = 'LocalFiles'
        Status    = 'Ready'
        Paths     = $ReportPaths
        Message   = 'Reports are available as local files. Replace this function when a delivery transport is confirmed.'
    }
}

function Invoke-DailySupportCheck {
    [CmdletBinding()]
    param(
        [string] $OutputPath = (Join-Path $PSScriptRoot 'Reports'),
        [string[]] $CheckName,
        [switch] $PassThru,
        [switch] $Strict
    )

    $startedAt = [datetime]::UtcNow
    $context = [pscustomobject]@{ OutputPath = $OutputPath; StartedAt = $startedAt }
    $definitions = @(Get-SupportCheckDefinitions)
    if ($CheckName) {
        $definitions = @($definitions | Where-Object Name -in $CheckName)
        $unknown = @($CheckName | Where-Object { $_ -notin $definitions.Name })
        if ($unknown.Count -gt 0) {
            throw "Unknown check name(s): $($unknown -join ', ')"
        }
    }

    $results = foreach ($definition in $definitions) {
        Invoke-SupportCheck -Name $definition.Name -Action $definition.Action -Context $context
    }

    $report = New-SupportReport -StartedAt $startedAt -Results @($results)
    $paths = Write-SupportReports -Report $report -OutputPath $OutputPath
    $transport = Send-SupportReport -Report $report -ReportPaths $paths
    $report | Add-Member -NotePropertyName ReportPaths -NotePropertyValue $paths
    $report | Add-Member -NotePropertyName Transport -NotePropertyValue $transport

    Write-Host "Daily Support Check: $($report.OverallStatus) ($($report.Checks.Count) checks)"
    Write-Host "JSON report: $($paths.JsonPath)"
    Write-Host "HTML report: $($paths.HtmlPath)"

    if ($PassThru) { return $report }
}

$isDotSourced = $MyInvocation.InvocationName -eq '.'
if (-not $isDotSourced) {
    try {
        $report = Invoke-DailySupportCheck -OutputPath $OutputPath -CheckName $CheckName -PassThru -Strict:$Strict
        if ($report.OverallStatus -eq 'Failed' -or ($Strict -and $report.OverallStatus -eq 'Warning')) {
            exit 1
        }
        exit 0
    }
    catch {
        Write-Error $_
        exit 1
    }
}
