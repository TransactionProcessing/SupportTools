Describe 'Daily support check' {
    It 'allows anonymous KurrentDB configuration without a Username property' {
        $settings = [pscustomobject]@{ BaseUrl = 'http://kurrentdb' }

        $credentials = Get-KurrentDbWriteActivityCredentials -Settings $settings

        $credentials.Username | Should -BeNullOrEmpty
        $credentials.Password | Should -BeNullOrEmpty
    }

    It 'uses the packaged KurrentDB client rather than a source-tree build output' {
        $configurationPath = Join-Path $PSScriptRoot '..\daily-support-check.json'
        $configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json

        $configuration.KurrentDbWriteActivity.KurrentDbClientAssemblyPath | Should -Be '.\KurrentDbClient\SupportTools.KurrentDbClient.dll'
    }

    BeforeAll {
        . (Join-Path $PSScriptRoot '..\Invoke-DailySupportCheck.ps1')
    }

    It 'converts an environment password to a read-only SecureString' {
        $securePassword = Get-EnvironmentPasswordSecureString -PasswordCharacters 'test-password'.ToCharArray()

        $securePassword.IsReadOnly() | Should -BeTrue
        [System.Net.NetworkCredential]::new('', $securePassword).Password | Should -Be 'test-password'
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
        $configPath = Join-Path $testOutputRoot 'daily-support-check.json'
        @'
{
  "DiskSpace": {
    "Enabled": true,
    "DefaultMinimumFreePercent": 0,
    "DriveOverrides": {}
  },
  "HealthMonitoring": { "Enabled": false, "BaseUrl": "http://localhost:9620", "ServicesPath": "/api/services", "TimeoutSeconds": 10 },
  "SubscriptionService": { "Enabled": false, "BaseUrl": "http://localhost:8080", "StatusPath": "/subscriptions/status", "TimeoutSeconds": 10 },
  "KurrentDbProjections": { "Enabled": false, "BaseUrl": "http://localhost:2113", "ProjectionsPath": "/projections/any", "ProjectionNames": [], "TimeoutSeconds": 10 },
  "ScheduledTasks": { "Enabled": false, "Tasks": [] },
  "KurrentDbWriteActivity": { "Enabled": false, "BaseUrl": "http://localhost:2113", "Streams": [] }
}
'@ | Set-Content -LiteralPath $configPath -Encoding UTF8

        $report = Invoke-DailySupportCheck -OutputPath $outputPath -ConfigPath $configPath -PassThru

        $report.OverallStatus | Should -Be 'Passed'
        $report.Checks.Name | Should -Be @(
            'PowerShell Runtime'
            'Report Output Directory'
            'Disk Space'
            'HealthMonitoring'
            'Subscription Service'
            'KurrentDB Projections'
            'Scheduled Tasks'
            'KurrentDB Write Activity'
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
                Get-CheckResult -Name 'Following Check' -Status Passed -Summary 'ran'
            })
        )

        $results[0].Status | Should -Be 'Failed'
        $results[0].Error | Should -Match 'expected failure'
        $results[1].Status | Should -Be 'Passed'
        $script:followingCheckRan | Should -BeTrue
    }

    It 'calculates status precedence as Failed, then Warning, then Passed' {
        Get-OverallStatus -Results @(
            (Get-CheckResult -Name 'Pass' -Status Passed -Summary 'ok'),
            (Get-CheckResult -Name 'Warning' -Status Warning -Summary 'attention')
        ) | Should -Be 'Warning'

        Get-OverallStatus -Results @(
            (Get-CheckResult -Name 'Warning' -Status Warning -Summary 'attention'),
            (Get-CheckResult -Name 'Fail' -Status Failed -Summary 'broken')
        ) | Should -Be 'Failed'
    }

    It 'preserves details in JSON and HTML-encodes report content' {
        $outputPath = Join-Path $testOutputRoot 'content'
        $longSummary = ('failure detail ' * 20).Trim()
        $check = Get-CheckResult -Name 'Content Check' -Status Warning -Summary $longSummary -Details 'detail & value'
        $check.Error = 'error <text>'
        $report = Get-SupportReport -StartedAt ([datetime]::UtcNow) -Results @($check)

        $paths = Write-SupportReports -Report $report -OutputPath $outputPath
        $json = Get-Content -Raw -Path $paths.JsonPath | ConvertFrom-Json
        $html = Get-Content -Raw -Path $paths.HtmlPath

        $json.Checks[0].Details | Should -Be 'detail & value'
        $html | Should -Match '<th>Check</th><th>Status</th><th>Summary</th>'
        $html | Should -Match '🟠</span> Warning'
        $html | Should -Match 'failure detail failure detail'
        $html | Should -Match '…'
        $html | Should -Not -Match 'Details'
        $html | Should -Not -Match 'detail &amp; value'
        $html | Should -Not -Match 'error &lt;text&gt;'
    }

    It 'removes matching reports older than the configured retention period' {
        $outputPath = Join-Path $testOutputRoot 'retention'
        New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
        $oldJsonPath = Join-Path $outputPath 'daily-support-check-old.json'
        $oldHtmlPath = Join-Path $outputPath 'daily-support-check-old.html'
        $recentJsonPath = Join-Path $outputPath 'daily-support-check-recent.json'
        $unrelatedPath = Join-Path $outputPath 'unrelated.json'
        'old' | Set-Content -LiteralPath $oldJsonPath
        'old' | Set-Content -LiteralPath $oldHtmlPath
        'recent' | Set-Content -LiteralPath $recentJsonPath
        'unrelated' | Set-Content -LiteralPath $unrelatedPath
        $oldTime = [DateTime]::UtcNow.AddDays(-8)
        [IO.File]::SetLastWriteTimeUtc($oldJsonPath, $oldTime)
        [IO.File]::SetLastWriteTimeUtc($oldHtmlPath, $oldTime)
        [IO.File]::SetLastWriteTimeUtc($unrelatedPath, $oldTime)

        $report = Get-SupportReport -StartedAt ([datetime]::UtcNow) -Results @(
            (Get-CheckResult -Name 'Retention Check' -Status Passed -Summary 'ok')
        )
        Write-SupportReports -Report $report -OutputPath $outputPath -RetentionDays 7 | Out-Null

        Test-Path -LiteralPath $oldJsonPath | Should -BeFalse
        Test-Path -LiteralPath $oldHtmlPath | Should -BeFalse
        Test-Path -LiteralPath $recentJsonPath | Should -BeTrue
        Test-Path -LiteralPath $unrelatedPath | Should -BeTrue
    }

    It 'returns the generated files from the local transport seam' {
        $paths = [pscustomobject]@{ JsonPath = 'report.json'; HtmlPath = 'report.html' }
        $transport = Send-SupportReport -Report ([pscustomobject]@{ OverallStatus = 'Passed' }) -ReportPaths $paths

        $transport.Transport | Should -Be 'LocalFiles'
        $transport.Status | Should -Be 'Ready'
        $transport.Paths.JsonPath | Should -Be 'report.json'
        $transport.Paths.HtmlPath | Should -Be 'report.html'
    }

    It 'sends a Brevo email with the report summary and attachments' {
        $jsonPath = Join-Path $testOutputRoot 'report.json'
        $htmlPath = Join-Path $testOutputRoot 'report.html'
        '{"OverallStatus":"Passed"}' | Set-Content -LiteralPath $jsonPath -Encoding UTF8
        '<html><body>Passed</body></html>' | Set-Content -LiteralPath $htmlPath -Encoding UTF8
        $captured = $null
        $configuration = [pscustomobject]@{
            Email = [pscustomobject]@{
                Enabled = $true
                Provider = 'Brevo'
                ApiUrl = 'https://brevo.test/v3/smtp/email'
                ApiKey = 'api-test-key'
                From = 'support@example.com'
                To = @('recipient@example.com')
                SubjectPrefix = 'Daily Support Check'
                AttachHtmlReport = $true
                AttachJsonReport = $true
            }
        }
        $provider = {
            param($requestUri, $headers, $payload)
            $script:captured = [pscustomobject]@{ Uri = $requestUri; Headers = $headers; Payload = $payload }
            [pscustomobject]@{ messageId = '<message-id>' }
        }

        $transport = Send-SupportReport `
            -Report ([pscustomobject]@{ OverallStatus = 'Passed'; StartedAt = '2026-09-29T15:30:00.0000000Z'; Counts = [pscustomobject]@{ Passed = 1; Warning = 0; Failed = 0 }; Checks = @() }) `
            -ReportPaths ([pscustomobject]@{ JsonPath = $jsonPath; HtmlPath = $htmlPath }) `
            -Configuration $configuration `
            -EmailProvider $provider

        $transport.Transport | Should -Be 'Brevo'
        $transport.Status | Should -Be 'Sent'
        $script:captured.Uri | Should -Be 'https://brevo.test/v3/smtp/email'
        $script:captured.Headers['api-key'] | Should -Be 'api-test-key'
        $expectedServerName = if ([string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) { 'Unknown Server' } else { $env:COMPUTERNAME }
        $script:captured.Payload.subject | Should -Be "Daily Support Check - Passed - $expectedServerName - 2026-09-29"
        $script:captured.Payload.sender.email | Should -Be 'support@example.com'
        $script:captured.Payload.to.email | Should -Contain 'recipient@example.com'
        $script:captured.Payload.attachments.name | Should -Contain 'report.html'
        $script:captured.Payload.attachments.name | Should -Contain 'report.json'
    }

    It 'reports a Brevo API failure without exposing the API key' {
        $configuration = [pscustomobject]@{
            Email = [pscustomobject]@{
                Enabled = $true
                Provider = 'Brevo'
                ApiUrl = 'https://brevo.test/v3/smtp/email'
                ApiKey = 'api-secret-key'
                From = 'support@example.com'
                To = @('recipient@example.com')
            }
        }
        $transport = Send-SupportReport `
            -Report ([pscustomobject]@{ OverallStatus = 'Failed'; Counts = [pscustomobject]@{ Passed = 0; Warning = 0; Failed = 1 }; Checks = @() }) `
            -ReportPaths ([pscustomobject]@{ JsonPath = 'report.json'; HtmlPath = 'report.html' }) `
            -Configuration $configuration `
            -EmailProvider { param($requestUri, $headers, $payload) throw 'Brevo rejected the request.' }

        $transport.Transport | Should -Be 'Brevo'
        $transport.Status | Should -Be 'Failed'
        $transport.Message | Should -Match 'rejected'
        $transport.Message | Should -Not -Match 'api-secret-key'
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
  },
  "SubscriptionService": {
    "Enabled": true,
    "BaseUrl": "http://localhost:8080",
    "StatusPath": "/subscriptions/status",
    "TimeoutSeconds": 10
  },
    "KurrentDbProjections": {
    "Enabled": true,
    "BaseUrl": "http://localhost:2113",
    "ProjectionsPath": "/projections/any",
    "ProjectionNames": [
        "MerchantBalanceProjection"
    ],
    "TimeoutSeconds": 10
  },
  "ScheduledTasks": {
    "Enabled": true,
    "Tasks": [
      {
          "Name": "Daily Settlement",
          "Path": "\\",
          "ExpectedRunIntervalMinutes": 1440
        },
        {
          "Name": "Replay Parked Queue",
          "Path": "\\",
          "ExpectedRunIntervalMinutes": 1440
        },
        {
          "Name": "Scavenge",
        "Path": "\\",
          "ExpectedRunIntervalMinutes": 10080
      }
    ]
  },
  "KurrentDbWriteActivity": {
    "Enabled": true,
    "BaseUrl": "http://localhost:2113",
    "Streams": [
      {
        "Name": "$idx-ce-TransactionAggregate",
        "EventCount": 10,
        "MaxLatestEventAgeMinutes": 15
      }
    ]
  }
}
'@ | Set-Content -LiteralPath $configPath -Encoding UTF8

        $configuration = Get-SupportConfiguration -Path $configPath

        $configuration.DiskSpace.Enabled | Should -BeTrue
        $configuration.DiskSpace.DefaultMinimumFreePercent | Should -Be 15
        $configuration.DiskSpace.DriveOverrides.'C:' | Should -Be 10
        $configuration.HealthMonitoring.Enabled | Should -BeTrue
        $configuration.HealthMonitoring.BaseUrl | Should -Be 'http://localhost:9620'
        $configuration.SubscriptionService.Enabled | Should -BeTrue
        $configuration.SubscriptionService.BaseUrl | Should -Be 'http://localhost:8080'
        $configuration.ReportRetentionDays | Should -Be 7
        $configuration.KurrentDbProjections.Enabled | Should -BeTrue
        $configuration.KurrentDbProjections.ProjectionNames | Should -Be @('MerchantBalanceProjection')
        $configuration.ScheduledTasks.Enabled | Should -BeTrue
        $configuration.ScheduledTasks.Tasks.Name | Should -Be @('Daily Settlement', 'Replay Parked Queue', 'Scavenge')
        $configuration.ScheduledTasks.Tasks[0].ExpectedRunIntervalMinutes | Should -Be 1440
        $configuration.ScheduledTasks.Tasks[2].ExpectedRunIntervalMinutes | Should -Be 10080
        $configuration.KurrentDbWriteActivity.Enabled | Should -BeTrue
        $configuration.KurrentDbWriteActivity.Streams.Count | Should -Be 1
        $configuration.KurrentDbWriteActivity.Streams[0].Name | Should -Be '$idx-ce-TransactionAggregate'
    }

    It 'merges the optional local configuration override without changing the base file' {
        $configPath = Join-Path $testOutputRoot 'daily-support-check.json'
        $localConfigPath = Join-Path $testOutputRoot 'daily-support-check.local.json'
        @'
{
  "DiskSpace": {
    "Enabled": false,
    "DefaultMinimumFreePercent": 15,
    "DriveOverrides": {}
  },
  "Email": {
    "Enabled": false,
    "From": "tracked@example.com"
  }
}
'@ | Set-Content -LiteralPath $configPath -Encoding UTF8
        @'
{
  "Email": {
    "Enabled": true,
    "ApiKey": "local-secret"
  }
}
'@ | Set-Content -LiteralPath $localConfigPath -Encoding UTF8

        $configuration = Get-SupportConfiguration -Path $configPath

        $configuration.Email.Enabled | Should -BeTrue
        $configuration.Email.From | Should -Be 'tracked@example.com'
        $configuration.Email.ApiKey | Should -Be 'local-secret'
        (Get-Content -Raw -LiteralPath $configPath) | Should -Match 'tracked@example.com'
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

    It 'passes the subscription check when all subscriptions are running without parked messages' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                SubscriptionService = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://subscription-service'
                    StatusPath = '/subscriptions/status'
                    TimeoutSeconds = 10
                }
            }
            SubscriptionServiceProvider = {
                @([pscustomobject]@{
                    subscriptionId = 'subscription-1'
                    tag = 'Main'
                    isRunning = $true
                    health = 'Healthy'
                    parkedEventCount = 0
                    operationalReason = $null
                    runtimeFailureReason = $null
                })
            }
        }

        $result = Test-SubscriptionService -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Summary | Should -Match '1 subscription'
    }

    It 'expands a wrapped subscription response into individual subscriptions' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                SubscriptionService = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://subscription-service'
                    StatusPath = '/subscriptions/status'
                    TimeoutSeconds = 10
                }
            }
            SubscriptionServiceProvider = {
                [pscustomobject]@{
                    subscriptionId = @('subscription-1', 'subscription-2')
                    tag = @('Main', 'Ordered')
                    isRunning = @($true, $true)
                    health = @('Healthy', 'Healthy')
                    parkedEventCount = @(0, 2)
                    operationalReason = @($null, $null)
                    runtimeFailureReason = @($null, $null)
                }
            }
        }

        $result = Test-SubscriptionService -Context $context

        $result.Status | Should -Be 'Warning'
        $result.Summary | Should -Match '1 subscription\(s\) have parked messages'
        $result.Details.Count | Should -Be 2
    }

    It 'fails when a subscription is stopped and warns when parked messages exist' {
        $configuration = [pscustomobject]@{
            SubscriptionService = [pscustomobject]@{
                Enabled = $true
                BaseUrl = 'http://subscription-service'
                StatusPath = '/subscriptions/status'
                TimeoutSeconds = 10
            }
        }
        $stoppedContext = [pscustomobject]@{
            Configuration = $configuration
            SubscriptionServiceProvider = { @([pscustomobject]@{ subscriptionId = 'stopped'; tag = 'Stopped'; isRunning = $false; health = 'Unhealthy'; parkedEventCount = 0 }) }
        }
        $parkedContext = [pscustomobject]@{
            Configuration = $configuration
            SubscriptionServiceProvider = { @([pscustomobject]@{ subscriptionId = 'parked'; tag = 'Parked'; isRunning = $true; health = 'Healthy'; parkedEventCount = 3 }) }
        }

        (Test-SubscriptionService -Context $stoppedContext).Status | Should -Be 'Failed'
        (Test-SubscriptionService -Context $parkedContext).Status | Should -Be 'Warning'
    }

    It 'fails the subscription check when the status endpoint cannot be queried' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                SubscriptionService = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://subscription-service'
                    StatusPath = '/subscriptions/status'
                    TimeoutSeconds = 10
                }
            }
            SubscriptionServiceProvider = { throw 'subscription endpoint unavailable' }
        }

        $result = Test-SubscriptionService -Context $context

        $result.Status | Should -Be 'Failed'
        $result.Error | Should -Match 'subscription endpoint unavailable'
    }

    It 'passes only the configured KurrentDB projections when they are running' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbProjections = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://kurrentdb'
                    ProjectionsPath = '/projections/any'
                    ProjectionNames = @('selected')
                    TimeoutSeconds = 10
                }
            }
            KurrentDbProjectionProvider = {
                @(
                    [pscustomobject]@{ effectiveName = 'selected'; status = 'Running'; progress = 100; stateReason = $null }
                    [pscustomobject]@{ effectiveName = 'not-selected'; status = 'Stopped'; progress = 0; stateReason = 'disabled' }
                )
            }
        }

        $result = Test-KurrentDbProjections -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Details.Count | Should -Be 1
        $result.Details[0].Name | Should -Be 'selected'
    }

    It 'includes the raw KurrentDB projection payload when debug is enabled' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbProjections = [pscustomobject]@{
                    Enabled = $true
                    Debug = $true
                    BaseUrl = 'http://kurrentdb'
                    ProjectionsPath = '/projections/any'
                    ProjectionNames = @('selected')
                    TimeoutSeconds = 10
                }
            }
            KurrentDbProjectionProvider = {
                @([pscustomobject]@{ effectiveName = 'selected'; status = 'Running'; progress = 100; stateReason = $null })
            }
        }

        $result = Test-KurrentDbProjections -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Details.RawResponse[0].effectiveName | Should -Be 'selected'
        $result.Details.Projections[0].Name | Should -Be 'selected'
    }

    It 'unwraps the projections property returned by KurrentDB' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbProjections = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://kurrentdb'
                    ProjectionsPath = '/projections/any'
                    ProjectionNames = @('selected')
                    TimeoutSeconds = 10
                }
            }
            KurrentDbProjectionProvider = {
                [pscustomobject]@{
                    projections = @([pscustomobject]@{ effectiveName = 'selected'; status = 'Running'; progress = 100; stateReason = $null })
                }
            }
        }

        $result = Test-KurrentDbProjections -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Details[0].Name | Should -Be 'selected'
    }

    It 'fails when a configured KurrentDB projection is stopped or missing' {
        $configuration = [pscustomobject]@{
            KurrentDbProjections = [pscustomobject]@{
                Enabled = $true
                BaseUrl = 'http://kurrentdb'
                ProjectionsPath = '/projections/any'
                ProjectionNames = @('stopped', 'missing')
                TimeoutSeconds = 10
            }
        }
        $context = [pscustomobject]@{
            Configuration = $configuration
            KurrentDbProjectionProvider = {
                @([pscustomobject]@{ effectiveName = 'stopped'; status = 'Stopped'; progress = 0; stateReason = 'disabled' })
            }
        }

        $result = Test-KurrentDbProjections -Context $context

        $result.Status | Should -Be 'Failed'
        $result.Summary | Should -Match 'stopped'
        $result.Summary | Should -Match 'missing'
    }

    It 'warns when no KurrentDB projections are configured' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbProjections = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://kurrentdb'
                    ProjectionsPath = '/projections/any'
                    ProjectionNames = @()
                    TimeoutSeconds = 10
                }
            }
        }

        $result = Test-KurrentDbProjections -Context $context

        $result.Status | Should -Be 'Warning'
    }

    It 'fails the KurrentDB projection check when the endpoint cannot be queried' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbProjections = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://kurrentdb'
                    ProjectionsPath = '/projections/any'
                    ProjectionNames = @('selected')
                    TimeoutSeconds = 10
                }
            }
            KurrentDbProjectionProvider = { throw 'projection endpoint unavailable' }
        }

        $result = Test-KurrentDbProjections -Context $context

        $result.Status | Should -Be 'Failed'
        $result.Error | Should -Match 'projection endpoint unavailable'
    }

    It 'passes when configured scheduled tasks are enabled and have successful recent runs' {
        $now = [datetime]::Now
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                ScheduledTasks = [pscustomobject]@{
                    Enabled = $true
                    Tasks = @([pscustomobject]@{ Name = 'Daily Support Check'; Path = '\'; MaxLastRunAgeHours = 24 })
                }
            }
            ScheduledTaskProvider = {
                param($taskConfiguration)
                [pscustomobject]@{
                    Name = $taskConfiguration.Name
                    Path = $taskConfiguration.Path
                    State = 'Ready'
                    LastTaskResult = 0
                    LastRunTime = $now.AddHours(-1)
                    NextRunTime = $now.AddHours(23)
                }
            }
        }

        $result = Test-ScheduledTasks -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Details[0].LastTaskResult | Should -Be 0
    }

    It 'fails for missing, disabled, failed, or overdue scheduled tasks' {
        $now = [datetime]::Now
        $configuration = [pscustomobject]@{
            ScheduledTasks = [pscustomobject]@{
                Enabled = $true
                Tasks = @(
                    [pscustomobject]@{ Name = 'Missing'; Path = '\'; MaxLastRunAgeHours = 24 }
                    [pscustomobject]@{ Name = 'Disabled'; Path = '\'; MaxLastRunAgeHours = 24 }
                    [pscustomobject]@{ Name = 'Failed'; Path = '\'; MaxLastRunAgeHours = 24 }
                    [pscustomobject]@{ Name = 'Overdue'; Path = '\'; MaxLastRunAgeHours = 24 }
                )
            }
        }
        $context = [pscustomobject]@{
            Configuration = $configuration
            ScheduledTaskProvider = {
                param($taskConfiguration)
                switch ($taskConfiguration.Name) {
                    'Missing' { throw 'task not found' }
                    'Disabled' { [pscustomobject]@{ Name = 'Disabled'; Path = '\'; State = 'Disabled'; LastTaskResult = 0; LastRunTime = $now.AddHours(-1); NextRunTime = $now.AddHours(23) } }
                    'Failed' { [pscustomobject]@{ Name = 'Failed'; Path = '\'; State = 'Ready'; LastTaskResult = 1; LastRunTime = $now.AddHours(-1); NextRunTime = $now.AddHours(23) } }
                    'Overdue' { [pscustomobject]@{ Name = 'Overdue'; Path = '\'; State = 'Ready'; LastTaskResult = 0; LastRunTime = $now.AddHours(-25); NextRunTime = $now.AddHours(-1) } }
                }
            }
        }

        $result = Test-ScheduledTasks -Context $context

        $result.Status | Should -Be 'Failed'
        $result.Summary | Should -Match 'Missing'
        $result.Summary | Should -Match 'Disabled'
        $result.Summary | Should -Match 'Failed'
        $result.Summary | Should -Match 'Overdue'
    }

    It 'uses the expected run interval when checking scheduled-task freshness' {
        $now = [datetime]::Now
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                ScheduledTasks = [pscustomobject]@{
                    Enabled = $true
                    Tasks = @([pscustomobject]@{
                        Name = 'Daily Settlement'
                        Path = '\'
                        ExpectedRunIntervalMinutes = 1440
                    })
                }
            }
            ScheduledTaskProvider = {
                [pscustomobject]@{
                    Name = 'Daily Settlement'
                    Path = '\'
                    State = 'Ready'
                    LastTaskResult = 0
                    LastRunTime = $now.AddMinutes(-1441)
                    NextRunTime = $now.AddHours(23)
                }
            }
        }

        $result = Test-ScheduledTasks -Context $context

        $result.Status | Should -Be 'Failed'
        $result.Summary | Should -Match 'Last run is too old'
    }

    It 'warns when no scheduled tasks are configured' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                ScheduledTasks = [pscustomobject]@{ Enabled = $true; Tasks = @() }
            }
        }

        (Test-ScheduledTasks -Context $context).Status | Should -Be 'Warning'
    }

    It 'passes when recent events are found on the configured KurrentDB stream' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbWriteActivity = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://kurrentdb'
                    StreamName = '$idx-ce-CallbackMessageAggregate'
                    EventCount = 2
                    MaxLatestEventAgeMinutes = 15
                }
            }
            KurrentDbWriteActivityProvider = {
                param($requestUri, $requestCount)
                [pscustomobject]@{
                    entries = @(
                        [pscustomobject]@{ title = '9@stream'; updated = ([datetime]::UtcNow.AddMinutes(-1)).ToString('o'); summary = 'EventType'; id = 'event-9' }
                        [pscustomobject]@{ title = '8@stream'; updated = ([datetime]::UtcNow.AddMinutes(-2)).ToString('o'); summary = 'EventType'; id = 'event-8' }
                    )
                }
            }
        }

        $result = Test-KurrentDbWriteActivity -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Details.Count | Should -Be 1
        $result.Details[0].Events.Count | Should -Be 2
        $result.Summary | Should -Match '1 configured KurrentDB stream'
    }

    It 'checks multiple configured KurrentDB streams independently' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbWriteActivity = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://kurrentdb'
                    Streams = @(
                        [pscustomobject]@{ Name = 'stream-one'; EventCount = 1; MaxLatestEventAgeMinutes = 15 }
                        [pscustomobject]@{ Name = 'stream-two'; EventCount = 1; MaxLatestEventAgeMinutes = 15 }
                    )
                }
            }
            KurrentDbWriteActivityProvider = {
                param($requestUri, $requestCount)
                [pscustomobject]@{
                    entries = @([pscustomobject]@{ title = "0@$requestUri"; updated = ([datetime]::UtcNow.AddMinutes(-1)).ToString('o'); summary = 'EventType'; id = $requestUri })
                }
            }
        }

        $result = Test-KurrentDbWriteActivity -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Details.Count | Should -Be 2
        $result.Details.StreamName | Should -Contain 'stream-one'
        $result.Details.StreamName | Should -Contain 'stream-two'
    }

    It 'accepts events returned by the KurrentDB filtered all-stream client' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbWriteActivity = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://kurrentdb'
                    Streams = @([pscustomobject]@{ Name = '$idx-ce-TransactionAggregate'; EventCount = 1; MaxLatestEventAgeMinutes = 15 })
                }
            }
            KurrentDbWriteActivityProvider = {
                param($indexName, $requestCount)
                [pscustomobject]@{
                    events = @([pscustomobject]@{
                        EventId = 'event-1'
                        EventType = 'TransactionHasStartedEvent'
                        Timestamp = ([datetime]::UtcNow.AddMinutes(-1)).ToString('o')
                        Title = 'TransactionAggregate-1'
                    })
                }
            }
        }

        $result = Test-KurrentDbWriteActivity -Context $context

        $result.Status | Should -Be 'Passed'
        $result.Details[0].Events[0].EventType | Should -Be 'TransactionHasStartedEvent'
    }

    It 'warns when fewer events are returned and fails when the latest event is stale or absent' {
        $configuration = [pscustomobject]@{
            KurrentDbWriteActivity = [pscustomobject]@{
                Enabled = $true
                BaseUrl = 'http://kurrentdb'
                StreamName = '$idx-ce-CallbackMessageAggregate'
                EventCount = 10
                MaxLatestEventAgeMinutes = 15
            }
        }
        $fewContext = [pscustomobject]@{
            Configuration = $configuration
            KurrentDbWriteActivityProvider = { [pscustomobject]@{ entries = @([pscustomobject]@{ title = '0@stream'; updated = ([datetime]::UtcNow.AddMinutes(-1)).ToString('o'); summary = 'EventType'; id = 'event-0' }) } }
        }
        $staleContext = [pscustomobject]@{
            Configuration = $configuration
            KurrentDbWriteActivityProvider = { [pscustomobject]@{ entries = @([pscustomobject]@{ title = '0@stream'; updated = ([datetime]::UtcNow.AddMinutes(-30)).ToString('o'); summary = 'EventType'; id = 'event-0' }) } }
        }
        $emptyContext = [pscustomobject]@{
            Configuration = $configuration
            KurrentDbWriteActivityProvider = { [pscustomobject]@{ entries = @() } }
        }

        (Test-KurrentDbWriteActivity -Context $fewContext).Status | Should -Be 'Warning'
        (Test-KurrentDbWriteActivity -Context $staleContext).Status | Should -Be 'Failed'
        (Test-KurrentDbWriteActivity -Context $emptyContext).Status | Should -Be 'Failed'
    }

    It 'fails the KurrentDB write-activity check when the stream cannot be queried' {
        $context = [pscustomobject]@{
            Configuration = [pscustomobject]@{
                KurrentDbWriteActivity = [pscustomobject]@{
                    Enabled = $true
                    BaseUrl = 'http://kurrentdb'
                    StreamName = '$idx-ce-CallbackMessageAggregate'
                    EventCount = 10
                    MaxLatestEventAgeMinutes = 15
                }
            }
            KurrentDbWriteActivityProvider = { throw 'stream endpoint unavailable' }
        }

        $result = Test-KurrentDbWriteActivity -Context $context

        $result.Status | Should -Be 'Failed'
        $result.Error | Should -Match 'stream endpoint unavailable'
    }
}
