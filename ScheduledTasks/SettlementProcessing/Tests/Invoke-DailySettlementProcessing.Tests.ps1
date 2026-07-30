Describe 'Invoke-DailySettlementProcessing.ps1' {
    It 'gets merchants and posts a settlement for each merchant returned' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') 'Invoke-DailySettlementProcessing.ps1')).Path
        $postedUris = [System.Collections.Generic.List[string]]::new()
        $settlementDate = (Get-Date).ToString('yyyy-MM-dd')
        $captured = [pscustomobject]@{
            TokenCalls    = 0
            MerchantCalls = 0
        }

        Mock Invoke-RestMethod {
            $captured.TokenCalls++
            @{
                access_token = 'access-token'
            }
        } -ParameterFilter {
            $Method -eq 'Post' -and
            $Uri -eq 'https://security.example/connect/token' -and
            $ContentType -eq 'application/x-www-form-urlencoded' -and
            $Body.grant_type -eq 'client_credentials' -and
            $Body.client_id -eq 'client-id' -and
            $Body.client_secret -eq 'client-secret'
        }

        Mock Invoke-RestMethod {
            $captured.MerchantCalls++
            @{
                merchants = @(
                    @{ merchantId = 'merchant-1' },
                    @{ MerchantId = 'merchant-2' }
                )
            }
        } -ParameterFilter {
            $Method -eq 'Get' -and
            $Uri -eq 'https://api.example/api/estates/estate-123/merchants' -and
            $Headers.Authorization -eq 'Bearer access-token'
        }

        Mock Invoke-RestMethod {
            $postedUris.Add($Uri) | Out-Null
        } -ParameterFilter {
            $Method -eq 'Post' -and
            $Uri -like 'https://api.example/api/estates/estate-123/settlements/*/merchants/*' -and
            $Headers.Authorization -eq 'Bearer access-token'
        }

        & $scriptUnderTest `
            -EstateId 'estate-123' `
            -BaseUrl 'https://api.example/' `
            -SecurityServiceUrl 'https://security.example/' `
            -ClientId 'client-id' `
            -ClientSecret 'client-secret'

        $captured.TokenCalls | Should -Be 1
        $captured.MerchantCalls | Should -Be 1
        $postedUris | Should -Be @(
            "https://api.example/api/estates/estate-123/settlements/$settlementDate/merchants/merchant-1",
            "https://api.example/api/estates/estate-123/settlements/$settlementDate/merchants/merchant-2"
        )
    }

    It 'does not post settlements when no merchants are returned' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') 'Invoke-DailySettlementProcessing.ps1')).Path
        $captured = [pscustomobject]@{
            TokenCalls    = 0
            MerchantCalls = 0
            SettlementCalls = 0
        }

        Mock Invoke-RestMethod {
            $captured.TokenCalls++
            @{
                access_token = 'access-token'
            }
        } -ParameterFilter {
            $Method -eq 'Post' -and
            $Uri -eq 'https://security.example/connect/token' -and
            $ContentType -eq 'application/x-www-form-urlencoded' -and
            $Body.grant_type -eq 'client_credentials' -and
            $Body.client_id -eq 'client-id' -and
            $Body.client_secret -eq 'client-secret'
        }

        Mock Invoke-RestMethod {
            $captured.MerchantCalls++
            @()
        } -ParameterFilter {
            $Method -eq 'Get' -and
            $Uri -eq 'https://api.example/api/estates/estate-123/merchants' -and
            $Headers.Authorization -eq 'Bearer access-token'
        }

        Mock Invoke-RestMethod {
            $captured.SettlementCalls++
        } -ParameterFilter {
            $Method -eq 'Post' -and
            $Uri -like 'https://api.example/api/estates/estate-123/settlements/*/merchants/*'
        }

        & $scriptUnderTest `
            -EstateId 'estate-123' `
            -BaseUrl 'https://api.example/' `
            -SecurityServiceUrl 'https://security.example/' `
            -ClientId 'client-id' `
            -ClientSecret 'client-secret'

        $captured.TokenCalls | Should -Be 1
        $captured.MerchantCalls | Should -Be 1
        $captured.SettlementCalls | Should -Be 0
    }
}
