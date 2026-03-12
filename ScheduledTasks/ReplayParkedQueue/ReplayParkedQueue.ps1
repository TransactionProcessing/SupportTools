param (
    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter]
    [string]$Username,

    [Parameter]
    [string]$Password,

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
            # Avoid failing the run due to cleanup issues
        }
    }

Write-Trace "INFO" "Script started. BaseUrl=$BaseUrl"

# =========================
# Auth header
# =========================
$AuthHeader = @{
    Authorization = "Basic " +
        [Convert]::ToBase64String(
            [Text.Encoding]::ASCII.GetBytes("$Username`:$Password")
        )
}

# =========================
# Query subscriptions
# =========================
try {
    Write-Trace "INFO" "Querying persistent subscriptions"
    $subscriptions = Invoke-RestMethod `
        -Method GET `
        -Uri "$BaseUrl/subscriptions" `
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

    $stream = $sub.eventStreamId
    $group  = $sub.groupName

    #Write-Trace "INFO" "Processing subscription [$stream][$group]"

    $streamEncoded = if ($stream -eq '$all') {
        '%24all'
    }
    else {
        [System.Web.HttpUtility]::UrlEncode($stream)
    }

    $infoUrl = "$BaseUrl/subscriptions/$streamEncoded/$group/info"

    try {
        $info = Invoke-RestMethod `
            -Method GET `
            -Uri $infoUrl `
            -Headers $AuthHeader
    }
    catch {
        Write-Trace "WARN" "Failed to get info for [$stream][$group]: $($_.Exception.Message)"
        continue
    }

    $parkedCount = $info.parkedMessageCount
    #Write-Trace "INFO" "[$stream][$group] parkedMessageCount=$parkedCount"

    if ($parkedCount -gt 0) {
        $replayUrl = "$BaseUrl/subscriptions/$streamEncoded/$group/replayParked?from=0"

        Write-Trace "INFO" "Replaying [$parkedCount] parked messages for [$stream][$group]"

        try {
            Invoke-RestMethod `
                -Method POST `
                -Uri $replayUrl `
                -Headers $AuthHeader

            Write-Trace "INFO" "Replay triggered successfully for [$stream][$group]"
        }
        catch {
            Write-Trace "ERROR" "Replay failed for [$stream][$group]: $($_.Exception.Message)"
        }
    }
    #else {
        #Write-Trace "INFO" "No parked messages for [$stream][$group]"
    #}
}

Write-Trace "INFO" "Script completed"