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

function Get-EnvironmentPasswordSecureString {
    [OutputType([System.Security.SecureString])]
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [char[]] $PasswordCharacters
    )

    $securePassword = [System.Security.SecureString]::new()
    foreach ($character in $PasswordCharacters) {
        $securePassword.AppendChar($character)
    }
    $securePassword.MakeReadOnly()
    return $securePassword
}

function Get-CheckResult {
    [OutputType([pscustomobject])]
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [ValidateSet('Passed', 'Warning', 'Failed')] [string] $Status,
        [Parameter(Mandatory)] [string] $Summary,
        [object] $Details,
        [datetime] $StartedAt = ([datetime]::UtcNow),
        [datetime] $CompletedAt = ([datetime]::UtcNow),
        [double] $DurationMs = 0,
        [string] $ErrorMessage
    )

    [pscustomobject]@{
        Name        = $Name
        Status      = $Status
        Summary     = $Summary
        Details     = $Details
        StartedAt   = $StartedAt.ToUniversalTime().ToString('o')
        CompletedAt = $CompletedAt.ToUniversalTime().ToString('o')
        DurationMs  = [math]::Round($DurationMs, 2)
        Error       = $ErrorMessage
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
        return Get-CheckResult -Name $Name -Status Failed -Summary 'The check raised an exception.' -Details $_.Exception.Message -StartedAt $startedAt -CompletedAt ([datetime]::UtcNow) -DurationMs $stopwatch.Elapsed.TotalMilliseconds -ErrorMessage $_.Exception.ToString()
    }
}

function Get-OverallStatus {
    [OutputType([string])]
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object[]] $Results)

    if (@($Results | Where-Object Status -eq 'Failed').Count -gt 0) { return 'Failed' }
    if (@($Results | Where-Object Status -eq 'Warning').Count -gt 0) { return 'Warning' }
    return 'Passed'
}

function Test-PowerShellRuntime {
    param([object] $Context)

    Get-CheckResult -Name 'PowerShell Runtime' -Status Passed -Summary "PowerShell $($PSVersionTable.PSVersion) is available." -Details ([pscustomobject]@{
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

    Get-CheckResult -Name 'Report Output Directory' -Status Passed -Summary "Report output directory is writable: $path" -Details $path
}

function Test-TemplateConfiguration {
    param([object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return Get-CheckResult -Name 'Template Configuration' -Status Failed -Summary 'Configuration could not be loaded.' -Details $Context.ConfigurationError -ErrorMessage $Context.ConfigurationError
    }

    Get-CheckResult -Name 'Template Configuration' -Status Passed -Summary 'Template configuration is ready for additional checks.' -Details 'Replace or extend the registered checks for environment-specific support actions.'
}

function Merge-SupportConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [psobject] $Base,
        [Parameter(Mandatory)] [psobject] $Override
    )

    foreach ($property in $Override.PSObject.Properties) {
        $baseProperty = $Base.PSObject.Properties[$property.Name]
        if ($null -ne $baseProperty -and $baseProperty.Value -is [pscustomobject] -and $property.Value -is [pscustomobject]) {
            Merge-SupportConfiguration -Base $baseProperty.Value -Override $property.Value | Out-Null
        }
        else {
            $Base | Add-Member -MemberType NoteProperty -Name $property.Name -Value $property.Value -Force
        }
    }

    $Base
}

function Get-SupportConfiguration {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Configuration file was not found: $Path"
    }

    $configuration = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $localPath = Join-Path (Split-Path -Parent $Path) (([IO.Path]::GetFileNameWithoutExtension($Path)) + '.local.json')
    if (Test-Path -LiteralPath $localPath -PathType Leaf) {
        $localConfiguration = Get-Content -LiteralPath $localPath -Raw | ConvertFrom-Json
        $configuration = Merge-SupportConfiguration -Base $configuration -Override $localConfiguration
    }
    if (-not $configuration.PSObject.Properties['ReportRetentionDays']) {
        $configuration | Add-Member -MemberType NoteProperty -Name ReportRetentionDays -Value 7
    }
    if ([int] $configuration.ReportRetentionDays -lt 1) {
        throw 'ReportRetentionDays must be greater than zero.'
    }
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
                if ($task.PSObject.Properties['ExpectedRunIntervalMinutes'] -and [double] $task.ExpectedRunIntervalMinutes -le 0) {
                    throw "Scheduled task '$($task.Name)' ExpectedRunIntervalMinutes must be greater than zero."
                }
                if ($task.PSObject.Properties['MaxLastRunAgeHours'] -and [double] $task.MaxLastRunAgeHours -le 0) {
                    throw "Scheduled task '$($task.Name)' MaxLastRunAgeHours must be greater than zero."
                }
            }
        }
    }

    if ($configuration.PSObject.Properties['KurrentDbWriteActivity']) {
        if (-not [bool] $configuration.KurrentDbWriteActivity.Enabled) {
            return $configuration
        }

        $writeActivityUri = $null
        if (-not [uri]::TryCreate([string] $configuration.KurrentDbWriteActivity.BaseUrl, [UriKind]::Absolute, [ref] $writeActivityUri)) {
            throw 'KurrentDbWriteActivity.BaseUrl must be an absolute URI.'
        }
        $streamConfigurations = if ($configuration.KurrentDbWriteActivity.PSObject.Properties['Streams']) {
            @($configuration.KurrentDbWriteActivity.Streams)
        }
        elseif ($configuration.KurrentDbWriteActivity.PSObject.Properties['StreamName']) {
            @([pscustomobject]@{
                    Name = $configuration.KurrentDbWriteActivity.StreamName
                    EventCount = $configuration.KurrentDbWriteActivity.EventCount
                    MaxLatestEventAgeMinutes = $configuration.KurrentDbWriteActivity.MaxLatestEventAgeMinutes
                })
        }
        else {
            @()
        }
        if ($streamConfigurations.Count -eq 0) {
            throw 'KurrentDbWriteActivity.Streams must contain at least one stream.'
        }
        foreach ($stream in $streamConfigurations) {
            if ([string]::IsNullOrWhiteSpace([string] $stream.Name)) {
                throw 'KurrentDbWriteActivity stream entries must specify a Name.'
            }
            if ([int] $stream.EventCount -le 0) {
                throw "KurrentDbWriteActivity EventCount for '$($stream.Name)' must be greater than zero."
            }
            if ($stream.PSObject.Properties['MaxLatestEventAgeMinutes'] -and [double] $stream.MaxLatestEventAgeMinutes -le 0) {
                throw "KurrentDbWriteActivity MaxLatestEventAgeMinutes for '$($stream.Name)' must be greater than zero."
            }
        }
    }

    return $configuration
}

