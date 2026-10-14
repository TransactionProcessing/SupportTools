#requires -Version 5.1
#requires -RunAsAdministrator
<#
.SYNOPSIS
Updates KurrentDB through Chocolatey and deploys it to a fixed service directory.
.DESCRIPTION
Run from an elevated 64-bit PowerShell window. Downloads the latest stable
package (no --pre). Archives the old service directory outside Chocolatey's lib
folder before uninstalling the registered Chocolatey package and reinstalling it.
Existing service account/settings are preserved. Requires Chocolatey 2.x or newer.
Archives contain the service directory only, NOT an external database backup.
Keep database/index paths outside the service directory in your YAML config.
Automatic binary restoration is attempted only BEFORE the new server is started.
After startup has been attempted, manual recovery is required: an older binary
may not be compatible with data touched by the newer server.
.PARAMETER Source
Optional Chocolatey feed. Omit to use your configured Chocolatey sources.
.PARAMETER Version
Optional exact stable package version, for a controlled update.
.EXAMPLE
.\Update-KurrentDB.ps1
.EXAMPLE
.\Update-KurrentDB.ps1 -Version 26.1.2
#>
[CmdletBinding()]
param(
    [string]$ServiceName = 'KurrentDB',
    [string]$PackageRoot = 'C:\ProgramData\chocolatey\lib\kurrentdb',
    [string]$ConfigPath = 'C:\home\txnproc\config\kurrentdb-config.yaml',
    [string]$ArchiveRoot = 'C:\ProgramData\KurrentDB-Archives',
    [string]$Source,
    [string]$Version,
    [ValidateRange(10, 600)][int]$ServiceTimeoutSeconds = 120
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Native failures are checked explicitly, including in PowerShell 7.
$PSNativeCommandUseErrorActionPreference = $false
if (-not [Environment]::Is64BitProcess) { throw 'Run this script in 64-bit PowerShell.' }
if ($Version -and $Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    throw 'Version must be an exact stable numeric version, for example 26.1.2.'
}
$choco = (Get-Command choco.exe -ErrorAction Stop).Source
$PackageRoot = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
$ArchiveRoot = [IO.Path]::GetFullPath($ArchiveRoot).TrimEnd('\')
$serviceDirectory = Join-Path $PackageRoot 'kurrentservice'
$executable = Join-Path $serviceDirectory 'KurrentDB.exe'
$binaryPath = '"{0}" --config="{1}"' -f $executable, $ConfigPath
if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { throw "Config not found: $ConfigPath" }
if ($ArchiveRoot.Equals($PackageRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $ArchiveRoot.StartsWith($PackageRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ArchiveRoot must be outside the Chocolatey package directory.'
}
function Stop-KurrentService {
    $current = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $current -and $current.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -ErrorAction Stop
        $current.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped,
            [TimeSpan]::FromSeconds($ServiceTimeoutSeconds))
    }
}
function Start-KurrentService {
    Start-Service -Name $ServiceName -ErrorAction Stop
    $current = Get-Service -Name $ServiceName
    $current.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds($ServiceTimeoutSeconds))
    # Catch immediate process exits; this is a service check, not database readiness.
    Start-Sleep -Seconds 5
    $current.Refresh()
    if ($current.Status -ne 'Running') { throw 'KurrentDB exited shortly after starting. Check its logs.' }
}
function Copy-DirectoryContents([string]$From, [string]$To) {
    New-Item -ItemType Directory -Path $To -Force | Out-Null
    Get-ChildItem -LiteralPath $From -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $To -Recurse -Force
    }
}
$mutex = New-Object Threading.Mutex($false, 'Global\KurrentDB-Chocolatey-Update')
$locked = $false
$archive = $null
$archiveComplete = $false
$staging = $null
$wasRunning = $false
$startupAttempted = $false
$replacementStarted = $false
$createdService = $false
$stoppedByScript = $false
try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (-not $locked) { throw 'Another KurrentDB update is already running.' }
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -ne $service) {
        $info = Get-CimInstance Win32_Service | Where-Object { $_.Name -eq $ServiceName }
        # Refuse to restart a different executable/config by accident.
        $normalized = $info.PathName.Replace('"', '').Trim()
        $expected = ('{0} --config={1}' -f $executable, $ConfigPath)
        if (-not $normalized.Equals($expected, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Existing service has a different command line: $($info.PathName). Expected: $binaryPath"
        }
        if ($info.StartMode -eq 'Disabled') { throw 'The existing service is disabled. Enable it before updating.' }
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Existing service executable not found: $executable" }
        $wasRunning = $service.Status -eq 'Running'
        Write-Host "Stopping $ServiceName..."
        Stop-KurrentService
        $stoppedByScript = $true
    }
    $runId = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    New-Item -ItemType Directory -Path $ArchiveRoot -Force | Out-Null
    if (Test-Path -LiteralPath $serviceDirectory -PathType Container) {
        $archive = Join-Path $ArchiveRoot "kurrentservice-$runId"
        Write-Host "Archiving existing directory to $archive"
        Copy-DirectoryContents $serviceDirectory $archive
        # Only mark the archive usable after the entire copy succeeds.
        $archiveComplete = $true
    } else { $archiveComplete = $false }
    # Chocolatey 2.x lists locally registered packages by default.
    # Capture output so we can distinguish an installed package from leftover files.
    Write-Host 'Checking the Chocolatey installed-package list...'
    $packageList = @(& $choco list kurrentdb --exact --limit-output)
    $listExitCode = $LASTEXITCODE
    if ($listExitCode -notin @(0, 2)) {
        throw "Cannot query Chocolatey installed packages (exit code $listExitCode)."
    }
    $registeredPackage = @($packageList | Where-Object { $_ -match '^kurrentdb\|' })
    if ($registeredPackage.Count -gt 0) {
        Write-Host 'Uninstalling the currently registered KurrentDB Chocolatey package...'
        & $choco uninstall kurrentdb --yes --no-progress --limit-output
        $uninstallExitCode = $LASTEXITCODE
        if ($uninstallExitCode -notin @(0, 1605, 1614, 1641, 3010)) {
            throw "Chocolatey uninstall failed with exit code $uninstallExitCode."
        }
        if ($uninstallExitCode -in @(1641, 3010)) {
            throw 'Chocolatey uninstall reports a reboot is needed. Reboot and rerun this script.'
        }
        # Do not proceed with an install that could silently skip an existing registration.
        $remainingPackages = @(& $choco list kurrentdb --exact --limit-output)
        $verificationExitCode = $LASTEXITCODE
        if ($verificationExitCode -notin @(0, 2)) {
            throw "Cannot verify Chocolatey uninstall (exit code $verificationExitCode)."
        }
        if (@($remainingPackages | Where-Object { $_ -match '^kurrentdb\|' }).Count -gt 0) {
            throw 'KurrentDB is still registered in Chocolatey after uninstall. Installation aborted.'
        }
    } else {
        Write-Host 'KurrentDB is not registered in Chocolatey; proceeding with installation.'
    }
    if ($Version) { Write-Host "Installing KurrentDB package version $Version..." }
    else { Write-Host 'Installing the latest stable KurrentDB package...' }
    $arguments = @('install', 'kurrentdb', '--yes', '--no-progress', '--limit-output')
    if ($Source) { $arguments += "--source=$Source" }
    if ($Version) { $arguments += "--version=$Version" }
    & $choco @arguments
    $chocoExitCode = $LASTEXITCODE
    if ($chocoExitCode -notin @(0, 2, 1641, 3010)) { throw "Chocolatey failed with exit code $chocoExitCode. See Chocolatey's log." }
    if ($chocoExitCode -in @(1641, 3010)) { throw 'Chocolatey reports a reboot is needed. Reboot and rerun this script before deploying.' }
    $nuspecPath = Join-Path $PackageRoot 'kurrentdb.nuspec'
    if (-not (Test-Path -LiteralPath $nuspecPath -PathType Leaf)) { throw "Package metadata missing: $nuspecPath" }
    [xml]$nuspec = Get-Content -LiteralPath $nuspecPath -Raw
    $versionNode = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='version']")
    if ($null -eq $versionNode) { throw 'Cannot read installed package version.' }
    $installedVersion = $versionNode.InnerText.Trim()
    if ($installedVersion -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw "Refusing non-stable package version: $installedVersion" }
    if ($Version -and $installedVersion -ne $Version) { throw "Requested $Version but installed package is $installedVersion. Check package pins." }
    $sourceDirectory = Join-Path $PackageRoot "kurrentdb-$installedVersion-windows.x64"
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory 'KurrentDB.exe') -PathType Leaf)) {
        throw "Expected version directory missing or incomplete: $sourceDirectory"
    }
    $staging = Join-Path $ArchiveRoot "staging-$runId"
    Copy-DirectoryContents $sourceDirectory $staging
    Write-Host "Deploying KurrentDB $installedVersion to $serviceDirectory"
    Stop-KurrentService
    $replacementStarted = $true
    if (Test-Path -LiteralPath $serviceDirectory) { Remove-Item -LiteralPath $serviceDirectory -Recurse -Force }
    Copy-DirectoryContents $staging $serviceDirectory
    if ($null -eq $service) {
        Write-Host "Creating service $ServiceName..."
        New-Service -Name $ServiceName -DisplayName $ServiceName -BinaryPathName $binaryPath -StartupType Automatic | Out-Null
        $createdService = $true
    }
    $startupAttempted = $true
    Write-Host "Starting $ServiceName..."
    Start-KurrentService
    Write-Host "KurrentDB $installedVersion deployed. Service is running." -ForegroundColor Green
    if ($archive) { Write-Host "Previous installation archive: $archive" }
    Write-Host 'Service status was verified; database readiness was not checked.'
} catch {
    $failure = $_
    if ($locked) {
        try {
            if ($startupAttempted) {
                Stop-KurrentService
                Write-Warning "Startup was attempted. No automatic downgrade was performed. Review KurrentDB logs and database compatibility before restoring binaries from $archive."
            } elseif ($archive -and $archiveComplete) {
                Stop-KurrentService
                if (Test-Path -LiteralPath $serviceDirectory) { Remove-Item -LiteralPath $serviceDirectory -Recurse -Force }
                Copy-DirectoryContents $archive $serviceDirectory
                if ($wasRunning) { Start-KurrentService }
                Write-Warning 'Previous service directory restored. Chocolatey package registration may differ from the restored binaries; rerun after resolving the failure.'
            } elseif ($stoppedByScript -and $wasRunning -and -not $replacementStarted) {
                Start-KurrentService
            }
        } catch { Write-Warning "Recovery also failed: $($_.Exception.Message). Archive: $archive" }
        if ($createdService) { Write-Warning 'The newly created service remains registered for troubleshooting.' }
    }
    throw $failure
} finally {
    if ($staging -and (Test-Path -LiteralPath $staging)) {
        try { Remove-Item -LiteralPath $staging -Recurse -Force } catch { Write-Warning "Staging cleanup failed: $staging" }
    }
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
