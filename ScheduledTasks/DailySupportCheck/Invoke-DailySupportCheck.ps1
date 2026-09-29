[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'Reports'),
    [string] $ConfigPath = (Join-Path $PSScriptRoot 'daily-support-check.json'),
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

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return New-CheckResult -Name 'Template Configuration' -Status Failed -Summary 'Configuration could not be loaded.' -Details $Context.ConfigurationError -Error $Context.ConfigurationError
    }

    New-CheckResult -Name 'Template Configuration' -Status Passed -Summary 'Template configuration is ready for additional checks.' -Details 'Replace or extend the registered checks for environment-specific support actions.'
}

function Get-SupportConfiguration {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Configuration file was not found: $Path"
    }

    $configuration = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    if ($null -eq $configuration.DiskSpace) {
        throw 'Configuration must contain a DiskSpace section.'
    }

    $threshold = [double] $configuration.DiskSpace.DefaultMinimumFreePercent
    if ($threshold -lt 0 -or $threshold -gt 100) {
        throw 'DiskSpace.DefaultMinimumFreePercent must be between 0 and 100.'
    }

    foreach ($override in @($configuration.DiskSpace.DriveOverrides.PSObject.Properties)) {
        $overrideValue = [double] $override.Value
        if ($overrideValue -lt 0 -or $overrideValue -gt 100) {
            throw "Disk-space override for '$($override.Name)' must be between 0 and 100."
        }
    }

    if ($configuration.PSObject.Properties['HealthMonitoring']) {
        if (-not [bool] $configuration.HealthMonitoring.Enabled) {
            return $configuration
        }

        $healthMonitoringUri = $null
        if (-not [uri]::TryCreate([string] $configuration.HealthMonitoring.BaseUrl, [UriKind]::Absolute, [ref] $healthMonitoringUri)) {
            throw 'HealthMonitoring.BaseUrl must be an absolute URI.'
        }

        if ([int] $configuration.HealthMonitoring.TimeoutSeconds -le 0) {
            throw 'HealthMonitoring.TimeoutSeconds must be greater than zero.'
        }
    }

    if ($configuration.PSObject.Properties['SubscriptionService']) {
        if (-not [bool] $configuration.SubscriptionService.Enabled) {
            return $configuration
        }

        $subscriptionServiceUri = $null
        if (-not [uri]::TryCreate([string] $configuration.SubscriptionService.BaseUrl, [UriKind]::Absolute, [ref] $subscriptionServiceUri)) {
            throw 'SubscriptionService.BaseUrl must be an absolute URI.'
        }

        if ([int] $configuration.SubscriptionService.TimeoutSeconds -le 0) {
            throw 'SubscriptionService.TimeoutSeconds must be greater than zero.'
        }
    }

    if ($configuration.PSObject.Properties['KurrentDbProjections']) {
        if (-not [bool] $configuration.KurrentDbProjections.Enabled) {
            return $configuration
        }

        $kurrentDbUri = $null
        if (-not [uri]::TryCreate([string] $configuration.KurrentDbProjections.BaseUrl, [UriKind]::Absolute, [ref] $kurrentDbUri)) {
            throw 'KurrentDbProjections.BaseUrl must be an absolute URI.'
        }

        if ([int] $configuration.KurrentDbProjections.TimeoutSeconds -le 0) {
            throw 'KurrentDbProjections.TimeoutSeconds must be greater than zero.'
        }

        $hasUsername = $configuration.KurrentDbProjections.PSObject.Properties['Username'] -and -not [string]::IsNullOrWhiteSpace([string] $configuration.KurrentDbProjections.Username)
        $hasPasswordEnvironmentVariable = $configuration.KurrentDbProjections.PSObject.Properties['PasswordEnvironmentVariable'] -and -not [string]::IsNullOrWhiteSpace([string] $configuration.KurrentDbProjections.PasswordEnvironmentVariable)
        if ($hasUsername -xor $hasPasswordEnvironmentVariable) {
            throw 'KurrentDbProjections.Username and KurrentDbProjections.PasswordEnvironmentVariable must be configured together.'
        }
    }

    if ($configuration.PSObject.Properties['ScheduledTasks']) {
        if (-not [bool] $configuration.ScheduledTasks.Enabled) {
            return $configuration
        }

        if ($configuration.ScheduledTasks.PSObject.Properties['Tasks']) {
            foreach ($task in @($configuration.ScheduledTasks.Tasks)) {
                if ([string]::IsNullOrWhiteSpace([string] $task.Name)) {
                    throw 'ScheduledTasks entries must specify a Name.'
                }
                if ($task.PSObject.Properties['MaxLastRunAgeHours'] -and [double] $task.MaxLastRunAgeHours -le 0) {
                    throw "Scheduled task '$($task.Name)' MaxLastRunAgeHours must be greater than zero."
                }
            }
        }
    }

    return $configuration
}

