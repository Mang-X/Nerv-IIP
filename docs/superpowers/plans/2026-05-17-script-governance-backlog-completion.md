# Script Governance Backlog Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the remaining script automation governance backlog before starting the next feature stage: finish the current IAM authorization audit handoff, migrate the priority legacy verify scripts, remove their governance exemptions, and capture non-Windows compatibility evidence.

**Architecture:** Keep ADR 0010 and `docs/architecture/script-automation-governance.md` as the decision boundary. Use `scripts/lib/ScriptAutomation.ps1` as the only wrapper for long-running native commands, Docker Compose, nested PowerShell scripts, scoped environment variables and process diagnostics. Add a small compatibility gate script that can be run from WSL, macOS or Linux and that records exact command/version evidence instead of claiming support from intent alone.

**Tech Stack:** PowerShell 7, .NET 10, Docker Compose v2, Git, WSL Ubuntu or another macOS/Linux runner, existing xUnit and frontend verification scripts.

---

## Completion Record

This plan starts from commit `8c6bcde Merge pull request #12 from Mang-X/codex/iam-persistent-auth-foundation`, currently checked out as detached `HEAD` with `main` and `origin/main` pointing at the same commit.

Known handoff notes:

1. `skills-lock.json` is dirty before this plan begins. Do not stage, edit or revert it unless the user explicitly asks.
2. A post-merge IAM audit has already produced local changes that guard PostgreSQL IAM user/role management endpoints before persistence access. Keep those changes separate from the script governance commit.
3. The script governance plan `docs/superpowers/plans/2026-05-17-script-automation-governance.md` still has two open backlog items: migrate the priority fourth/fifth verify scripts and run a macOS/Linux compatibility gate.

## Execution Record

1. Created branch `codex/script-governance-backlog-completion` from `8c6bcde`.
2. Committed the IAM audit handoff separately as `99970a6 fix: guard iam management endpoints`.
3. Added priority no-exemption governance coverage as `70aabd1 test: cover priority script governance backlog`.
4. Migrated the fifth-stage verify script as `d9dd810 chore: migrate fifth verify script governance`.
5. Migrated the fourth-stage verify script as `71e073e chore: migrate fourth verify script governance`.
6. Removed the fourth/fifth priority exemptions as `3691f49 chore: remove priority script exemptions`.
7. Added the compatibility gate as `396f281 chore: add script compatibility gate`.
8. Ran the full Ubuntu WSL compatibility gate with evidence at `artifacts/script-logs/script-compatibility/20260517-233939-907/evidence.json`: Ubuntu 22.04.3 LTS, PowerShell 7.6.1, .NET SDK 10.0.300, Docker Compose 5.1.3, `fastOnly: false`, IAM persistent auth verify passed.
9. Re-ran final Windows gates: script governance tests, script governance gate, Windows fast compatibility smoke, fifth verify script, fourth verify script, backend solution tests and `git diff --check`.
10. Kept pre-existing `skills-lock.json` and generated `artifacts/script-logs/**` evidence out of git.

## Boundaries

1. Do not start Gateway-wide authorization, Console login UI, FileStorage, Notification, high-risk Ops approval or deployment installer work in this plan.
2. Do not migrate every legacy script in one pass. The required migration target is `verify-fifth-slice-persistence-foundation.ps1` and `verify-fourth-slice-real-infra.ps1`.
3. Do not remove exemptions for `export-gateway-openapi.ps1`, `verify-first-slice.ps1`, `verify-second-slice-ops.ps1` or `verify-third-slice-console.ps1` unless those scripts are migrated in a separate approved plan.
4. Do not add `.github` CI provider files in this pass. The non-Windows gate is a repo-local script plus recorded evidence.
5. Do not claim macOS/Linux support unless the gate actually runs outside Windows and the evidence file records OS, PowerShell, .NET and Docker Compose details.
6. Do not stage unrelated `skills-lock.json`.

## File Structure Map

```text
scripts/
  lib/ScriptAutomation.ps1
  check-script-governance.ps1
  check-script-compatibility.ps1
  script-governance-baseline.json
  tests/check-script-governance.Tests.ps1
  verify-fifth-slice-persistence-foundation.ps1
  verify-fourth-slice-real-infra.ps1

docs/architecture/
  script-automation-governance.md
  implementation-readiness.md

docs/superpowers/plans/
  2026-05-17-script-automation-governance.md
  2026-05-17-script-governance-backlog-completion.md
```

