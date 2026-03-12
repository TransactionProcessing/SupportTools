[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$EstateId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SecurityServiceUrl,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ClientId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ClientSecret
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-AccessToken {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SecurityServiceUrl,

        [Parameter(Mandatory = $true)]
        [string]$ClientId,

        [Parameter(Mandatory = $true)]
        [string]$ClientSecret
    )

    $tokenEndpoint = '{0}/connect/token' -f $SecurityServiceUrl.TrimEnd('/')
    Write-Verbose "Requesting access token from [$tokenEndpoint]"

    $tokenResponse = Invoke-RestMethod -Method Post `
                                       -Uri $tokenEndpoint `
                                       -ContentType 'application/x-www-form-urlencoded' `
                                       -Body @{
                                           grant_type    = 'client_credentials'
                                           client_id     = $ClientId
                                           client_secret = $ClientSecret
                                       }

    if ($tokenResponse -is [hashtable]) {
        foreach ($propertyName in 'access_token', 'AccessToken', 'token', 'Token') {
            if ($tokenResponse.ContainsKey($propertyName)) {
                $accessToken = [string]$tokenResponse[$propertyName]
                if (-not [string]::IsNullOrWhiteSpace($accessToken)) {
                    return $accessToken
                }
            }
        }
    }
    else {
        foreach ($propertyName in 'access_token', 'AccessToken', 'token', 'Token') {
            $property = $tokenResponse.PSObject.Properties[$propertyName]
            if ($null -ne $property) {
                $accessToken = [string]$property.Value
                if (-not [string]::IsNullOrWhiteSpace($accessToken)) {
                    return $accessToken
                }
            }
        }
    }

    throw 'Token response did not contain an access token.'
}

function Get-MerchantItems {
    param(
        [Parameter(Mandatory = $false)]
        $MerchantResponse
    )

    if ($null -eq $MerchantResponse) {
        return @()
    }

    if ($MerchantResponse -is [string]) {
        return @($MerchantResponse)
    }

    if ($MerchantResponse -is [hashtable]) {
        foreach ($propertyName in 'merchants', 'Merchants', 'data', 'Data', 'items', 'Items', 'value', 'Value') {
            if ($MerchantResponse.ContainsKey($propertyName)) {
                return @(Get-MerchantItems -MerchantResponse $MerchantResponse[$propertyName])
            }
        }

        return @($MerchantResponse)
    }

    if ($MerchantResponse -is [System.Collections.IEnumerable] -and $MerchantResponse -isnot [psobject]) {
        return @($MerchantResponse)
    }

    foreach ($propertyName in 'merchants', 'Merchants', 'data', 'Data', 'items', 'Items', 'value', 'Value') {
        $property = $MerchantResponse.PSObject.Properties[$propertyName]
        if ($null -ne $property) {
            return @(Get-MerchantItems -MerchantResponse $property.Value)
        }
    }

    if ($MerchantResponse -is [System.Collections.IEnumerable] -and $MerchantResponse -isnot [string]) {
        return @($MerchantResponse)
    }

    return @($MerchantResponse)
}

function Get-MerchantId {
    param(
        [Parameter(Mandatory = $true)]
        $Merchant
    )

    if ($Merchant -is [string]) {
        return $Merchant
    }

    if ($Merchant -is [hashtable]) {
        foreach ($propertyName in 'merchantId', 'MerchantId', 'id', 'Id') {
            if ($Merchant.ContainsKey($propertyName)) {
                $merchantId = [string]$Merchant[$propertyName]
                if (-not [string]::IsNullOrWhiteSpace($merchantId)) {
                    return $merchantId
                }
            }
        }
    }
    else {
        foreach ($propertyName in 'merchantId', 'MerchantId', 'id', 'Id') {
            $property = $Merchant.PSObject.Properties[$propertyName]
            if ($null -ne $property) {
                $merchantId = [string]$property.Value
                if (-not [string]::IsNullOrWhiteSpace($merchantId)) {
                    return $merchantId
                }
            }
        }
    }

    throw 'Unable to determine merchant id from merchant response.'
}

function Invoke-DailySettlementProcessing {
    param(
        [Parameter(Mandatory = $true)]
        [string]$EstateId,

        [Parameter(Mandatory = $true)]
        [string]$BaseUrl,

        [Parameter(Mandatory = $true)]
        [string]$SecurityServiceUrl,

        [Parameter(Mandatory = $true)]
        [string]$ClientId,

        [Parameter(Mandatory = $true)]
        [string]$ClientSecret
    )

    $settlementDate = (Get-Date).ToString('yyyy-MM-dd')
    $accessToken = Get-AccessToken -SecurityServiceUrl $SecurityServiceUrl -ClientId $ClientId -ClientSecret $ClientSecret
    $headers = @{
        Authorization = "Bearer $accessToken"
    }

    $normalizedBaseUrl = $BaseUrl.TrimEnd('/')
    $merchantsEndpoint = '{0}/api/v2/estates/{1}/merchants' -f $normalizedBaseUrl, $EstateId
    Write-Verbose "Requesting merchants from [$merchantsEndpoint]"

    $merchantResponse = Invoke-RestMethod -Method Get -Uri $merchantsEndpoint -Headers $headers
    $merchantIds = @(Get-MerchantItems -MerchantResponse $merchantResponse | ForEach-Object { Get-MerchantId -Merchant $_ })

    if ($merchantIds.Count -eq 0) {
        Write-Verbose "No merchants were returned for estate [$EstateId]"
        return
    }

    foreach ($merchantId in $merchantIds) {
        $settlementEndpoint = '{0}/api/estates/{1}/settlements/{2}/merchants/{3}' -f $normalizedBaseUrl, $EstateId, $settlementDate, $merchantId
        Write-Verbose "Submitting settlement for merchant [$merchantId] using [$settlementEndpoint]"
        Invoke-RestMethod -Method Post -Uri $settlementEndpoint -Headers $headers
    }
}

Invoke-DailySettlementProcessing @PSBoundParameters