function Get-DiskSpaceSnapshot {
    [CmdletBinding()]
    param()

    @(Get-PSDrive -PSProvider FileSystem | Where-Object { $null -ne $_.Free } | ForEach-Object {
            $freeBytes = [int64] $_.Free
            $sizeBytes = $freeBytes + [int64] $_.Used
            [pscustomobject]@{
                Drive     = "$($_.Name):"
                SizeBytes = $sizeBytes
                FreeBytes = $freeBytes
            }
        })
}

function Test-DiskSpace {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return New-CheckResult -Name 'Disk Space' -Status Failed -Summary 'Disk-space configuration could not be loaded.' -Details $Context.ConfigurationError -Error $Context.ConfigurationError
    }

    $settings = $Context.Configuration.DiskSpace
    if (-not [bool] $settings.Enabled) {
        return New-CheckResult -Name 'Disk Space' -Status Passed -Summary 'Disk-space check is disabled by configuration.' -Details @()
    }

    try {
        $provider = ${function:Get-DiskSpaceSnapshot}
        if ($Context.PSObject.Properties['DiskSpaceProvider']) {
            $provider = $Context.DiskSpaceProvider
        }
        $snapshots = @(& $provider)
        if ($snapshots.Count -eq 0) {
            return New-CheckResult -Name 'Disk Space' -Status Warning -Summary 'No filesystem drives were found.' -Details @()
        }

        $details = foreach ($snapshot in $snapshots) {
            if ([int64] $snapshot.SizeBytes -le 0) {
                throw "Drive '$($snapshot.Drive)' reported an invalid size."
            }

            $drive = [string] $snapshot.Drive
            $threshold = [double] $settings.DefaultMinimumFreePercent
            $override = $settings.DriveOverrides.PSObject.Properties[$drive]
            if ($null -ne $override) {
                $threshold = [double] $override.Value
            }

            $freePercent = ([double] $snapshot.FreeBytes / [double] $snapshot.SizeBytes) * 100
            [pscustomobject]@{
                Drive           = $drive
                SizeBytes       = [int64] $snapshot.SizeBytes
                FreeBytes       = [int64] $snapshot.FreeBytes
                FreePercent     = [math]::Round($freePercent, 2)
                ThresholdPercent = $threshold
                Status          = if ($freePercent -lt $threshold) { 'Warning' } else { 'Passed' }
            }
        }

        $belowThreshold = @($details | Where-Object Status -eq 'Warning')
        if ($belowThreshold.Count -gt 0) {
            $drives = $belowThreshold.Drive -join ', '
            return New-CheckResult -Name 'Disk Space' -Status Warning -Summary "Drive(s) below free-space threshold: $drives" -Details @($details)
        }

        New-CheckResult -Name 'Disk Space' -Status Passed -Summary "All $($details.Count) filesystem drive(s) meet the configured free-space threshold." -Details @($details)
    }
    catch {
        New-CheckResult -Name 'Disk Space' -Status Failed -Summary 'Disk-space inspection failed.' -Details $_.Exception.Message -Error $_.Exception.ToString()
    }
}