## Task 0: Stabilize Current IAM Audit Handoff

**Files:**

- Stage later: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/IamEndpointAuthorization.cs`
- Stage later: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Users/UserEndpoints.cs`
- Stage later: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Roles/RoleEndpoints.cs`
- Stage later: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamManagementEndpointAuthorizationTests.cs`
- Stage later: `docs/architecture/iam-authentication-baseline.md`
- Stage later: `docs/architecture/database-schema-catalog.md`
- Stage later: `docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md`

- [x] **Step 1: Create a working branch from detached HEAD**

Run:

```powershell
git switch -c codex/script-governance-backlog-completion
```

Expected: branch creation succeeds from `8c6bcde`. If the branch already exists, run `git switch codex/script-governance-backlog-completion` and continue.

- [x] **Step 2: Confirm the IAM audit changes are the only non-script work**

Run:

```powershell
git status --short --branch
```

Expected: the status includes the IAM endpoint/test/doc changes listed above, the pre-existing `skills-lock.json`, and no staged files.

- [x] **Step 3: Re-run IAM audit verification before committing it**

Run:

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
dotnet test backend/Nerv.IIP.sln --no-restore
pwsh scripts/check-script-governance.ps1
git diff --check
```

Expected: every command exits `0`. `git diff --check` may print line-ending warnings before the command summary, but it must not report whitespace errors.

- [x] **Step 4: Commit the IAM audit fix separately**

Run:

```powershell
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/IamEndpointAuthorization.cs
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Users/UserEndpoints.cs
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Roles/RoleEndpoints.cs
git add backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamManagementEndpointAuthorizationTests.cs
git add docs/architecture/iam-authentication-baseline.md
git add docs/architecture/database-schema-catalog.md
git add docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md
git commit -m "fix: guard iam management endpoints"
```

Expected: commit succeeds and `skills-lock.json` remains unstaged.

## Task 1: Add Regression Coverage For Priority Script Governance

**Files:**

- Modify: `scripts/tests/check-script-governance.Tests.ps1`

- [x] **Step 1: Add a helper that runs the governance gate without exemptions**

Append this helper after `Invoke-GovernanceCase` in `scripts/tests/check-script-governance.Tests.ps1`:

```powershell
function Invoke-GovernanceScriptCase {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $emptyBaseline = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-empty-script-governance-baseline-$([System.Guid]::NewGuid().ToString('N')).json"
    [System.IO.File]::WriteAllText($emptyBaseline, '{"schema":1,"exemptions":[]}', [System.Text.UTF8Encoding]::new($false))

    try {
        $target = Join-Path $repoRoot $RelativePath
        $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $checker -Path $target -BaselinePath $emptyBaseline 2>&1
        $actualExitCode = $LASTEXITCODE

        if ($actualExitCode -ne 0) {
            $output | ForEach-Object { Write-Host $_ }
            throw "Expected $RelativePath to pass without baseline exemptions, got $actualExitCode."
        }
    }
    finally {
        Remove-Item -LiteralPath $emptyBaseline -Force -ErrorAction SilentlyContinue
    }
}
```

- [x] **Step 2: Add the priority script assertions**

Add these calls after the existing fixture cases and before the helper smoke block:

```powershell
Invoke-GovernanceScriptCase -RelativePath 'scripts/verify-fifth-slice-persistence-foundation.ps1'
Invoke-GovernanceScriptCase -RelativePath 'scripts/verify-fourth-slice-real-infra.ps1'
```

- [x] **Step 3: Run the test harness and verify the expected red state**

Run:

```powershell
pwsh scripts/tests/check-script-governance.Tests.ps1
```

Expected: FAIL because the fifth and fourth verify scripts still rely on baseline exemptions for missing governance headers, missing helper usage and direct native commands.

## Task 2: Migrate Fifth-Stage Verify Script

**Files:**

- Replace: `scripts/verify-fifth-slice-persistence-foundation.ps1`

- [x] **Step 1: Replace the script with a helper-governed version**

Replace the full contents of `scripts/verify-fifth-slice-persistence-foundation.ps1` with:

```powershell
# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local PostgreSQL, Redis and RabbitMQ from infra/docker-compose.dev.yml
#     - Uses disposable AppHub and Ops migration verification databases
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores scoped environment variables
#     - Leaves shared Docker development services running
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Wait-TcpPort {
  param(
    [string]$HostName,
    [int]$Port,
    [int]$TimeoutSeconds = 90
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
      $connectTask = $client.ConnectAsync($HostName, $Port)
      if ($connectTask.Wait(1000) -and $client.Connected) {
        return
      }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
    finally {
      $client.Dispose()
    }

    Start-Sleep -Milliseconds 500
  } while ((Get-Date) -lt $deadline)

  throw "TCP port $HostName`:$Port did not become available within $TimeoutSeconds seconds."
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$appHubTests = Join-Path $root "backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj"
$opsTests = Join-Path $root "backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify release-grade persistence foundation."
}

