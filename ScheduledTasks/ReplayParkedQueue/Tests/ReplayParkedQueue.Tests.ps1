Describe 'ReplayParkedQueue.ps1' {
    It 'replays parked messages for subscriptions with parked counts and encodes $all streams' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'ReplayParkedQueue.ps1')).Path
        $logDirectory = Join-Path $TestDrive 'replay-logs-1'
        $subscriptions = @(
            [pscustomobject]@{
                eventStreamId = 'stream-1'
                groupName     = 'group-1'
            },
            [pscustomobject]@{
                eventStreamId = '$all'
                groupName     = 'group-2'
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
            $Uri -eq 'https://queue.example/subscriptions'
        }

        Mock Invoke-RestMethod {
            $captured.InfoUris.Add($Uri) | Out-Null
            @{ parkedMessageCount = 2 }
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/stream-1/group-1/info'
        }

        Mock Invoke-RestMethod {
            $captured.InfoUris.Add($Uri) | Out-Null
            @{ parkedMessageCount = 0 }
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/%24all/group-2/info'
        }

        Mock Invoke-RestMethod {
            $captured.ReplayUris.Add($Uri) | Out-Null
        } -ParameterFilter {
            $Method -eq 'POST' -and
            $Uri -eq 'https://queue.example/subscriptions/stream-1/group-1/replayParked?from=0'
        }

        & $scriptUnderTest 'https://queue.example' 'alice' 'secret' -LogDirectory $logDirectory

        $captured.SubscriptionCalls | Should -Be 1
        $captured.InfoUris | Should -Be @(
            'https://queue.example/subscriptions/stream-1/group-1/info',
            'https://queue.example/subscriptions/%24all/group-2/info'
        )
        $captured.ReplayUris | Should -Be @(
            'https://queue.example/subscriptions/stream-1/group-1/replayParked?from=0'
        )
    }

    It 'does not post a replay when no subscription has parked messages' {
        $scriptUnderTest = (Resolve-Path (Join-Path (Split-Path $PSScriptRoot -Parent) 'ReplayParkedQueue.ps1')).Path
        $logDirectory = Join-Path $TestDrive 'replay-logs-2'
        $subscriptions = @(
            [pscustomobject]@{
                eventStreamId = 'stream-1'
                groupName     = 'group-1'
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
            $Uri -eq 'https://queue.example/subscriptions'
        }

        Mock Invoke-RestMethod {
            $captured.InfoCalls++
            @{ parkedMessageCount = 0 }
        } -ParameterFilter {
            $Method -eq 'GET' -and
            $Uri -eq 'https://queue.example/subscriptions/stream-1/group-1/info'
        }

        Mock Invoke-RestMethod {
            $captured.ReplayCalls++
        } -ParameterFilter {
            $Method -eq 'POST' -and
            $Uri -like 'https://queue.example/subscriptions/*/*/replayParked?from=0'
        }

        & $scriptUnderTest 'https://queue.example' 'alice' 'secret' -LogDirectory $logDirectory

        $captured.SubscriptionCalls | Should -Be 1
        $captured.InfoCalls | Should -Be 1
        $captured.ReplayCalls | Should -Be 0
    }
}