function Test-HealthMonitoring {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return New-CheckResult -Name 'HealthMonitoring' -Status Failed -Summary 'HealthMonitoring configuration could not be loaded.' -Details $Context.ConfigurationError -Error $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['HealthMonitoring'] -or -not [bool] $Context.Configuration.HealthMonitoring.Enabled) {
        return New-CheckResult -Name 'HealthMonitoring' -Status Passed -Summary 'HealthMonitoring check is disabled by configuration.' -Details @()
    }

    try {
        $settings = $Context.Configuration.HealthMonitoring
        $uri = ([uri]::new("$($settings.BaseUrl.TrimEnd('/'))/$($settings.ServicesPath.TrimStart('/'))")).AbsoluteUri
        $timeoutSeconds = [int] $settings.TimeoutSeconds
        $provider = {
            param($requestUri, $requestTimeoutSeconds)
            Invoke-RestMethod -Uri $requestUri -Method Get -TimeoutSec $requestTimeoutSeconds
        }
        if ($Context.PSObject.Properties['HealthMonitoringProvider']) {
            $provider = $Context.HealthMonitoringProvider
        }

        $services = @(& $provider $uri $timeoutSeconds)
        if ($services.Count -eq 0) {
            return New-CheckResult -Name 'HealthMonitoring' -Status Warning -Summary 'HealthMonitoring returned no monitored services.' -Details @()
        }

        $details = foreach ($service in $services) {
            $status = [string] $service.Status
            if ([string]::IsNullOrWhiteSpace($status)) {
                $status = 'Unknown'
            }
            $status = (Get-Culture).TextInfo.ToTitleCase($status.ToLowerInvariant())
            [pscustomobject]@{
                ServiceId         = [string] $service.ServiceId
                Name              = [string] $service.Name
                Status            = $status
                LastObservedAtUtc = if ($service.PSObject.Properties['LastObservedAtUtc']) { $service.LastObservedAtUtc } else { $null }
                LastError         = if ($service.PSObject.Properties['LastError']) { $service.LastError } else { $null }
            }
        }

        $failed = @($details | Where-Object Status -in @('Unhealthy', 'Unknown'))
        $warnings = @($details | Where-Object Status -eq 'Degraded')
        if ($failed.Count -gt 0) {
            return New-CheckResult -Name 'HealthMonitoring' -Status Failed -Summary "$($failed.Count) monitored service(s) are unhealthy or unknown." -Details @($details)
        }
        if ($warnings.Count -gt 0) {
            return New-CheckResult -Name 'HealthMonitoring' -Status Warning -Summary "$($warnings.Count) monitored service(s) are degraded." -Details @($details)
        }

        New-CheckResult -Name 'HealthMonitoring' -Status Passed -Summary "All $($details.Count) monitored service(s) are healthy." -Details @($details)
    }
    catch {
        New-CheckResult -Name 'HealthMonitoring' -Status Failed -Summary 'HealthMonitoring endpoint query failed.' -Details $_.Exception.Message -Error $_.Exception.ToString()
    }
}