Invoke-WithScopedEnvironment -Variables @{
  NERV_IIP_POSTGRES_PORT = $postgresPort
} -ScriptBlock {
  Invoke-DockerCompose -Arguments @("-f", $composeFile, "up", "-d", "postgres", "redis", "rabbitmq") -WorkingDirectory $root -TimeoutSeconds 240 -Name "fifth-docker-compose-dependencies" | Out-Null
  Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort)
  Wait-TcpPort -HostName "localhost" -Port 6379 -TimeoutSeconds 60
  Wait-TcpPort -HostName "localhost" -Port 5672 -TimeoutSeconds 60

  Invoke-DotNet -Arguments @("tool", "restore") -WorkingDirectory $root -TimeoutSeconds 300 -Name "fifth-dotnet-tool-restore" | Out-Null

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_apphub_migration_verify;Username=nerv;Password=nerv"
  } -ScriptBlock {
    Invoke-DotNet -Arguments @("test", $appHubTests, "--filter", "FullyQualifiedName~AppHubPostgresProfileTests") -WorkingDirectory $root -TimeoutSeconds 600 -Name "fifth-apphub-postgres-profile-tests" | Out-Null
  }

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_ops_migration_verify;Username=nerv;Password=nerv"
  } -ScriptBlock {
    Invoke-DotNet -Arguments @("test", $opsTests, "--filter", "FullyQualifiedName~OpsPostgresProfileTests") -WorkingDirectory $root -TimeoutSeconds 600 -Name "fifth-ops-postgres-profile-tests" | Out-Null
  }

  Invoke-DotNet -Arguments @("test", "backend/Nerv.IIP.sln") -WorkingDirectory $root -TimeoutSeconds 900 -Name "fifth-backend-solution-tests" | Out-Null
  Invoke-DotNet -Arguments @("test", "connector-hosts/Nerv.IIP.ConnectorHost.sln") -WorkingDirectory $root -TimeoutSeconds 900 -Name "fifth-connector-host-solution-tests" | Out-Null
}

Write-Host "Fifth slice release-grade persistence foundation verified."
```

- [x] **Step 2: Run the no-exemption gate for the fifth script**

Run:

```powershell
$emptyBaseline = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-empty-script-governance-baseline.json"
[System.IO.File]::WriteAllText($emptyBaseline, '{"schema":1,"exemptions":[]}', [System.Text.UTF8Encoding]::new($false))
try {
  pwsh scripts/check-script-governance.ps1 -Path scripts/verify-fifth-slice-persistence-foundation.ps1 -BaselinePath $emptyBaseline
}
finally {
  Remove-Item -LiteralPath $emptyBaseline -Force -ErrorAction SilentlyContinue
}
```

Expected: PASS.

## Task 3: Migrate Fourth-Stage Verify Script

**Files:**

- Replace: `scripts/verify-fourth-slice-real-infra.ps1`

- [x] **Step 1: Replace the script with a helper-governed version**

Replace the full contents of `scripts/verify-fourth-slice-real-infra.ps1` with:

```powershell
# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local PostgreSQL, Redis and RabbitMQ from infra/docker-compose.dev.yml
#     - Recreates disposable AppHub and Ops verification databases
#     - Runs the third-stage console verification under PostgreSQL profile
#   Writes:
#     - artifacts/script-logs/**
#     - frontend/packages/api-client/openapi/platform-gateway.v1.json through the nested third-stage verification
#     - frontend/packages/api-client/src/** through the nested third-stage verification
#   Cleanup:
#     - Restores scoped environment variables
#     - Stops managed nested script process if it times out through ScriptAutomation.ps1
#     - Leaves shared Docker development services running
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop
#     - Node.js 22.22.3
#     - pnpm 10.13.1

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Wait-TcpPort {
  param(
    [string]$HostName,
    [int]$Port,
    [int]$TimeoutSeconds = 60
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
      $connectTask = $client.ConnectAsync($HostName, $Port)
      if ($connectTask.Wait(1000) -and $client.Connected) {
        return
      }
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
    finally {
      $client.Dispose()
    }

    Start-Sleep -Milliseconds 500
  } while ((Get-Date) -lt $deadline)

  throw "TCP port $HostName`:$Port did not become available within $TimeoutSeconds seconds."
}