function Get-DiskSpaceSnapshot {
    [OutputType([System.Array])]
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
        return Get-CheckResult -Name 'Disk Space' -Status Failed -Summary 'Disk-space configuration could not be loaded.' -Details $Context.ConfigurationError -ErrorMessage $Context.ConfigurationError
    }

    $settings = $Context.Configuration.DiskSpace
    if (-not [bool] $settings.Enabled) {
        return Get-CheckResult -Name 'Disk Space' -Status Passed -Summary 'Disk-space check is disabled by configuration.' -Details @()
    }

    try {
        $provider = ${function:Get-DiskSpaceSnapshot}
        if ($Context.PSObject.Properties['DiskSpaceProvider']) {
            $provider = $Context.DiskSpaceProvider
        }
        $snapshots = @(& $provider)
        if ($snapshots.Count -eq 0) {
            return Get-CheckResult -Name 'Disk Space' -Status Warning -Summary 'No filesystem drives were found.' -Details @()
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
            return Get-CheckResult -Name 'Disk Space' -Status Warning -Summary "Drive(s) below free-space threshold: $drives" -Details @($details)
        }

        Get-CheckResult -Name 'Disk Space' -Status Passed -Summary "All $($details.Count) filesystem drive(s) meet the configured free-space threshold." -Details @($details)
    }
    catch {
        Get-CheckResult -Name 'Disk Space' -Status Failed -Summary 'Disk-space inspection failed.' -Details $_.Exception.Message -ErrorMessage $_.Exception.ToString()
    }
}

function Test-HealthMonitoring {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return Get-CheckResult -Name 'HealthMonitoring' -Status Failed -Summary 'HealthMonitoring configuration could not be loaded.' -Details $Context.ConfigurationError -ErrorMessage $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['HealthMonitoring'] -or -not [bool] $Context.Configuration.HealthMonitoring.Enabled) {
        return Get-CheckResult -Name 'HealthMonitoring' -Status Passed -Summary 'HealthMonitoring check is disabled by configuration.' -Details @()
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
            return Get-CheckResult -Name 'HealthMonitoring' -Status Warning -Summary 'HealthMonitoring returned no monitored services.' -Details @()
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
            return Get-CheckResult -Name 'HealthMonitoring' -Status Failed -Summary "$($failed.Count) monitored service(s) are unhealthy or unknown." -Details @($details)
        }
        if ($warnings.Count -gt 0) {
            return Get-CheckResult -Name 'HealthMonitoring' -Status Warning -Summary "$($warnings.Count) monitored service(s) are degraded." -Details @($details)
        }

        Get-CheckResult -Name 'HealthMonitoring' -Status Passed -Summary "All $($details.Count) monitored service(s) are healthy." -Details @($details)
    }
    catch {
        Get-CheckResult -Name 'HealthMonitoring' -Status Failed -Summary 'HealthMonitoring endpoint query failed.' -Details $_.Exception.Message -ErrorMessage $_.Exception.ToString()
    }
}

