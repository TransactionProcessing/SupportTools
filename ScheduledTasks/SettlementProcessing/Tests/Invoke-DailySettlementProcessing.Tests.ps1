Describe 'Invoke-DailySettlementProcessing.ps1' {
    It 'gets merchants and posts a settlement for each merchant returned' {
        $postedUris = [System.Collections.Generic.List[string]]::new()
        $settlementDate = (Get-Date).ToString('yyyy-MM-dd')
        $scriptUnderTest = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') 'Invoke-DailySettlementProcessing.ps1')).Path

        Mock Invoke-RestMethod {
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

        Assert-MockCalled Invoke-RestMethod -Times 1 -ParameterFilter {
            $Method -eq 'Post' -and $Uri -eq 'https://security.example/connect/token'
        }

        Assert-MockCalled Invoke-RestMethod -Times 1 -ParameterFilter {
            $Method -eq 'Get' -and $Uri -eq 'https://api.example/api/v2/estates/estate-123/merchants'
        }

        $postedUris | Should -Be @(
            "https://api.example/api/estates/estate-123/settlements/$settlementDate/merchants/merchant-1",
            "https://api.example/api/estates/estate-123/settlements/$settlementDate/merchants/merchant-2"
        )
    }
}