function Invoke-PostgresProfileTest {
  param(
    [string]$Project,
    [string]$Filter,
    [string]$ConnectionString,
    [string]$Name
  )

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_TEST_POSTGRES = $ConnectionString
  } -ScriptBlock {
    Invoke-DotNet -Arguments @("test", $Project, "--filter", $Filter) -WorkingDirectory $root -TimeoutSeconds 600 -Name $Name | Out-Null
  }
}

function Reset-PostgresDatabase {
  param(
    [string]$ComposeFile,
    [string]$DatabaseName,
    [string]$Name
  )

  Invoke-DockerCompose -Arguments @("-f", $ComposeFile, "exec", "-T", "postgres", "psql", "-U", "nerv", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-c", "DROP DATABASE IF EXISTS $DatabaseName WITH (FORCE);") -WorkingDirectory $root -TimeoutSeconds 120 -Name "$Name-drop" | Out-Null
  Invoke-DockerCompose -Arguments @("-f", $ComposeFile, "exec", "-T", "postgres", "psql", "-U", "nerv", "-d", "postgres", "-v", "ON_ERROR_STOP=1", "-c", "CREATE DATABASE $DatabaseName OWNER nerv;") -WorkingDirectory $root -TimeoutSeconds 120 -Name "$Name-create" | Out-Null
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$appHubTestConnectionString = "Host=localhost;Port=$postgresPort;Database=nerv_iip_apphub_test;Username=nerv;Password=nerv"
$opsTestConnectionString = "Host=localhost;Port=$postgresPort;Database=nerv_iip_ops_test;Username=nerv;Password=nerv"
$appHubVerifyDatabase = "nerv_iip_apphub_verify"
$opsVerifyDatabase = "nerv_iip_ops_verify"
$appHubVerifyConnectionString = "Host=localhost;Port=$postgresPort;Database=$appHubVerifyDatabase;Username=nerv;Password=nerv"
$opsVerifyConnectionString = "Host=localhost;Port=$postgresPort;Database=$opsVerifyDatabase;Username=nerv;Password=nerv"
$appHubTests = Join-Path $root "backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj"
$opsTests = Join-Path $root "backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj"
$thirdStageScript = Join-Path $root "scripts/verify-third-slice-console.ps1"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "Docker CLI is required to verify fourth slice real infrastructure."
}

Invoke-WithScopedEnvironment -Variables @{
  NERV_IIP_POSTGRES_PORT = $postgresPort
} -ScriptBlock {
  Invoke-DockerCompose -Arguments @("-f", $composeFile, "up", "-d", "postgres", "redis", "rabbitmq") -WorkingDirectory $root -TimeoutSeconds 240 -Name "fourth-docker-compose-dependencies" | Out-Null

  Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort) -TimeoutSeconds 90
  Wait-TcpPort -HostName "localhost" -Port 6379 -TimeoutSeconds 60
  Wait-TcpPort -HostName "localhost" -Port 5672 -TimeoutSeconds 60

  Reset-PostgresDatabase -ComposeFile $composeFile -DatabaseName $appHubVerifyDatabase -Name "fourth-apphub-verify-database"
  Reset-PostgresDatabase -ComposeFile $composeFile -DatabaseName $opsVerifyDatabase -Name "fourth-ops-verify-database"

  Invoke-PostgresProfileTest -Project $appHubTests -Filter "FullyQualifiedName~AppHubPostgresProfileTests" -ConnectionString $appHubTestConnectionString -Name "fourth-apphub-postgres-profile-tests"
  Invoke-PostgresProfileTest -Project $opsTests -Filter "FullyQualifiedName~OpsPostgresProfileTests" -ConnectionString $opsTestConnectionString -Name "fourth-ops-postgres-profile-tests"

  Invoke-WithScopedEnvironment -Variables @{
    NERV_IIP_APPHUB_POSTGRES = $appHubVerifyConnectionString
    NERV_IIP_OPS_POSTGRES = $opsVerifyConnectionString
  } -ScriptBlock {
    Invoke-PwshScript -ScriptPath $thirdStageScript -Arguments @("-UsePostgres") -WorkingDirectory $root -TimeoutSeconds 1200 -Name "fourth-third-stage-console-postgres" | Out-Null
  }
}