function Test-SubscriptionService {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return Get-CheckResult -Name 'Subscription Service' -Status Failed -Summary 'Subscription service configuration could not be loaded.' -Details $Context.ConfigurationError -ErrorMessage $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['SubscriptionService'] -or -not [bool] $Context.Configuration.SubscriptionService.Enabled) {
        return Get-CheckResult -Name 'Subscription Service' -Status Passed -Summary 'Subscription service check is disabled by configuration.' -Details @()
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
        if ($subscriptions.Count -eq 1 -and $subscriptions[0].PSObject.Properties['subscriptionId'] -and $subscriptions[0].subscriptionId -is [array]) {
            $wrappedSubscriptions = $subscriptions[0]
            $subscriptionCount = @($wrappedSubscriptions.subscriptionId).Count
            $subscriptions = for ($index = 0; $index -lt $subscriptionCount; $index++) {
                $subscription = [ordered]@{}
                foreach ($property in $wrappedSubscriptions.PSObject.Properties) {
                    $value = $property.Value
                    $subscription[$property.Name] = if ($value -is [array] -and $value.Count -eq $subscriptionCount) { $value[$index] } else { $value }
                }
                [pscustomobject] $subscription
            }
        }
        if ($subscriptions.Count -eq 0) {
            return Get-CheckResult -Name 'Subscription Service' -Status Warning -Summary 'Subscription service returned no subscriptions.' -Details @()
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
            return Get-CheckResult -Name 'Subscription Service' -Status Failed -Summary "$($stopped.Count) subscription(s) are not running." -Details @($details)
        }
        if ($parked.Count -gt 0) {
            return Get-CheckResult -Name 'Subscription Service' -Status Warning -Summary "$($parked.Count) subscription(s) have parked messages." -Details @($details)
        }

        Get-CheckResult -Name 'Subscription Service' -Status Passed -Summary "All $($details.Count) subscription(s) are running without parked messages." -Details @($details)
    }
    catch {
        Get-CheckResult -Name 'Subscription Service' -Status Failed -Summary 'Subscription status endpoint query failed.' -Details $_.Exception.Message -ErrorMessage $_.Exception.ToString()
    }
}

function Test-KurrentDbProjections {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return Get-CheckResult -Name 'KurrentDB Projections' -Status Failed -Summary 'KurrentDB projection configuration could not be loaded.' -Details $Context.ConfigurationError -ErrorMessage $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['KurrentDbProjections'] -or -not [bool] $Context.Configuration.KurrentDbProjections.Enabled) {
        return Get-CheckResult -Name 'KurrentDB Projections' -Status Passed -Summary 'KurrentDB projection check is disabled by configuration.' -Details @()
    }

    try {
        $settings = $Context.Configuration.KurrentDbProjections
        $configuredNames = @($settings.ProjectionNames | ForEach-Object { [string] $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($configuredNames.Count -eq 0) {
            return Get-CheckResult -Name 'KurrentDB Projections' -Status Warning -Summary 'No KurrentDB projections are configured for checking.' -Details @()
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
                $securePassword = Get-EnvironmentPasswordSecureString -PasswordCharacters $password.ToCharArray()
                $request.Authentication = 'Basic'
                $request.Credential = [pscredential]::new([string] $settings.Username, $securePassword)
            }
            Invoke-RestMethod @request
        }
        if ($Context.PSObject.Properties['KurrentDbProjectionProvider']) {
            $provider = $Context.KurrentDbProjectionProvider
        }

        $rawResponse = @(& $provider $uri $timeoutSeconds)
        $projections = if ($rawResponse.Count -eq 1 -and $rawResponse[0].PSObject.Properties['projections']) {
            @($rawResponse[0].projections)
        }
        else {
            $rawResponse
        }
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

        $resultDetails = if ($settings.PSObject.Properties['Debug'] -and [bool] $settings.Debug) {
            [pscustomobject]@{
                Projections = @($details)
                RawResponse = @($rawResponse)
            }
        }
        else {
            @($details)
        }

        $notRunning = @($details | Where-Object { $_.Status -ne 'Running' })
        if ($notRunning.Count -gt 0) {
            $summary = ($notRunning | ForEach-Object { "$($_.Name) [$($_.Status)]" }) -join ', '
            return Get-CheckResult -Name 'KurrentDB Projections' -Status Failed -Summary "Projection(s) not running: $summary" -Details $resultDetails
        }

        Get-CheckResult -Name 'KurrentDB Projections' -Status Passed -Summary "All $($details.Count) configured KurrentDB projection(s) are running." -Details $resultDetails
    }
    catch {
        Get-CheckResult -Name 'KurrentDB Projections' -Status Failed -Summary 'KurrentDB projection status query failed.' -Details $_.Exception.Message -ErrorMessage $_.Exception.ToString()
    }
}

