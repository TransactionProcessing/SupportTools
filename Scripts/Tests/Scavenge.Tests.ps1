Describe 'scavenge.ps1' {
    It 'posts to /admin/scavenge after trimming a trailing slash from BaseUrl' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'scavenge.ps1')).Path
        $expectedUri = 'http://localhost:2113/admin/scavenge'
        $captured = [pscustomobject]@{
            Count = 0
            Uri = $null
            Headers = $null
        }

        Mock Invoke-RestMethod {
            $captured.Count++
            $captured.Uri = $Uri
            $captured.Headers = $Headers
            @{ status = 'ok' }
        } -ParameterFilter {
            $Method -eq 'Post'
        }

        & $scriptUnderTest -BaseUrl 'http://localhost:2113/'

        $captured.Count | Should -Be 1
        $captured.Uri | Should -Be $expectedUri
        $captured.Headers.Accept | Should -Be 'application/json'
        $captured.Headers.'Content-Type' | Should -Be 'application/json'
        $captured.Headers.ContainsKey('Authorization') | Should -BeFalse
    }

    It 'adds a Basic Authorization header when username and password are both provided' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'scavenge.ps1')).Path
        $expectedUri = 'http://localhost:2113/admin/scavenge'
        $expectedAuth = 'Basic ' + [Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes('alice:secret'))
        $captured = [pscustomobject]@{
            Count = 0
            Uri = $null
            Headers = $null
        }

        Mock Invoke-RestMethod {
            $captured.Count++
            $captured.Uri = $Uri
            $captured.Headers = $Headers
            @{ status = 'ok' }
        } -ParameterFilter {
            $Method -eq 'Post'
        }

        & $scriptUnderTest -BaseUrl 'http://localhost:2113' -Username 'alice' -Password 'secret'

        $captured.Count | Should -Be 1
        $captured.Uri | Should -Be $expectedUri
        $captured.Headers.Authorization | Should -Be $expectedAuth
    }

    It 'does not add an Authorization header when only one credential is supplied' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'scavenge.ps1')).Path
        $captured = [pscustomobject]@{
            Count = 0
            Uri = $null
            Headers = $null
        }

        Mock Invoke-RestMethod {
            $captured.Count++
            $captured.Uri = $Uri
            $captured.Headers = $Headers
            @{ status = 'ok' }
        } -ParameterFilter {
            $Method -eq 'Post'
        }

        & $scriptUnderTest -BaseUrl 'http://localhost:2113' -Username 'alice'

        $captured.Count | Should -Be 1
        $captured.Uri | Should -Be 'http://localhost:2113/admin/scavenge'
        $captured.Headers.ContainsKey('Authorization') | Should -BeFalse
    }
}