Write-Host "Fourth vertical slice real infrastructure verified."
```

- [x] **Step 2: Run the correct no-exemption gate for the fourth script**

Run:

```powershell
$emptyBaseline = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-iip-empty-script-governance-baseline.json"
[System.IO.File]::WriteAllText($emptyBaseline, '{"schema":1,"exemptions":[]}', [System.Text.UTF8Encoding]::new($false))
try {
  pwsh scripts/check-script-governance.ps1 -Path scripts/verify-fourth-slice-real-infra.ps1 -BaselinePath $emptyBaseline
}
finally {
  Remove-Item -LiteralPath $emptyBaseline -Force -ErrorAction SilentlyContinue
}
```

Expected: PASS.

## Task 4: Remove Priority Script Exemptions

**Files:**

- Modify: `scripts/script-governance-baseline.json`

- [x] **Step 1: Remove the fourth/fifth exemptions only**

Replace `scripts/script-governance-baseline.json` with:

```json
{
  "schema": 1,
  "exemptions": [
    {
      "path": "scripts/export-gateway-openapi.ps1",
      "rules": [
        "MissingGovernanceHeader",
        "MissingCategory",
        "MissingHelper",
        "ForbiddenCommand",
        "DynamicInvocation",
        "ForbiddenProcessStart"
      ]
    },
    {
      "path": "scripts/verify-first-slice.ps1",
      "rules": [
        "MissingGovernanceHeader",
        "MissingCategory",
        "MissingHelper",
        "ForbiddenCommand",
        "DynamicInvocation",
        "ForbiddenProcessStart"
      ]
    },
    {
      "path": "scripts/verify-second-slice-ops.ps1",
      "rules": [
        "MissingGovernanceHeader",
        "MissingCategory",
        "MissingHelper",
        "ForbiddenCommand",
        "DynamicInvocation",
        "ForbiddenProcessStart"
      ]
    },
    {
      "path": "scripts/verify-third-slice-console.ps1",
      "rules": [
        "MissingGovernanceHeader",
        "MissingCategory",
        "MissingHelper",
        "ForbiddenCommand",
        "DynamicInvocation",
        "ForbiddenProcessStart"
      ]
    }
  ]
}
```

- [x] **Step 2: Run the script governance harness**

Run:

```powershell
pwsh scripts/tests/check-script-governance.Tests.ps1
```

Expected: PASS, including the two no-exemption assertions added in Task 1.

- [x] **Step 3: Run the repository script governance gate**

Run:

```powershell
pwsh scripts/check-script-governance.ps1
```

Expected: PASS with the remaining legacy exemptions only for export, first, second and third scripts.

## Task 5: Add Non-Windows Compatibility Gate

**Files:**

- Create: `scripts/check-script-compatibility.ps1`

- [x] **Step 1: Add the compatibility gate script**

Create `scripts/check-script-compatibility.ps1`:

```powershell
# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs script governance and compatibility verification commands
#     - Optionally runs the IAM persistent auth verification script
#   Writes:
#     - artifacts/script-logs/**
#     - artifacts/script-logs/script-compatibility/**/evidence.json
#   Cleanup:
#     - Stops managed child process trees through ScriptAutomation.ps1 when commands time out
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Compose v2 when running without -FastOnly

[CmdletBinding()]
param(
  [switch]$FastOnly,
  [switch]$AllowWindows,
  [string]$EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

if ($IsWindows -and -not $AllowWindows) {
  throw "Script compatibility gate must run on macOS or Linux. Use -AllowWindows only for a local smoke run."
}

if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
  $evidenceDirectory = New-ScriptAutomationLogDirectory -Name "script-compatibility"
  $EvidencePath = Join-Path $evidenceDirectory "evidence.json"
}
else {
  $evidenceDirectory = Split-Path -Parent $EvidencePath
  if (-not [string]::IsNullOrWhiteSpace($evidenceDirectory)) {
    New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
  }
}

$commandRecords = New-Object System.Collections.Generic.List[object]