function Test-ScheduledTasks {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return Get-CheckResult -Name 'Scheduled Tasks' -Status Failed -Summary 'Scheduled-task configuration could not be loaded.' -Details $Context.ConfigurationError -ErrorMessage $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['ScheduledTasks'] -or -not [bool] $Context.Configuration.ScheduledTasks.Enabled) {
        return Get-CheckResult -Name 'Scheduled Tasks' -Status Passed -Summary 'Scheduled-task check is disabled by configuration.' -Details @()
    }

    $taskConfigurations = @($Context.Configuration.ScheduledTasks.Tasks)
    if ($taskConfigurations.Count -eq 0) {
        return Get-CheckResult -Name 'Scheduled Tasks' -Status Warning -Summary 'No scheduled tasks are configured for checking.' -Details @()
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
                $maxLastRunAgeMinutes = if ($taskConfiguration.PSObject.Properties['ExpectedRunIntervalMinutes']) {
                    [double] $taskConfiguration.ExpectedRunIntervalMinutes
                }
                elseif ($taskConfiguration.PSObject.Properties['MaxLastRunAgeHours']) {
                    [double] $taskConfiguration.MaxLastRunAgeHours * 60
                }
                else {
                    $null
                }
                $failureReasons = [System.Collections.Generic.List[string]]::new()

                if ($state -eq 'Disabled') { $failureReasons.Add('Disabled') }
                if ($lastTaskResult -ne 0) { $failureReasons.Add("LastTaskResult=$lastTaskResult") }
                if ($null -eq $lastRunTime) {
                    $failureReasons.Add('No successful run recorded')
                }
                elseif ($null -ne $maxLastRunAgeMinutes -and $lastRunTime -lt $now.AddMinutes(-$maxLastRunAgeMinutes)) {
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
                    MaxLastRunAgeMinutes = $maxLastRunAgeMinutes
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
            return Get-CheckResult -Name 'Scheduled Tasks' -Status Failed -Summary "Scheduled task(s) require attention: $summary" -Details @($details)
        }

        Get-CheckResult -Name 'Scheduled Tasks' -Status Passed -Summary "All $($details.Count) configured scheduled task(s) are healthy." -Details @($details)
    }
    catch {
        Get-CheckResult -Name 'Scheduled Tasks' -Status Failed -Summary 'Scheduled-task status query failed.' -Details $_.Exception.Message -ErrorMessage $_.Exception.ToString()
    }
}

function Import-KurrentDbClientAssembly {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $AssemblyPath)

    if ([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'SupportTools.KurrentDbClient' }) {
        return
    }

    if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
        throw "KurrentDB client assembly was not found: $AssemblyPath"
    }

    Add-Type -Path $AssemblyPath
}

function Get-KurrentDbWriteActivityCredentials {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Settings)

    $username = $null
    $password = $null
    if ($Settings.PSObject.Properties['Username'] -and -not [string]::IsNullOrWhiteSpace([string] $Settings.Username)) {
        $username = [string] $Settings.Username
        $passwordEnvironmentVariable = [string] $Settings.PasswordEnvironmentVariable
        $password = [Environment]::GetEnvironmentVariable($passwordEnvironmentVariable)
        if ([string]::IsNullOrWhiteSpace($password)) {
            throw "KurrentDB password environment variable '$passwordEnvironmentVariable' is not set."
        }
    }

    [pscustomobject]@{
        Username = $username
        Password = $password
    }
}

