Describe 'Daily support check' {
    BeforeAll {
        . (Join-Path $PSScriptRoot '..\Invoke-DailySupportCheck.ps1')
    }

    BeforeEach {
        $testOutputRoot = Join-Path ([IO.Path]::GetTempPath()) (Join-Path 'DailySupportCheckTests' ([guid]::NewGuid().ToString('N')))
        New-Item -ItemType Directory -Path $testOutputRoot -Force | Out-Null
    }

    AfterEach {
        if (Test-Path $testOutputRoot) {
            Remove-Item -LiteralPath $testOutputRoot -Recurse -Force
        }
    }

    It 'runs all registered checks in order and writes both report formats' {
        $outputPath = Join-Path $testOutputRoot 'reports'

        $report = Invoke-DailySupportCheck -OutputPath $outputPath -PassThru

        $report.OverallStatus | Should -Be 'Passed'
        $report.Checks.Name | Should -Be @(
            'PowerShell Runtime'
            'Report Output Directory'
            'Disk Space'
            'HealthMonitoring'
            'Template Configuration'
        )
        @((Get-ChildItem -Path $outputPath -Filter '*.json')).Count | Should -Be 1
        @((Get-ChildItem -Path $outputPath -Filter '*.html')).Count | Should -Be 1
    }

    It 'records a failed check and still runs the following check' {
        $script:followingCheckRan = $false

        $results = @(
            (Invoke-SupportCheck -Name 'Throwing Check' -Action {
                throw 'expected failure'
            }),
            (Invoke-SupportCheck -Name 'Following Check' -Action {
                $script:followingCheckRan = $true
                New-CheckResult -Name 'Following Check' -Status Passed -Summary 'ran'
            })
        )

        $results[0].Status | Should -Be 'Failed'
        $results[0].Error | Should -Match 'expected failure'
        $results[1].Status | Should -Be 'Passed'
        $script:followingCheckRan | Should -BeTrue
    }

    It 'calculates status precedence as Failed, then Warning, then Passed' {
        Get-OverallStatus -Results @(
            (New-CheckResult -Name 'Pass' -Status Passed -Summary 'ok'),
            (New-CheckResult -Name 'Warning' -Status Warning -Summary 'attention')
        ) | Should -Be 'Warning'

        Get-OverallStatus -Results @(
            (New-CheckResult -Name 'Warning' -Status Warning -Summary 'attention'),
            (New-CheckResult -Name 'Fail' -Status Failed -Summary 'broken')
        ) | Should -Be 'Failed'
    }

    It 'preserves details in JSON and HTML-encodes report content' {
        $outputPath = Join-Path $testOutputRoot 'content'
        $check = New-CheckResult -Name 'Content Check' -Status Warning -Summary '<needs review>' -Details 'detail & value'
        $check.Error = 'error <text>'
        $report = New-SupportReport -StartedAt ([datetime]::UtcNow) -Results @($check)

        $paths = Write-SupportReports -Report $report -OutputPath $outputPath
        $json = Get-Content -Raw -Path $paths.JsonPath | ConvertFrom-Json
        $html = Get-Content -Raw -Path $paths.HtmlPath

        $json.Checks[0].Details | Should -Be 'detail & value'
        $html | Should -Match '&lt;needs review&gt;'
        $html | Should -Match 'detail &amp; value'
        $html | Should -Match 'error &lt;text&gt;'
    }

    It 'returns the generated files from the local transport seam' {
        $paths = [pscustomobject]@{ JsonPath = 'report.json'; HtmlPath = 'report.html' }
        $transport = Send-SupportReport -Report ([pscustomobject]@{ OverallStatus = 'Passed' }) -ReportPaths $paths

        $transport.Transport | Should -Be 'LocalFiles'
        $transport.Status | Should -Be 'Ready'
        $transport.Paths.JsonPath | Should -Be 'report.json'
        $transport.Paths.HtmlPath | Should -Be 'report.html'
    }

    It 'loads disk-space settings from a JSON configuration file' {
        $configPath = Join-Path $testOutputRoot 'daily-support-check.json'
        @'
{
  "DiskSpace": {
    "Enabled": true,
    "DefaultMinimumFreePercent": 15,
    "DriveOverrides": {
      "C:": 10,
      "D:": 20
    }
  },
  "HealthMonitoring": {
    "Enabled": true,
    "BaseUrl": "http://localhost:9620",
    "ServicesPath": "/api/services",
    "TimeoutSeconds": 10
  }
}
'@ | Set-Content -LiteralPath $configPath -Encoding UTF8

        $configuration = Get-SupportConfiguration -Path $configPath

        $configuration.DiskSpace.Enabled | Should -BeTrue
        $configuration.DiskSpace.DefaultMinimumFreePercent | Should -Be 15
        $configuration.DiskSpace.DriveOverrides.'C:' | Should -Be 10
        $configuration.HealthMonitoring.Enabled | Should -BeTrue
        $configuration.HealthMonitoring.BaseUrl | Should -Be 'http://localhost:9620'
    }

    It 'uses a drive override instead of the global disk-space threshold' {
        $configuration = [pscustomobject]@{
            DiskSpace = [pscustomobject]@{
                Enabled = $true
                DefaultMinimumFreePercent = 15
                DriveOverrides = [pscustomobject]@{ 'C:' = 25 }
            }
        }
        $context = [pscustomobject]@{
            Configuration = $configuration
            DiskSpaceProvider = {
                @(
                    [pscustomobject]@{ Drive = 'C:'; SizeBytes = 1000; FreeBytes = 200 }
                    [pscustomobject]@{ Drive = 'D:'; SizeBytes = 1000; FreeBytes = 200 }
                )
            }
        }

        $result = Test-DiskSpace -Context $context

        $result.Status | Should -Be 'Warning'
        ($result.Details | Where-Object Drive -eq 'C:').ThresholdPercent | Should -Be 25
        ($result.Details | Where-Object Drive -eq 'D:').ThresholdPercent | Should -Be 15
    }

    It 'fails the disk-space check when drive inspection fails' {
        $configuration = [pscustomobject]@{
            DiskSpace = [pscustomobject]@{
                Enabled = $true
                DefaultMinimumFreePercent = 15
                DriveOverrides = [pscustomobject]@{}
            }
        }
        $context = [pscustomobject]@{
            Configuration = $configuration
            DiskSpaceProvider = { throw 'disk query failed' }
        }

        $result = Test-DiskSpace -Context $context

        $result.Status | Should -Be 'Failed'
        $result.Error | Should -Match 'disk query failed'
    }

    It 'passes the HealthMonitoring check when all services are healthy' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                HealthMonitoring = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://health-monitoring'
                    ServicesPath = '/api/services'
                    TimeoutSeconds = 10
                }
            }
            HealthMonitoringProvider = {
                @(
                    [pscustomobject]@{ ServiceId = 'orders'; Name = 'Orders'; Status = 'Healthy'; LastObservedAtUtc = '2026-09-29T12:00:00Z'; LastError = $null }
                    [pscustomobject]@{ ServiceId = 'payments'; Name = 'Payments'; Status = 'Healthy'; LastObservedAtUtc = '2026-09-29T12:00:00Z'; LastError = $null }
                )
            }
        }

        $result = Test-HealthMonitoring -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Details.Count | Should -Be 2
        $result.Summary | Should -Match '2 monitored service'
    }

    It 'returns Warning for degraded services and Failed for unhealthy services' {
        $configuration = [pscustomobject]@{
            HealthMonitoring = [pscustomobject]@{
                Enabled = $true
                BaseUrl = 'http://health-monitoring'
                ServicesPath = '/api/services'
                TimeoutSeconds = 10
            }
        }

        $degradedContext = [pscustomobject]@{
            Configuration = $configuration
            HealthMonitoringProvider = { @([pscustomobject]@{ ServiceId = 'orders'; Name = 'Orders'; Status = 'Degraded' }) }
        }
        $unhealthyContext = [pscustomobject]@{
            Configuration = $configuration
            HealthMonitoringProvider = { @([pscustomobject]@{ ServiceId = 'payments'; Name = 'Payments'; Status = 'Unhealthy' }) }
        }

        (Test-HealthMonitoring -Context $degradedContext).Status | Should -Be 'Warning'
        (Test-HealthMonitoring -Context $unhealthyContext).Status | Should -Be 'Failed'
    }

    It 'fails the HealthMonitoring check when the endpoint cannot be queried' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                HealthMonitoring = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://health-monitoring'
                    ServicesPath = '/api/services'
                    TimeoutSeconds = 10
                }
            }
            HealthMonitoringProvider = { throw 'health endpoint unavailable' }
        }

        $result = Test-HealthMonitoring -Context $context

        $result.Status | Should -Be 'Failed'
        $result.Error | Should -Match 'health endpoint unavailable'
    }
}