function Invoke-RecordedNativeCommand {
  param(
    [Parameter(Mandatory)]
    [string]$Command,

    [string[]]$Arguments = @(),

    [Parameter(Mandatory)]
    [string]$Name,

    [int]$TimeoutSeconds = 120
  )

  $startedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
  try {
    $result = Invoke-NativeCommandWithTimeout -Command $Command -Arguments $Arguments -WorkingDirectory $root -TimeoutSeconds $TimeoutSeconds -Name $Name
    $stdout = if (Test-Path $result.StdoutPath) { (Get-Content $result.StdoutPath -Raw).Trim() } else { "" }
    $commandRecords.Add([pscustomobject]@{
      name = $Name
      command = $Command
      arguments = $Arguments
      exitCode = $result.ExitCode
      startedAtUtc = $startedAtUtc
      durationMs = $result.Duration.TotalMilliseconds
      stdout = $stdout
      logDirectory = $result.LogDirectory
    })
    return $result
  }
  catch {
    $commandRecords.Add([pscustomobject]@{
      name = $Name
      command = $Command
      arguments = $Arguments
      exitCode = -1
      startedAtUtc = $startedAtUtc
      durationMs = 0
      stdout = ""
      logDirectory = ""
      error = $_.Exception.Message
    })
    throw
  }
}

function Invoke-RecordedPwshScript {
  param(
    [Parameter(Mandatory)]
    [string]$ScriptPath,

    [string[]]$Arguments = @(),

    [Parameter(Mandatory)]
    [string]$Name,

    [int]$TimeoutSeconds = 300
  )

  Invoke-RecordedNativeCommand -Command "pwsh" -Arguments (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $Arguments) -Name $Name -TimeoutSeconds $TimeoutSeconds | Out-Null
}

try {
  Invoke-RecordedNativeCommand -Command "dotnet" -Arguments @("--version") -Name "compat-dotnet-version" -TimeoutSeconds 60 | Out-Null
  Invoke-RecordedNativeCommand -Command "docker" -Arguments @("compose", "version", "--short") -Name "compat-docker-compose-version" -TimeoutSeconds 60 | Out-Null
  Invoke-RecordedPwshScript -ScriptPath (Join-Path $root "scripts/check-script-governance.ps1") -Name "compat-script-governance" -TimeoutSeconds 120
  Invoke-RecordedPwshScript -ScriptPath (Join-Path $root "scripts/tests/check-script-governance.Tests.ps1") -Name "compat-script-governance-tests" -TimeoutSeconds 180
  Invoke-RecordedNativeCommand -Command "git" -Arguments @("diff", "--check") -Name "compat-git-diff-check" -TimeoutSeconds 120 | Out-Null

  if (-not $FastOnly) {
    Invoke-RecordedPwshScript -ScriptPath (Join-Path $root "scripts/verify-iam-persistent-auth-foundation.ps1") -Name "compat-iam-persistent-auth-verify" -TimeoutSeconds 1200
  }
}
finally {
  $evidence = [ordered]@{
    schema = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    osDescription = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    isWindows = $IsWindows
    isLinux = $IsLinux
    isMacOS = $IsMacOS
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    fastOnly = $FastOnly.IsPresent
    commands = @($commandRecords)
  }

  $json = ($evidence | ConvertTo-Json -Depth 20) + [Environment]::NewLine
  [System.IO.File]::WriteAllText($EvidencePath, $json, [System.Text.UTF8Encoding]::new($false))
  Write-Host "Script compatibility evidence written to $EvidencePath"
}