function Test-KurrentDbWriteActivity {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object] $Context)

    if ($Context.PSObject.Properties['ConfigurationError'] -and $Context.ConfigurationError) {
        return Get-CheckResult -Name 'KurrentDB Write Activity' -Status Failed -Summary 'KurrentDB write-activity configuration could not be loaded.' -Details $Context.ConfigurationError -ErrorMessage $Context.ConfigurationError
    }

    if (-not $Context.Configuration.PSObject.Properties['KurrentDbWriteActivity'] -or -not [bool] $Context.Configuration.KurrentDbWriteActivity.Enabled) {
        return Get-CheckResult -Name 'KurrentDB Write Activity' -Status Passed -Summary 'KurrentDB write-activity check is disabled by configuration.' -Details @()
    }

    try {
        $settings = $Context.Configuration.KurrentDbWriteActivity
        $streamConfigurations = if ($settings.PSObject.Properties['Streams']) {
            @($settings.Streams)
        }
        elseif ($settings.PSObject.Properties['StreamName']) {
            @([pscustomobject]@{ Name = $settings.StreamName; EventCount = $settings.EventCount; MaxLatestEventAgeMinutes = $settings.MaxLatestEventAgeMinutes })
        }
        else {
            @()
        }
        if ($streamConfigurations.Count -eq 0) {
            return Get-CheckResult -Name 'KurrentDB Write Activity' -Status Warning -Summary 'No KurrentDB streams are configured for write-activity checking.' -Details @()
        }

        $timeoutSeconds = if ($settings.PSObject.Properties['TimeoutSeconds']) { [int] $settings.TimeoutSeconds } else { 10 }
        $assemblyPath = if ($settings.PSObject.Properties['KurrentDbClientAssemblyPath'] -and -not [string]::IsNullOrWhiteSpace([string] $settings.KurrentDbClientAssemblyPath)) {
            [string] $settings.KurrentDbClientAssemblyPath
        }
        else {
            Join-Path $PSScriptRoot 'KurrentDbClient\SupportTools.KurrentDbClient.dll'
        }
        if (-not [IO.Path]::IsPathRooted($assemblyPath)) {
            $assemblyPath = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $Context.ConfigPath) $assemblyPath))
        }

        $provider = {
            param($indexName, $requestCount)
            Import-KurrentDbClientAssembly -AssemblyPath $assemblyPath

            $baseUri = [uri] $settings.BaseUrl
            $port = if ($baseUri.IsDefaultPort) { 2113 } else { $baseUri.Port }
            $connectionString = if ($settings.PSObject.Properties['ConnectionString'] -and -not [string]::IsNullOrWhiteSpace([string] $settings.ConnectionString)) {
                [string] $settings.ConnectionString
            }
            else {
                "esdb://$($baseUri.Host):${port}?tls=false&tlsVerifyCert=false"
            }

            $credentials = Get-KurrentDbWriteActivityCredentials -Settings $settings

            $reader = [SupportTools.KurrentDbClient.KurrentDbEventReader]::new()
            $events = $reader.ReadRecentEventsAsync(
                $connectionString,
                $indexName,
                $requestCount,
                [timespan]::FromSeconds($timeoutSeconds),
                $credentials.Username,
                $credentials.Password,
                [Threading.CancellationToken]::None).GetAwaiter().GetResult()

            [pscustomobject]@{ events = @($events) }
        }
        if ($Context.PSObject.Properties['KurrentDbWriteActivityProvider']) {
            $provider = $Context.KurrentDbWriteActivityProvider
        }

        $details = foreach ($stream in $streamConfigurations) {
            $streamName = [string] $stream.Name
            $eventCount = [int] $stream.EventCount
            try {
                $response = & $provider $streamName $eventCount
                $entries = if ($response.PSObject.Properties['events']) { @($response.events) } elseif ($response.PSObject.Properties['entries']) { @($response.entries) } else { @($response) }
                $events = foreach ($entry in $entries) {
                    $updated = $null
                    $timestampValue = if ($entry.PSObject.Properties['Timestamp']) { $entry.Timestamp } elseif ($entry.PSObject.Properties['Created']) { $entry.Created } elseif ($entry.PSObject.Properties['updated']) { $entry.updated } else { $null }
                    if ($null -ne $timestampValue -and -not [string]::IsNullOrWhiteSpace([string] $timestampValue)) {
                        $updated = [datetime]::Parse([string] $timestampValue).ToUniversalTime()
                    }
                    [pscustomobject]@{
                        EventId   = if ($entry.PSObject.Properties['EventId']) { [string] $entry.EventId } elseif ($entry.PSObject.Properties['id']) { [string] $entry.id } else { [string] $entry.title }
                        EventType = if ($entry.PSObject.Properties['EventType']) { [string] $entry.EventType } elseif ($entry.PSObject.Properties['summary']) { [string] $entry.summary } else { $null }
                        Timestamp = $updated
                        Title     = if ($entry.PSObject.Properties['Title']) { [string] $entry.Title } elseif ($entry.PSObject.Properties['title']) { [string] $entry.title } else { $null }
                    }
                }
                $latest = @($events | Where-Object { $null -ne $_.Timestamp } | Sort-Object Timestamp -Descending | Select-Object -First 1)
                $status = 'Passed'
                $failureReason = $null
                if ($events.Count -eq 0) {
                    $status = 'Failed'; $failureReason = 'No events were found.'
                }
                elseif ($latest.Count -eq 0) {
                    $status = 'Failed'; $failureReason = 'Events did not contain timestamps.'
                }
                elseif ($stream.PSObject.Properties['MaxLatestEventAgeMinutes'] -and $latest[0].Timestamp -lt [datetime]::UtcNow.AddMinutes(-[double] $stream.MaxLatestEventAgeMinutes)) {
                    $status = 'Failed'; $failureReason = 'Latest event is older than the configured age limit.'
                }
                elseif ($events.Count -lt $eventCount) {
                    $status = 'Warning'; $failureReason = "$($events.Count) event(s) returned; $eventCount requested."
                }

                [pscustomobject]@{ StreamName = $streamName; Status = $status; EventCount = $events.Count; LatestEvent = $latest | Select-Object -First 1; FailureReason = $failureReason; Events = @($events) }
            }
            catch {
                [pscustomobject]@{ StreamName = $streamName; Status = 'Failed'; EventCount = 0; LatestEvent = $null; FailureReason = $_.Exception.Message; Events = @() }
            }
        }

        $failed = @($details | Where-Object Status -eq 'Failed')
        $warnings = @($details | Where-Object Status -eq 'Warning')
        if ($failed.Count -gt 0) {
            $summary = ($failed | ForEach-Object { "$($_.StreamName) [$($_.FailureReason)]" }) -join ', '
            return Get-CheckResult -Name 'KurrentDB Write Activity' -Status Failed -Summary "KurrentDB stream(s) require attention: $summary" -Details @($details) -ErrorMessage $summary
        }
        if ($warnings.Count -gt 0) {
            $summary = ($warnings | ForEach-Object { "$($_.StreamName) [$($_.FailureReason)]" }) -join ', '
            return Get-CheckResult -Name 'KurrentDB Write Activity' -Status Warning -Summary "KurrentDB stream(s) returned fewer events than requested: $summary" -Details @($details)
        }

        Get-CheckResult -Name 'KurrentDB Write Activity' -Status Passed -Summary "All $($details.Count) configured KurrentDB stream(s) contain recent events." -Details @($details)
    }
    catch {
        Get-CheckResult -Name 'KurrentDB Write Activity' -Status Failed -Summary 'KurrentDB write-activity query failed.' -Details $_.Exception.Message -ErrorMessage $_.Exception.ToString()
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
        [pscustomobject]@{ Name = 'KurrentDB Write Activity'; Action = ${function:Test-KurrentDbWriteActivity} }
        [pscustomobject]@{ Name = 'Template Configuration'; Action = ${function:Test-TemplateConfiguration} }
    )
}

