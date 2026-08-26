param(
    [string]$BaseUrl = "http://localhost:2113",
    [string]$Username,
    [System.Security.SecureString]$Password,
    [System.Management.Automation.PSCredential]$Credential
)

$BaseUrl = $BaseUrl.TrimEnd('/')
$uri = "$BaseUrl/admin/scavenge"

$headers = @{
    Accept = 'application/json'
    'Content-Type' = 'application/json'
}

if ($Credential) {
    $pair = "$($Credential.UserName)`:$($Credential.GetNetworkCredential().Password)"
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($pair)
    $encoded = [Convert]::ToBase64String($bytes)
    $headers['Authorization'] = "Basic $encoded"
}
elseif (-not [string]::IsNullOrWhiteSpace($Username) -and -not [string]::IsNullOrWhiteSpace($Password)) {
    $usernameCredential = [System.Management.Automation.PSCredential]::new($Username, $Password)
    $pair = "$($usernameCredential.UserName)`:$($usernameCredential.GetNetworkCredential().Password)"
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