Write-Host "Script compatibility gate verified."
```

- [x] **Step 2: Run a Windows smoke pass without claiming compatibility**

Run:

```powershell
pwsh scripts/check-script-compatibility.ps1 -AllowWindows -FastOnly
```

Expected: PASS and an evidence JSON is written under `artifacts/script-logs/script-compatibility/**/evidence.json`. This is a smoke pass only, not the macOS/Linux compatibility evidence.

- [x] **Step 3: Run the script governance gate after adding the new script**

Run:

```powershell
pwsh scripts/check-script-governance.ps1
```

Expected: PASS. The new compatibility script has a governance header, helper dot-source and no direct forbidden native command invocation.

## Task 6: Run Non-Windows Compatibility Gate And Record Evidence

**Files:**

- Generated by script: `artifacts/script-logs/script-compatibility/**/evidence.json`

- [x] **Step 1: Verify WSL Ubuntu is available on this machine**

Run:

```powershell
wsl -l -q
```

Expected: output includes `Ubuntu`. If `Ubuntu` is missing, run the same gate on another macOS or Linux machine and copy no logs into git unless explicitly requested.

- [x] **Step 2: Run the full compatibility gate in Ubuntu**

Run:

```powershell
wsl -d Ubuntu -- bash -lc 'cd /mnt/c/Users/Mang/.codex/worktrees/bcca/Nerv-IIP && pwsh scripts/check-script-compatibility.ps1'
```

Expected: PASS and final output `Script compatibility gate verified.` The evidence JSON must show `isLinux: true`, `isWindows: false`, and include successful records for script governance, governance tests, `git diff --check`, Docker Compose version and IAM persistent auth verification.

- [x] **Step 3: Confirm fallback was not needed**

Step 2 passed with Docker Compose v2 available from Ubuntu, so the fallback fast-only run was not needed. The fallback command would be:

```powershell
wsl -d Ubuntu -- bash -lc 'cd /mnt/c/Users/Mang/.codex/worktrees/bcca/Nerv-IIP && pwsh scripts/check-script-compatibility.ps1 -FastOnly'
```

Expected if needed: PASS for `compat-fast`. Because the full gate passed, the compatibility backlog item is closed.

## Task 7: Update Architecture And Plan Documentation

**Files:**

- Modify: `docs/architecture/script-automation-governance.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/superpowers/plans/2026-05-17-script-automation-governance.md`

- [x] **Step 1: Update the script migration matrix**

In `docs/architecture/script-automation-governance.md`, change the migration matrix rows for the fourth and fifth scripts to:

```markdown
| `verify-fifth-slice-persistence-foundation.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose、dotnet、solution tests 和 scoped PostgreSQL test environment；baseline exemption 已移除。 |
| `verify-fourth-slice-real-infra.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose、PostgreSQL reset、AppHub/Ops profile tests 和嵌套第三阶段脚本；baseline exemption 已移除。 |
```

- [x] **Step 2: Document the compatibility gate entry point**

In the `跨平台兼容门禁` section of `docs/architecture/script-automation-governance.md`, add this paragraph after the three-step compatibility sequence:

```markdown
仓库提供 `scripts/check-script-compatibility.ps1` 作为本地兼容门禁入口。默认必须在 macOS 或 Linux 上运行；`-AllowWindows -FastOnly` 只用于 Windows 本地 smoke，不可作为兼容性声明依据。脚本会将 OS、PowerShell、.NET SDK、Docker Compose、执行命令、退出码和日志位置写入 `artifacts/script-logs/script-compatibility/**/evidence.json`。
```

- [x] **Step 3: Update implementation readiness**

In `docs/architecture/implementation-readiness.md`, update the current conclusion about script governance to:

```markdown
20. 脚本自动化治理已冻结到 ADR 0010 和 docs/architecture/script-automation-governance.md；IAM、第五阶段和第四阶段核心 verify 脚本已迁移到 helper 门禁，新增或修改脚本必须声明分类、副作用、日志、清理和 helper 使用方式。
```

In the "可以并行但不阻塞开工的事项" list, replace the existing script migration item with:

```markdown
10. 剩余 legacy 脚本继续迁移到 docs/architecture/script-automation-governance.md 的 helper 和门禁；剩余顺序是 OpenAPI 导出、第三阶段 console、第二阶段 Ops、第一阶段 slice。
```

- [x] **Step 4: Close the script governance backlog checkboxes after verification passes**

In `docs/superpowers/plans/2026-05-17-script-automation-governance.md`, update the follow-up backlog to:

```markdown
## Follow-up Backlog

- [x] Continue migrating legacy `verify` scripts to `scripts/lib/ScriptAutomation.ps1`, prioritizing `verify-fifth-slice-persistence-foundation.ps1` and `verify-fourth-slice-real-infra.ps1`.
- [x] Add and run a macOS/Linux compatibility gate: at minimum `pwsh scripts/check-script-governance.ps1`, `pwsh scripts/tests/check-script-governance.Tests.ps1`, `git diff --check`, and the migrated core verify script `pwsh scripts/verify-iam-persistent-auth-foundation.ps1` in a non-Windows environment.

