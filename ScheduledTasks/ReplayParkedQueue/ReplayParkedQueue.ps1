param (
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter()]
    [System.Management.Automation.PSCredential]$Credential,

    [string]$LogDirectory = "C:\home\txnproc\trace",
    [int]$LogRetentionDays = 7
)

# =========================
# Logging setup
# =========================
if (-not (Test-Path $LogDirectory)) {
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
}

function Get-LogFilePath {
    $date = (Get-Date).ToString("yyyy-MM-dd")
    Join-Path $LogDirectory "ReplayParkedSubscriptions-$date.log"
}

function Write-Trace {
    param (
        [string]$Level,
        [string]$Message
    )

    $timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff")
    $entry = "$timestamp [$Level] $Message"
    Add-Content -Path (Get-LogFilePath) -Value $entry
}

# =========================
# Log retention cleanup
# =========================
$cutoffDate = (Get-Date).Date.AddDays(-$LogRetentionDays)

Get-ChildItem -Path $LogDirectory -Filter "ReplayParkedSubscriptions-*.log" -File |
    Where-Object {
        if ($_.Name -match 'ReplayParkedSubscriptions-(\d{4}-\d{2}-\d{2})\.log') {
            [DateTime]::ParseExact($matches[1], 'yyyy-MM-dd', $null) -lt $cutoffDate
        }
        else {
            $false
        }
    } |
    ForEach-Object {
        try {
            Remove-Item $_.FullName -Force
        }
        catch {
            Write-Trace "WARN" "Failed to remove stale log file $($_.FullName): $($_.Exception.Message)"
        }
    }

Write-Trace "INFO" "Script started. BaseUrl=$BaseUrl"

# =========================
# Auth header
# =========================
$AuthHeader = @{}
if ($null -ne $Credential) {
    $networkCredential = $Credential.GetNetworkCredential()
    $username = $networkCredential.UserName
    $password = $networkCredential.Password

    $AuthHeader.Authorization = "Basic " +
        [Convert]::ToBase64String(
            [Text.Encoding]::ASCII.GetBytes("$username`:$password")
        )
}

# =========================
# Query subscriptions
# =========================
try {
    Write-Trace "INFO" "Querying persistent subscriptions"
    $subscriptions = Invoke-RestMethod `
        -Method GET `
        -Uri "$BaseUrl/subscriptions/status" `
        -Headers $AuthHeader
}
catch {
    Write-Trace "ERROR" "Failed to query subscriptions: $($_.Exception.Message)"
    exit 1
}

# =========================
# Process subscriptions
# =========================
foreach ($sub in $subscriptions) {

    $subscriptionId = $sub.subscriptionId
    $infoUrl = "$BaseUrl/subscriptions/$subscriptionId/status"

    try {
        $info = Invoke-RestMethod `
            -Method GET `
            -Uri $infoUrl `
            -Headers $AuthHeader
    }
    catch {
        Write-Trace "WARN" "Failed to get info for [$subscriptionId]: $($_.Exception.Message)"
        continue
    }
    #Write-Trace "INFO" "[$subscriptionId] $info"
    $parkedEventCount = $info.parkedEventCount
    #Write-Trace "INFO" "[$subscriptionId] parkedEventCount=$parkedEventCount"

    if ($parkedEventCount -gt 0) {
        $replayUrl = "$BaseUrl/subscriptions/$subscriptionId/replay"

        Write-Trace "INFO" "Replaying [$parkedEventCount] parked messages for [$subscriptionId]"

        try {
            Invoke-RestMethod `
                -Method POST `
                -Uri $replayUrl `
                -Headers $AuthHeader

            Write-Trace "INFO" "Replay triggered successfully for [$subscriptionId]"
        }
        catch {
            Write-Trace "ERROR" "Replay failed for [$subscriptionId]: $($_.Exception.Message)"
        }
    }
    #else {
    #    Write-Trace "INFO" "No parked messages for [$subscriptionId]"
    #}
}

Write-Trace "INFO" "Script completed"
