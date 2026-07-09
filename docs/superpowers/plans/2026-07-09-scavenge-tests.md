# Scavenge Script Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add focused unit tests for `Scripts/scavenge.ps1` that cover request construction and authentication header behavior.

**Architecture:** Keep the existing script unchanged unless a test exposes a real behavioral gap. Add one Pester test file alongside the existing script tests so the new coverage stays in the same style and location as the current PowerShell tests.

**Tech Stack:** PowerShell, Pester

## Global Constraints

- Keep tests local and deterministic; do not hit the real `http://localhost:2113` service.
- Follow the existing script-test style in `Scripts/Tests/Invoke-DailySettlementProcessing.Tests.ps1`.

---

### Task 1: Add scavenge script tests

**Files:**
- Create: `Scripts/Tests/Scavenge.Tests.ps1`

**Interfaces:**
- Consumes: `Scripts/scavenge.ps1`
- Produces: Pester coverage for the POST target URI and auth header branching

- [ ] **Step 1: Write the test file**

```powershell
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

        $captured.Count | Should Be 1
        $captured.Uri | Should Be $expectedUri
        $captured.Headers.Accept | Should Be 'application/json'
        $captured.Headers.'Content-Type' | Should Be 'application/json'
        $captured.Headers.ContainsKey('Authorization') | Should Be $false
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

        $captured.Count | Should Be 1
        $captured.Uri | Should Be $expectedUri
        $captured.Headers.Authorization | Should Be $expectedAuth
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

        $captured.Count | Should Be 1
        $captured.Uri | Should Be 'http://localhost:2113/admin/scavenge'
        $captured.Headers.ContainsKey('Authorization') | Should Be $false
    }
}
```

- [ ] **Step 2: Run the test file**

Run: `Invoke-Pester Scripts/Tests/Scavenge.Tests.ps1`
Expected: all three tests pass
