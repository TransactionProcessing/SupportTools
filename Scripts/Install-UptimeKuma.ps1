param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$InstallPath,

    [ValidateRange(1, 65535)]
    [int]$Port = 3001,

    [string]$HostAddress = "0.0.0.0"
)

$ErrorActionPreference = "Stop"

$ServiceName = "UptimeKuma"
$ServiceDisplayName = "Uptime Kuma"
$RepoUrl = "https://github.com/louislam/uptime-kuma.git"

$WinSWVersion = "v2.12.0"
$WinSWUrl = "https://github.com/winsw/winsw/releases/download/$WinSWVersion/WinSW-x64.exe"

$ServiceExe = Join-Path $InstallPath "UptimeKumaService.exe"
$ServiceConfig = Join-Path $InstallPath "UptimeKumaService.xml"
$LogDirectory = Join-Path $InstallPath "service-logs"

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan
}

function Test-Administrator {
    $currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)

    return $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )
}

function Test-Command {
    param([string]$Command)

    return [bool](Get-Command $Command -ErrorAction SilentlyContinue)
}

function Get-LatestUptimeKumaRelease {
    Write-Host "Finding latest stable Uptime Kuma release..."

    $release = Invoke-RestMethod `
        -Uri "https://api.github.com/repos/louislam/uptime-kuma/releases/latest" `
        -Headers @{
            "User-Agent" = "UptimeKuma-Windows-Installer"
        }

    return $release.tag_name
}

if (-not (Test-Administrator)) {
    throw "This script must be run from PowerShell as Administrator."
}

Write-Step "Checking prerequisites"

if (-not (Test-Command "git")) {
    throw "Git is not installed or is not available in PATH."
}

if (-not (Test-Command "node")) {
    throw "Node.js is not installed or is not available in PATH."
}

if (-not (Test-Command "npm")) {
    throw "npm is not installed or is not available in PATH."
}

$NodeCommand = (Get-Command node).Source

Write-Host "Git:       $(git --version)"
Write-Host "Node.js:   $(node --version)"
Write-Host "npm:       $(npm --version)"
Write-Host "Node Path: $NodeCommand"

Write-Step "Preparing installation location"

$ParentDirectory = Split-Path -Parent $InstallPath

if ([string]::IsNullOrWhiteSpace($ParentDirectory)) {
    throw "InstallPath must include a parent directory, for example C:\Services\UptimeKuma"
}

if (-not (Test-Path $ParentDirectory)) {
    New-Item `
        -Path $ParentDirectory `
        -ItemType Directory `
        -Force | Out-Null
}

$ExistingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if ($ExistingService) {
    Write-Step "Stopping existing Uptime Kuma service"

    if ($ExistingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force

        $ExistingService.WaitForStatus(
            "Stopped",
            (New-TimeSpan -Seconds 30)
        )
    }

    Write-Host "Service stopped."
}

$GitDirectory = Join-Path $InstallPath ".git"

if (-not (Test-Path $GitDirectory)) {

    Write-Step "Installing Uptime Kuma"

    if (Test-Path $InstallPath) {

        $ExistingItems = Get-ChildItem `
            -Path $InstallPath `
            -Force `
            -ErrorAction SilentlyContinue

        if ($ExistingItems) {
            throw @"
The installation directory already exists and is not empty:

$InstallPath

Uptime Kuma cannot be installed into a non-empty directory.

If this is only a failed first-time installation, remove the directory and run the script again.
Otherwise choose a different -InstallPath.
"@
        }

        Remove-Item $InstallPath -Force
    }

    git clone $RepoUrl $InstallPath

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to clone the Uptime Kuma repository."
    }
}
else {
    Write-Step "Existing Uptime Kuma installation found"
    Write-Host "Updating repository..."
}

$LatestVersion = Get-LatestUptimeKumaRelease

Write-Host ""
Write-Host "Latest stable version: $LatestVersion" -ForegroundColor Green

Push-Location $InstallPath

try {
    Write-Step "Updating source to $LatestVersion"

    git fetch --all --tags
    if ($LASTEXITCODE -ne 0) {
        throw "git fetch failed."
    }

    git checkout $LatestVersion --force
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to checkout Uptime Kuma version $LatestVersion."
    }

    Write-Step "Installing production dependencies"

    npm install --omit dev --no-audit
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed."
    }

    Write-Step "Downloading Uptime Kuma frontend"

    npm run download-dist
    if ($LASTEXITCODE -ne 0) {
        throw "npm run download-dist failed."
    }
}
finally {
    Pop-Location
}