function Get-SupportReport {
    [OutputType([pscustomobject])]
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

function ConvertTo-HtmlEncodedText {
    param([object] $Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string] $Value)) {
        return '—'
    }

    [System.Net.WebUtility]::HtmlEncode([string] $Value)
}

function Write-SupportReports {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Report,
        [Parameter(Mandatory)] [string] $OutputPath,
        [int] $RetentionDays = 7
    )

    if ($RetentionDays -lt 1) {
        throw 'RetentionDays must be greater than zero.'
    }

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
        $summaryText = [string] $check.Summary
        if ($summaryText.Length -gt 160) {
            $summaryText = $summaryText.Substring(0, 157).TrimEnd() + '…'
        }
        $summary = [System.Net.WebUtility]::HtmlEncode($summaryText)
        $statusIcon = switch ([string] $check.Status) {
            'Passed' { '🟢' }
            'Warning' { '🟠' }
            'Failed' { '🔴' }
            default { '⚪' }
        }
        [void] $rows.AppendLine("<tr class=`"status-$status`"><td>$name</td><td><span class=`"status-icon`" role=`"img`" aria-label=`"$status`">$statusIcon</span> $status</td><td>$summary</td></tr>")
    }

    $subscriptionDetails = [System.Text.StringBuilder]::new()
    $subscriptionCheck = @($Report.Checks | Where-Object Name -eq 'Subscription Service') | Select-Object -First 1
    $subscriptionItems = if ($null -ne $subscriptionCheck) {
        @($subscriptionCheck.Details | Where-Object { $_.PSObject.Properties['SubscriptionId'] })
    }
    $subscriptionItems = @($subscriptionItems)
    if ($subscriptionItems.Count -gt 0) {
        [void] $subscriptionDetails.AppendLine('<h2>Subscription Service details</h2>')
        [void] $subscriptionDetails.AppendLine('<div class="table-scroll"><table class="subscription-table"><thead><tr><th>Subscription</th><th>Tag</th><th>Status</th><th>Running</th><th>Health</th><th>Parked</th><th>Reason</th></tr></thead><tbody>')
        foreach ($subscription in $subscriptionItems) {
            $detailStatus = ConvertTo-HtmlEncodedText $subscription.Status
            $detailStatusClass = [System.Net.WebUtility]::HtmlEncode("status-$([string] $subscription.Status)")
            $detailStatusIcon = switch ([string] $subscription.Status) {
                'Passed' { '🟢' }
                'Warning' { '🟠' }
                'Failed' { '🔴' }
                default { '⚪' }
            }
            $reasonParts = @(
                if ($subscription.PSObject.Properties['operationalReason'] -and -not [string]::IsNullOrWhiteSpace([string] $subscription.OperationalReason)) { "Operational: $($subscription.OperationalReason)" }
                if ($subscription.PSObject.Properties['runtimeFailureReason'] -and -not [string]::IsNullOrWhiteSpace([string] $subscription.RuntimeFailureReason)) { "Runtime: $($subscription.RuntimeFailureReason)" }
            )
            $reason = ConvertTo-HtmlEncodedText ($reasonParts -join '; ')
            [void] $subscriptionDetails.AppendLine("<tr class=`"$detailStatusClass`"><td>$(ConvertTo-HtmlEncodedText $subscription.SubscriptionId)</td><td>$(ConvertTo-HtmlEncodedText $subscription.Tag)</td><td><span class=`"status-icon`" role=`"img`" aria-label=`"$detailStatus`">$detailStatusIcon</span> $detailStatus</td><td>$(ConvertTo-HtmlEncodedText $subscription.IsRunning)</td><td>$(ConvertTo-HtmlEncodedText $subscription.Health)</td><td>$(ConvertTo-HtmlEncodedText $subscription.ParkedEventCount)</td><td>$reason</td></tr>")
        }
        [void] $subscriptionDetails.AppendLine('</tbody></table></div>')
    }

    $overallStatus = [System.Net.WebUtility]::HtmlEncode([string] $Report.OverallStatus)
    $html = @"
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>Daily Support Check</title>
<style>body{font-family:Segoe UI,Arial,sans-serif;color:#222}table{border-collapse:collapse;width:100%}th,td{border:1px solid #ccc;padding:8px;text-align:left;vertical-align:top}th{background:#eee}h2{font-size:1.1em;margin:22px 0 8px}.table-scroll{overflow-x:auto}.subscription-table{font-size:.9em;min-width:760px}.status-Failed{background:#fde2e2}.status-Warning{background:#fff4cc}.status-Passed{background:#e4f4e4}.status-icon{font-size:1.15em;white-space:nowrap}</style>
</head>
<body><h1>Daily Support Check</h1><p><strong>Overall status:</strong> $overallStatus</p>
<p><strong>Started:</strong> $([System.Net.WebUtility]::HtmlEncode([string] $Report.StartedAt))<br><strong>Completed:</strong> $([System.Net.WebUtility]::HtmlEncode([string] $Report.CompletedAt))</p>
<table><thead><tr><th>Check</th><th>Status</th><th>Summary</th></tr></thead><tbody>$rows</tbody></table>
$subscriptionDetails
</body></html>
"@
    $html | Set-Content -LiteralPath $htmlPath -Encoding UTF8

    $cutoffUtc = [datetime]::UtcNow.AddDays(-$RetentionDays)
    Get-ChildItem -LiteralPath $OutputPath -File | Where-Object {
        $_.Name -like 'daily-support-check-*.json' -or $_.Name -like 'daily-support-check-*.html'
    } | Where-Object { $_.LastWriteTimeUtc -lt $cutoffUtc } | Remove-Item -Force

    [pscustomobject]@{ JsonPath = $jsonPath; HtmlPath = $htmlPath }
}