function Test-SubscriptionService {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return New-CheckResult -Name 'Subscription Service' -Status Failed -Summary 'Subscription service configuration could not be loaded.' -Details $Context.ConfigurationError -Error $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['SubscriptionService'] -or -not [bool] $Context.Configuration.SubscriptionService.Enabled) {
        return New-CheckResult -Name 'Subscription Service' -Status Passed -Summary 'Subscription service check is disabled by configuration.' -Details @()
    }

    try {
        $settings = $Context.Configuration.SubscriptionService
        $uri = ([uri]::new("$($settings.BaseUrl.TrimEnd('/'))/$($settings.StatusPath.TrimStart('/'))")).AbsoluteUri
        $timeoutSeconds = [int] $settings.TimeoutSeconds
        $provider = {
            param($requestUri, $requestTimeoutSeconds)
            Invoke-RestMethod -Uri $requestUri -Method Get -TimeoutSec $requestTimeoutSeconds
        }
        if ($Context.PSObject.Properties['SubscriptionServiceProvider']) {
            $provider = $Context.SubscriptionServiceProvider
        }

        $subscriptions = @(& $provider $uri $timeoutSeconds)
        if ($subscriptions.Count -eq 0) {
            return New-CheckResult -Name 'Subscription Service' -Status Warning -Summary 'Subscription service returned no subscriptions.' -Details @()
        }

        $details = foreach ($subscription in $subscriptions) {
            $isRunning = [bool] $subscription.isRunning
            $parkedEventCount = if ($subscription.PSObject.Properties['parkedEventCount'] -and $null -ne $subscription.parkedEventCount) { [int64] $subscription.parkedEventCount } else { 0 }
            [pscustomobject]@{
                SubscriptionId       = [string] $subscription.subscriptionId
                Tag                  = [string] $subscription.tag
                IsRunning            = $isRunning
                Health               = [string] $subscription.health
                ParkedEventCount     = $parkedEventCount
                OperationalReason    = if ($subscription.PSObject.Properties['operationalReason']) { $subscription.operationalReason } else { $null }
                RuntimeFailureReason = if ($subscription.PSObject.Properties['runtimeFailureReason']) { $subscription.runtimeFailureReason } else { $null }
                Status               = if (-not $isRunning) { 'Failed' } elseif ($parkedEventCount -gt 0) { 'Warning' } else { 'Passed' }
            }
        }

        $stopped = @($details | Where-Object Status -eq 'Failed')
        $parked = @($details | Where-Object Status -eq 'Warning')
        if ($stopped.Count -gt 0) {
            return New-CheckResult -Name 'Subscription Service' -Status Failed -Summary "$($stopped.Count) subscription(s) are not running." -Details @($details)
        }
        if ($parked.Count -gt 0) {
            return New-CheckResult -Name 'Subscription Service' -Status Warning -Summary "$($parked.Count) subscription(s) have parked messages." -Details @($details)
        }

        New-CheckResult -Name 'Subscription Service' -Status Passed -Summary "All $($details.Count) subscription(s) are running without parked messages." -Details @($details)
    }
    catch {
        New-CheckResult -Name 'Subscription Service' -Status Failed -Summary 'Subscription status endpoint query failed.' -Details $_.Exception.Message -Error $_.Exception.ToString()
    }
}

function Test-KurrentDbProjections {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return New-CheckResult -Name 'KurrentDB Projections' -Status Failed -Summary 'KurrentDB projection configuration could not be loaded.' -Details $Context.ConfigurationError -Error $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['KurrentDbProjections'] -or -not [bool] $Context.Configuration.KurrentDbProjections.Enabled) {
        return New-CheckResult -Name 'KurrentDB Projections' -Status Passed -Summary 'KurrentDB projection check is disabled by configuration.' -Details @()
    }

    try {
        $settings = $Context.Configuration.KurrentDbProjections
        $configuredNames = @($settings.ProjectionNames | ForEach-Object { [string] $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($configuredNames.Count -eq 0) {
            return New-CheckResult -Name 'KurrentDB Projections' -Status Warning -Summary 'No KurrentDB projections are configured for checking.' -Details @()
        }

        $uri = ([uri]::new("$($settings.BaseUrl.TrimEnd('/'))/$($settings.ProjectionsPath.TrimStart('/'))")).AbsoluteUri
        $timeoutSeconds = [int] $settings.TimeoutSeconds
        $provider = {
            param($requestUri, $requestTimeoutSeconds)
            $request = @{ Uri = $requestUri; Method = 'Get'; TimeoutSec = $requestTimeoutSeconds }
            $hasUsername = $settings.PSObject.Properties['Username'] -and -not [string]::IsNullOrWhiteSpace([string] $settings.Username)
            if ($hasUsername) {
                $passwordEnvironmentVariable = [string] $settings.PasswordEnvironmentVariable
                $password = [Environment]::GetEnvironmentVariable($passwordEnvironmentVariable)
                if ([string]::IsNullOrWhiteSpace($password)) {
                    throw "KurrentDB password environment variable '$passwordEnvironmentVariable' is not set."
                }
                $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
                $request.Authentication = 'Basic'
                $request.Credential = [pscredential]::new([string] $settings.Username, $securePassword)
            }
            Invoke-RestMethod @request
        }
        if ($Context.PSObject.Properties['KurrentDbProjectionProvider']) {
            $provider = $Context.KurrentDbProjectionProvider
        }

        $projections = @(& $provider $uri $timeoutSeconds)
        $projectionByName = @{}
        foreach ($projection in $projections) {
            $name = if ($projection.PSObject.Properties['effectiveName']) { [string] $projection.effectiveName } elseif ($projection.PSObject.Properties['name']) { [string] $projection.name } else { '' }
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                $projectionByName[$name] = $projection
            }
        }

        $details = foreach ($configuredName in $configuredNames) {
            $projection = $projectionByName[$configuredName]
            if ($null -eq $projection) {
                [pscustomobject]@{ Name = $configuredName; Status = 'Missing'; Progress = $null; StateReason = 'Projection was not returned by KurrentDB.' }
                continue
            }

            [pscustomobject]@{
                Name        = $configuredName
                Status      = [string] $projection.status
                Progress    = if ($projection.PSObject.Properties['progress']) { $projection.progress } else { $null }
                StateReason = if ($projection.PSObject.Properties['stateReason']) { $projection.stateReason } else { $null }
            }
        }

        $notRunning = @($details | Where-Object { $_.Status -ne 'Running' })
        if ($notRunning.Count -gt 0) {
            $summary = ($notRunning | ForEach-Object { "$($_.Name) [$($_.Status)]" }) -join ', '
            return New-CheckResult -Name 'KurrentDB Projections' -Status Failed -Summary "Projection(s) not running: $summary" -Details @($details)
        }

        New-CheckResult -Name 'KurrentDB Projections' -Status Passed -Summary "All $($details.Count) configured KurrentDB projection(s) are running." -Details @($details)
    }
    catch {
        New-CheckResult -Name 'KurrentDB Projections' -Status Failed -Summary 'KurrentDB projection status query failed.' -Details $_.Exception.Message -Error $_.Exception.ToString()
    }
}

