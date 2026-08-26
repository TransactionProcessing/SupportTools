BeforeAll {
    $script:scriptUnderTest = (Resolve-Path (Join-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'AutoScavenge') 'scavenge.ps1')).Path
}

Describe 'AutoScavenge\scavenge.ps1' {
    It 'posts to /admin/scavenge after trimming a trailing slash from BaseUrl' {
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

        & $script:scriptUnderTest -BaseUrl 'http://localhost:2113/'

        $captured.Count | Should -Be 1
        $captured.Uri | Should -Be $expectedUri
        $captured.Headers.Accept | Should -Be 'application/json'
        $captured.Headers.'Content-Type' | Should -Be 'application/json'
        $captured.Headers.ContainsKey('Authorization') | Should -BeFalse
    }

    It 'adds a Basic Authorization header when username and password are both provided' {
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

        & $script:scriptUnderTest -BaseUrl 'http://localhost:2113' -Username 'alice' -Password (ConvertTo-SecureString 'secret' -AsPlainText -Force)

        $captured.Count | Should -Be 1
        $captured.Uri | Should -Be $expectedUri
        $captured.Headers.Authorization | Should -Be $expectedAuth
    }

    It 'does not add an Authorization header when only one credential is supplied' {
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

        & $script:scriptUnderTest -BaseUrl 'http://localhost:2113' -Username 'alice'

        $captured.Count | Should -Be 1
        $captured.Uri | Should -Be 'http://localhost:2113/admin/scavenge'
        $captured.Headers.ContainsKey('Authorization') | Should -BeFalse
    }

    It 'does not add an Authorization header when only a password is supplied' {
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

        & $script:scriptUnderTest -BaseUrl 'http://localhost:2113' -Password (ConvertTo-SecureString 'secret' -AsPlainText -Force)

        $captured.Count | Should -Be 1
        $captured.Uri | Should -Be 'http://localhost:2113/admin/scavenge'
        $captured.Headers.ContainsKey('Authorization') | Should -BeFalse
    }
}