function Send-SupportReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Report,
        [Parameter(Mandatory)] [object] $ReportPaths,
        [object] $Configuration,
        [scriptblock] $EmailProvider
    )

    $email = if ($Configuration -and $Configuration.PSObject.Properties['Email']) { $Configuration.Email } else { $null }
    if ($null -eq $email -or -not [bool] $email.Enabled) {
        return [pscustomobject]@{
            Transport = 'LocalFiles'
            Status    = 'Ready'
            Paths     = $ReportPaths
            Message   = 'Reports are available as local files. Email delivery is disabled.'
        }
    }

    try {
        $providerName = if ([string]::IsNullOrWhiteSpace([string] $email.Provider)) { 'Brevo' } else { [string] $email.Provider }
        if ($providerName -ne 'Brevo') {
            throw "Unsupported email provider '$providerName'."
        }

        foreach ($required in @('ApiUrl', 'ApiKey', 'From')) {
            if ([string]::IsNullOrWhiteSpace([string] $email.$required)) {
                throw "Email.$required must be configured when email delivery is enabled."
            }
        }

        $recipients = @($email.To | ForEach-Object { [string] $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($recipients.Count -eq 0) {
            throw 'Email.To must contain at least one recipient when email delivery is enabled.'
        }

        $counts = if ($Report.Counts) { $Report.Counts } else { [pscustomobject]@{ Passed = 0; Warning = 0; Failed = 0 } }
        $textLines = @(
            'Daily Support Check'
            "Overall status: $($Report.OverallStatus)"
            "Passed: $($counts.Passed); Warning: $($counts.Warning); Failed: $($counts.Failed)"
        )
        foreach ($check in @($Report.Checks | Where-Object { $_.Status -ne 'Passed' })) {
            $textLines += "[$($check.Status)] $($check.Name): $($check.Summary)"
            if ($check.Error) { $textLines += "Error: $($check.Error)" }
        }
        $textBody = $textLines -join [Environment]::NewLine

        $htmlBody = $null
        if ($ReportPaths.HtmlPath -and (Test-Path -LiteralPath $ReportPaths.HtmlPath)) {
            $htmlBody = Get-Content -LiteralPath $ReportPaths.HtmlPath -Raw
        }
        if ([string]::IsNullOrWhiteSpace($htmlBody)) {
            $htmlBody = "<html><body><h1>Daily Support Check</h1><p>Overall status: $([System.Net.WebUtility]::HtmlEncode([string] $Report.OverallStatus))</p></body></html>"
        }

        $attachments = @()
        $attachHtmlReport = if ($email.PSObject.Properties['AttachHtmlReport']) { [bool] $email.AttachHtmlReport } else { $false }
        $attachJsonReport = if ($email.PSObject.Properties['AttachJsonReport']) { [bool] $email.AttachJsonReport } else { $false }
        $attachmentDefinitions = @(
            [pscustomobject]@{ Enabled = $attachHtmlReport; Path = $ReportPaths.HtmlPath; MimeType = 'text/html' }
            [pscustomobject]@{ Enabled = $attachJsonReport; Path = $ReportPaths.JsonPath; MimeType = 'application/json' }
        )
        foreach ($definition in $attachmentDefinitions) {
            if ($definition.Enabled -and $definition.Path -and (Test-Path -LiteralPath $definition.Path)) {
                $attachments += [pscustomobject]@{
                    name    = [IO.Path]::GetFileName($definition.Path)
                    content = [Convert]::ToBase64String([IO.File]::ReadAllBytes($definition.Path))
                }
            }
        }

        $configuredSubjectPrefix = if ($email.PSObject.Properties['SubjectPrefix']) { [string] $email.SubjectPrefix } else { '' }
        $subjectPrefix = if ([string]::IsNullOrWhiteSpace($configuredSubjectPrefix)) { 'Daily Support Check' } else { $configuredSubjectPrefix }
        $serverName = if ([string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) { 'Unknown Server' } else { $env:COMPUTERNAME }
        $runDate = try { ([datetime]::Parse([string] $Report.StartedAt)).ToString('yyyy-MM-dd') } catch { (Get-Date).ToString('yyyy-MM-dd') }
        $payload = [ordered]@{
            sender     = [ordered]@{ email = [string] $email.From }
            to         = @($recipients | ForEach-Object { [ordered]@{ email = $_ } })
            subject    = "$subjectPrefix - $($Report.OverallStatus) - $serverName - $runDate"
            textContent = $textBody
            htmlContent = $htmlBody
        }
        if ($attachments.Count -gt 0) { $payload.attachments = $attachments }

        if (-not $EmailProvider) {
            $EmailProvider = {
                param($requestUri, $headers, $requestPayload)
                Invoke-RestMethod -Uri $requestUri -Method Post -Headers $headers -ContentType 'application/json' -Body ($requestPayload | ConvertTo-Json -Depth 12)
            }
        }

        $headers = @{
            'api-key' = [string] $email.ApiKey
            Accept = 'application/json'
            'Content-Type' = 'application/json'
        }
        $response = & $EmailProvider ([string] $email.ApiUrl) $headers $payload

        [pscustomobject]@{
            Transport = 'Brevo'
            Status    = 'Sent'
            Paths     = $ReportPaths
            Message   = 'Report sent via Brevo.'
        }
    }
    catch {
        [pscustomobject]@{
            Transport = 'Brevo'
            Status    = 'Failed'
            Paths     = $ReportPaths
            Message   = $_.Exception.Message
        }
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
            ReportRetentionDays = 7
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

    $report = Get-SupportReport -StartedAt $startedAt -Results @($results)
    $paths = Write-SupportReports -Report $report -OutputPath $OutputPath -RetentionDays ([int] $configuration.ReportRetentionDays)
    $transport = Send-SupportReport -Report $report -ReportPaths $paths -Configuration $configuration
    $report | Add-Member -NotePropertyName ReportPaths -NotePropertyValue $paths
    $report | Add-Member -NotePropertyName Transport -NotePropertyValue $transport

    Write-Information "Daily Support Check: $($report.OverallStatus) ($($report.Checks.Count) checks)" -InformationAction Continue
    Write-Information "JSON report: $($paths.JsonPath)" -InformationAction Continue
    Write-Information "HTML report: $($paths.HtmlPath)" -InformationAction Continue

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
