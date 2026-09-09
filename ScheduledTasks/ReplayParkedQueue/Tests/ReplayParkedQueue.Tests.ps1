Describe 'ReplayParkedQueue.ps1' {
    It 'replays parked messages for subscriptions with parked counts' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'ReplayParkedQueue.ps1')).Path
        $logDirectory = Join-Path $TestDrive 'replay-logs-1'
        $securePassword = [System.Security.SecureString]::new()
        foreach ($code in 115, 101, 99, 114, 101, 116) {
            $securePassword.AppendChar([char]$code)
        }
        $credential = [pscredential]::new('alice', $securePassword)
        $subscriptions = @(
            [pscustomobject]@{
                subscriptionId = 'subscription-1'
            },
            [pscustomobject]@{
                subscriptionId = 'subscription-2'
            }
        )
        $captured = [pscustomobject]@{
            SubscriptionCalls = 0
            InfoUris          = [System.Collections.Generic.List[string]]::new()
            ReplayUris        = [System.Collections.Generic.List[string]]::new()
        }

        Mock Invoke-RestMethod {
            $captured.SubscriptionCalls++
            $subscriptions
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/status'
        }

        Mock Invoke-RestMethod {
            $captured.InfoUris.Add($Uri) | Out-Null
            @{ parkedEventCount = 2 }
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/subscription-1/status'
        }

        Mock Invoke-RestMethod {
            $captured.InfoUris.Add($Uri) | Out-Null
            @{ parkedEventCount = 0 }
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/subscription-2/status'
        }

        Mock Invoke-RestMethod {
            $captured.ReplayUris.Add($Uri) | Out-Null
        } -ParameterFilter {
            $Method -eq 'POST' -and
            $Uri -eq 'https://queue.example/subscriptions/subscription-1/replay'
        }

        & $scriptUnderTest 'https://queue.example' -Credential $credential -LogDirectory $logDirectory

        $captured.SubscriptionCalls | Should -Be 1
        $captured.InfoUris.Count | Should -Be 2
        $captured.InfoUris | Should -Be @(
            'https://queue.example/subscriptions/subscription-1/status',
            'https://queue.example/subscriptions/subscription-2/status'
        )
        $captured.ReplayUris | Should -Be @(
            'https://queue.example/subscriptions/subscription-1/replay'
        )
    }

    It 'does not post a replay when no subscription has parked messages' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'ReplayParkedQueue.ps1')).Path
        $logDirectory = Join-Path $TestDrive 'replay-logs-2'
        $securePassword = [System.Security.SecureString]::new()
        foreach ($code in 115, 101, 99, 114, 101, 116) {
            $securePassword.AppendChar([char]$code)
        }
        $credential = [pscredential]::new('alice', $securePassword)
        $subscriptions = @(
            [pscustomobject]@{
                subscriptionId = 'subscription-1'
            }
        )
        $captured = [pscustomobject]@{
            SubscriptionCalls = 0
            InfoCalls         = 0
            ReplayCalls       = 0
        }

        Mock Invoke-RestMethod {
            $captured.SubscriptionCalls++
            $subscriptions
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/status'
        }

        Mock Invoke-RestMethod {
            $captured.InfoCalls++
            @{ parkedEventCount = 0 }
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/subscription-1/status'
        }

        Mock Invoke-RestMethod {
            $captured.ReplayCalls++
        } -ParameterFilter {
            $Method -eq 'POST' -and
            $Uri -like 'https://queue.example/subscriptions/*/replay'
        }

        & $scriptUnderTest 'https://queue.example' -Credential $credential -LogDirectory $logDirectory

        $captured.SubscriptionCalls | Should -Be 1
        $captured.InfoCalls | Should -Be 1
        $captured.ReplayCalls | Should -Be 0
    }

    It 'does not add an Authorization header when no credential is supplied' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'ReplayParkedQueue.ps1')).Path
        $logDirectory = Join-Path $TestDrive 'replay-logs-3'
        $captured = [pscustomobject]@{
            Headers = $null
        }

        Mock Invoke-RestMethod {
            $captured.Headers = $Headers
            @()
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/status'
        }

        & $scriptUnderTest 'https://queue.example' -LogDirectory $logDirectory

        $captured.Headers.ContainsKey('Authorization') | Should -BeFalse
    }
}