function Test-ScheduledTasks {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return New-CheckResult -Name 'Scheduled Tasks' -Status Failed -Summary 'Scheduled-task configuration could not be loaded.' -Details $Context.ConfigurationError -Error $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['ScheduledTasks'] -or -not [bool] $Context.Configuration.ScheduledTasks.Enabled) {
        return New-CheckResult -Name 'Scheduled Tasks' -Status Passed -Summary 'Scheduled-task check is disabled by configuration.' -Details @()
    }

    $taskConfigurations = @($Context.Configuration.ScheduledTasks.Tasks)
    if ($taskConfigurations.Count -eq 0) {
        return New-CheckResult -Name 'Scheduled Tasks' -Status Warning -Summary 'No scheduled tasks are configured for checking.' -Details @()
    }

    try {
        $provider = {
            param($taskConfiguration)
            $task = Get-ScheduledTask -TaskName $taskConfiguration.Name -TaskPath $taskConfiguration.Path -ErrorAction Stop
            $info = Get-ScheduledTaskInfo -TaskName $taskConfiguration.Name -TaskPath $taskConfiguration.Path -ErrorAction Stop
            [pscustomobject]@{
                Name            = $task.TaskName
                Path            = $task.TaskPath
                State           = [string] $task.State
                LastTaskResult  = $info.LastTaskResult
                LastRunTime     = $info.LastRunTime
                NextRunTime     = $info.NextRunTime
            }
        }
        if ($Context.PSObject.Properties['ScheduledTaskProvider']) {
            $provider = $Context.ScheduledTaskProvider
        }

        $now = [datetime]::Now
        $details = foreach ($taskConfiguration in $taskConfigurations) {
            try {
                $task = & $provider $taskConfiguration
                $state = [string] $task.State
                $lastTaskResult = [int64] $task.LastTaskResult
                $lastRunTime = if ($task.PSObject.Properties['LastRunTime']) { $task.LastRunTime } else { $null }
                $nextRunTime = if ($task.PSObject.Properties['NextRunTime']) { $task.NextRunTime } else { $null }
                $failureReasons = [System.Collections.Generic.List[string]]::new()

                if ($state -eq 'Disabled') { $failureReasons.Add('Disabled') }
                if ($lastTaskResult -ne 0) { $failureReasons.Add("LastTaskResult=$lastTaskResult") }
                if ($null -eq $lastRunTime) {
                    $failureReasons.Add('No successful run recorded')
                }
                elseif ($taskConfiguration.PSObject.Properties['MaxLastRunAgeHours'] -and $lastRunTime -lt $now.AddHours(-[double] $taskConfiguration.MaxLastRunAgeHours)) {
                    $failureReasons.Add('Last run is too old')
                }
                if ($null -ne $nextRunTime -and $nextRunTime -lt $now -and $state -ne 'Running') {
                    $failureReasons.Add('Next run is overdue')
                }
                if ($state -notin @('Ready', 'Running', 'Disabled')) {
                    $failureReasons.Add("Unexpected state '$state'")
                }

                [pscustomobject]@{
                    Name            = [string] $taskConfiguration.Name
                    Path            = [string] $taskConfiguration.Path
                    State           = $state
                    LastTaskResult  = $lastTaskResult
                    LastRunTime     = $lastRunTime
                    NextRunTime     = $nextRunTime
                    Status          = if ($failureReasons.Count -gt 0) { 'Failed' } else { 'Passed' }
                    FailureReason   = if ($failureReasons.Count -gt 0) { $failureReasons -join '; ' } else { $null }
                }
            }
            catch {
                [pscustomobject]@{
                    Name = [string] $taskConfiguration.Name
                    Path = [string] $taskConfiguration.Path
                    State = 'Missing'
                    LastTaskResult = $null
                    LastRunTime = $null
                    NextRunTime = $null
                    Status = 'Failed'
                    FailureReason = $_.Exception.Message
                }
            }
        }

        $failed = @($details | Where-Object Status -eq 'Failed')
        if ($failed.Count -gt 0) {
            $summary = ($failed | ForEach-Object { "$($_.Name) [$($_.FailureReason)]" }) -join ', '
            return New-CheckResult -Name 'Scheduled Tasks' -Status Failed -Summary "Scheduled task(s) require attention: $summary" -Details @($details)
        }

        New-CheckResult -Name 'Scheduled Tasks' -Status Passed -Summary "All $($details.Count) configured scheduled task(s) are healthy." -Details @($details)
    }
    catch {
        New-CheckResult -Name 'Scheduled Tasks' -Status Failed -Summary 'Scheduled-task status query failed.' -Details $_.Exception.Message -Error $_.Exception.ToString()
    }
}