Completion note: `scripts/check-script-compatibility.ps1` records compatibility evidence under `artifacts/script-logs/script-compatibility/**/evidence.json`. The fourth/fifth verify scripts have had their priority baseline exemptions removed. Remaining legacy scripts are tracked as follow-on migration work, not blockers for the next feature stage.
```

Use the checked compatibility line only after Task 6 Step 2 passes. If only `-FastOnly` passed outside Windows, leave the second checkbox unchecked and add a blocker note instead.

## Task 8: Final Verification And Commit

**Files:**

- All files changed by Tasks 1-7.

- [x] **Step 1: Run script governance tests**

Run:

```powershell
pwsh scripts/tests/check-script-governance.Tests.ps1
pwsh scripts/check-script-governance.ps1
```

Expected: both exit `0`.

- [x] **Step 2: Run migrated priority verify scripts on Windows**

Run:

```powershell
pwsh scripts/verify-fifth-slice-persistence-foundation.ps1
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

Expected final lines:

```text
Fifth slice release-grade persistence foundation verified.
Fourth vertical slice real infrastructure verified.
```

- [x] **Step 3: Run compatibility gate**

Run:

```powershell
pwsh scripts/check-script-compatibility.ps1 -AllowWindows -FastOnly
wsl -d Ubuntu -- bash -lc 'cd /mnt/c/Users/Mang/.codex/worktrees/bcca/Nerv-IIP && pwsh scripts/check-script-compatibility.ps1'
```

Expected: Windows smoke and Ubuntu full gate both exit `0`. If the Ubuntu full gate fails because Docker Compose is unavailable, run the Ubuntu `-FastOnly` command from Task 6 and do not mark the full compatibility backlog closed.

- [x] **Step 4: Run repository hygiene checks**

Run:

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
git diff --check
git status --short
```

Expected: backend tests and diff check exit `0`. `git status --short` shows only intended script governance changes plus pre-existing `skills-lock.json`.

- [x] **Step 5: Commit script governance backlog completion**

Run:

```powershell
git add scripts/tests/check-script-governance.Tests.ps1
git add scripts/verify-fifth-slice-persistence-foundation.ps1
git add scripts/verify-fourth-slice-real-infra.ps1
git add scripts/script-governance-baseline.json
git add scripts/check-script-compatibility.ps1
git add docs/architecture/script-automation-governance.md
git add docs/architecture/implementation-readiness.md
git add docs/superpowers/plans/2026-05-17-script-automation-governance.md
git add docs/superpowers/plans/2026-05-17-script-governance-backlog-completion.md
git commit -m "chore: close script governance backlog"
```

Expected: commit succeeds. Do not stage `skills-lock.json` or generated `artifacts/script-logs/**` evidence files unless the user explicitly asks to preserve compatibility evidence in git.

## Execution Order

1. Task 0 first, so the current IAM authorization audit is preserved in a focused commit.
2. Task 1 establishes the red script governance regression test.
3. Tasks 2 and 3 can run in parallel because their write sets are disjoint.
4. Task 4 runs after both scripts pass without exemptions.
5. Task 5 adds the compatibility entry point after the priority scripts are migrated.
6. Task 6 runs after Task 5 so it can use the new gate.
7. Task 7 updates durable documentation only after verification evidence exists.
8. Task 8 performs final verification and commit.

## Self Review

Spec coverage:

1. Priority legacy verify migration is covered by Tasks 2, 3 and 4.
2. Script governance test coverage is covered by Tasks 1 and 8.
3. macOS/Linux compatibility gate and evidence are covered by Tasks 5 and 6.
4. Documentation and previous plan backlog closure are covered by Task 7.
5. Current IAM audit handoff is covered by Task 0.

Red-flag scan:

1. No empty task sections remain.
2. No unbounded "migrate everything" step remains.
3. Every script-changing task names exact files and concrete replacement content.
4. Every verification step has concrete commands and expected outcomes.

Type and command consistency:

1. Helper names match `scripts/lib/ScriptAutomation.ps1`: `Invoke-DotNet`, `Invoke-DockerCompose`, `Invoke-PwshScript`, `Invoke-NativeCommandWithTimeout`, `Invoke-WithScopedEnvironment`, and `New-ScriptAutomationLogDirectory`.
2. The fourth/fifth migrated script names match the baseline JSON paths.
3. The compatibility evidence path is consistently `artifacts/script-logs/script-compatibility/**/evidence.json`.