if (-not (Test-Path $LogDirectory)) {
    New-Item `
        -Path $LogDirectory `
        -ItemType Directory `
        -Force | Out-Null
}

Write-Step "Configuring Windows Service"

if (-not (Test-Path $ServiceExe)) {
    Write-Host "Downloading WinSW $WinSWVersion..."

    Invoke-WebRequest `
        -Uri $WinSWUrl `
        -OutFile $ServiceExe `
        -UseBasicParsing

    Write-Host "WinSW downloaded."
}
else {
    Write-Host "WinSW already installed."
}

$EscapedNodePath = [System.Security.SecurityElement]::Escape($NodeCommand)
$EscapedInstallPath = [System.Security.SecurityElement]::Escape($InstallPath)
$EscapedLogDirectory = [System.Security.SecurityElement]::Escape($LogDirectory)
$EscapedHostAddress = [System.Security.SecurityElement]::Escape($HostAddress)

$Xml = @"
<service>
    <id>$ServiceName</id>
    <name>$ServiceDisplayName</name>
    <description>Uptime Kuma monitoring service</description>

    <executable>$EscapedNodePath</executable>
    <arguments>server/server.js --host=$EscapedHostAddress --port=$Port</arguments>
    <workingdirectory>$EscapedInstallPath</workingdirectory>

    <startmode>Automatic</startmode>

    <onfailure action="restart" delay="10 sec"/>
    <onfailure action="restart" delay="30 sec"/>
    <onfailure action="restart" delay="60 sec"/>
    <resetfailure>1 hour</resetfailure>

    <logpath>$EscapedLogDirectory</logpath>
    <log mode="roll-by-size">
        <sizeThreshold>10240</sizeThreshold>
        <keepFiles>5</keepFiles>
    </log>
</service>
"@

Set-Content -Path $ServiceConfig -Value $Xml -Encoding UTF8

Write-Host "Service configuration created:"
Write-Host $ServiceConfig

$ExistingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue

if (-not $ExistingService) {
    Write-Step "Installing Windows Service"

    & $ServiceExe install

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install Uptime Kuma Windows Service."
    }
}
else {
    Write-Host "Windows Service already installed."
    & $ServiceExe refresh

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to refresh Uptime Kuma Windows Service."
    }
}

Write-Step "Configuring service startup"

Set-Service -Name $ServiceName -StartupType Automatic

Write-Step "Starting Uptime Kuma"

Start-Service -Name $ServiceName

$Service = Get-Service -Name $ServiceName
$Service.WaitForStatus(
    "Running",
    (New-TimeSpan -Seconds 30)
)

Write-Step "Checking Windows Firewall"

$FirewallRuleName = "Uptime Kuma TCP $Port"

$ExistingFirewallRule = Get-NetFirewallRule `
    -DisplayName $FirewallRuleName `
    -ErrorAction SilentlyContinue

if (-not $ExistingFirewallRule) {
    New-NetFirewallRule `
        -DisplayName $FirewallRuleName `
        -Direction Inbound `
        -Protocol TCP `
        -LocalPort $Port `
        -Action Allow `
        -Profile Private | Out-Null

    Write-Host "Firewall rule created for TCP port $Port."
}
else {
    Write-Host "Firewall rule already exists."
}

Write-Step "Uptime Kuma installation/update complete"

$Service = Get-Service -Name $ServiceName

Write-Host ""
Write-Host "Version:      $LatestVersion"
Write-Host "Service:      $($Service.DisplayName)"
Write-Host "Status:       $($Service.Status)"
Write-Host "Startup:      Automatic"
Write-Host "Install Path: $InstallPath"
Write-Host "Port:         $Port"
Write-Host ""
Write-Host "Local URL:" -ForegroundColor Green
Write-Host "http://localhost:$Port"
Write-Host ""
Write-Host "Service commands:"
Write-Host "  Get-Service UptimeKuma"
Write-Host "  Start-Service UptimeKuma"
Write-Host "  Stop-Service UptimeKuma"
Write-Host "  Restart-Service UptimeKuma"