function Get-SupportCheckDefinitions {
    @(
        [pscustomobject]@{ Name = 'PowerShell Runtime'; Action = ${function:Test-PowerShellRuntime} }
        [pscustomobject]@{ Name = 'Report Output Directory'; Action = ${function:Test-ReportOutputDirectory} }
        [pscustomobject]@{ Name = 'Disk Space'; Action = ${function:Test-DiskSpace} }
        [pscustomobject]@{ Name = 'HealthMonitoring'; Action = ${function:Test-HealthMonitoring} }
        [pscustomobject]@{ Name = 'Subscription Service'; Action = ${function:Test-SubscriptionService} }
        [pscustomobject]@{ Name = 'KurrentDB Projections'; Action = ${function:Test-KurrentDbProjections} }
        [pscustomobject]@{ Name = 'Scheduled Tasks'; Action = ${function:Test-ScheduledTasks} }
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
        [string] $ConfigPath = (Join-Path $PSScriptRoot 'daily-support-check.json'),
        [string[]] $CheckName,
        [switch] $PassThru,
        [switch] $Strict
    )

    $startedAt = [datetime]::UtcNow
    $configuration = $null
    $configurationError = $null
    try {
        $configuration = Get-SupportConfiguration -Path $ConfigPath
    }
    catch {
        $configurationError = $_.Exception.Message
        $configuration = [pscustomobject]@{
            DiskSpace = [pscustomobject]@{
                Enabled = $false
                DefaultMinimumFreePercent = 15
                DriveOverrides = [pscustomobject]@{}
            }
        }
    }

    $context = [pscustomobject]@{ OutputPath = $OutputPath; ConfigPath = $ConfigPath; Configuration = $configuration; ConfigurationError = $configurationError; StartedAt = $startedAt }
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
        $report = Invoke-DailySupportCheck -OutputPath $OutputPath -ConfigPath $ConfigPath -CheckName $CheckName -PassThru -Strict:$Strict
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
