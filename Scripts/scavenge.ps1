param(
    [string]$BaseUrl = "http://localhost:2113",
    [string]$Username,
    [string]$Password
)

$BaseUrl = $BaseUrl.TrimEnd('/')
$uri = "$BaseUrl/admin/scavenge"

$headers = @{
    Accept = 'application/json'
    'Content-Type' = 'application/json'
}

if ($Username -and $Password) {
    $pair = "$Username`:$Password"
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($pair)
    $encoded = [Convert]::ToBase64String($bytes)
    $headers['Authorization'] = "Basic $encoded"
}

try {
    Invoke-RestMethod -Method Post -Uri $uri -Headers $headers
}
catch {
    Write-Error "POST to $uri failed: $($_.Exception.Message)"
    exit 1
}