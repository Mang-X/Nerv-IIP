# 脚本治理待办收尾实施计划

> **面向智能体执行者：** 必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 在开始下一功能阶段之前关闭剩余的脚本自动化治理待办：完成当前 IAM 授权审核交接，迁移优先级较高的遗留 verify 脚本，移除其治理豁免，并采集非 Windows 兼容性证据。

**架构：** 以 ADR 0010 和 `docs/architecture/script-automation-governance.md` 作为决策边界。将 `scripts/lib/ScriptAutomation.ps1` 作为长时间运行的原生命令、Docker Compose、嵌套 PowerShell 脚本、作用域环境变量和进程诊断的唯一包装器。添加可在 WSL、macOS 或 Linux 中运行的小型兼容性门禁脚本，记录精确的命令/版本证据，而不是仅凭意图声称支持。

**技术栈：** PowerShell 7、.NET 10、Docker Compose v2、Git、WSL Ubuntu 或其他 macOS/Linux runner、现有 xUnit 和前端验证脚本。

---

## 完成记录

本计划从提交 `8c6bcde Merge pull request #12 from Mang-X/codex/iam-persistent-auth-foundation` 开始；当前以 detached `HEAD` 检出，`main` 和 `origin/main` 指向同一提交。

已知交接说明：

1. 本计划开始前 `skills-lock.json` 已处于脏状态。除非用户明确要求，否则不得暂存、编辑或还原该文件。
2. 合并后的 IAM 审核已产生本地变更，在访问持久化之前保护 PostgreSQL IAM 用户/角色管理 endpoint。保持这些变更与脚本治理提交分离。
3. 脚本治理计划 `docs/superpowers/plans/2026-05-17-script-automation-governance.md` 仍有两个开放待办：迁移优先级较高的第四/第五阶段 verify 脚本，以及运行 macOS/Linux 兼容性门禁。

## 执行记录

1. 创建分支 `codex/script-governance-backlog-completion`，起点为 `8c6bcde`。
2. 将 IAM 审核交接单独提交为 `99970a6 fix: guard iam management endpoints`。
3. 以 `70aabd1 test: cover priority script governance backlog` 添加优先脚本无豁免治理覆盖。
4. 以 `d9dd810 chore: migrate fifth verify script governance` 迁移第五阶段 verify 脚本。
5. 以 `71e073e chore: migrate fourth verify script governance` 迁移第四阶段 verify 脚本。
6. 以 `3691f49 chore: remove priority script exemptions` 移除第四/第五阶段优先脚本豁免。
7. 以 `396f281 chore: add script compatibility gate` 添加兼容性门禁。
8. 运行完整 Ubuntu WSL 兼容性门禁，证据位于 `artifacts/script-logs/script-compatibility/20260518-000559-198/evidence.json`：Ubuntu 22.04.3 LTS、PowerShell 7.6.1、.NET SDK 10.0.300、Docker Compose 5.1.3、`fastOnly: false`，IAM 持久化认证 verify 通过。
9. 使兼容性脚本与文档记录的 `compat-fast` 回退方案一致，因此 `-FastOnly` 不再探测 Docker Compose，完整模式分类为 `verify`。
10. 重新运行最终 Windows 门禁：脚本治理测试、脚本治理门禁、Windows 快速兼容性 smoke、第五阶段 verify 脚本、第四阶段 verify 脚本、后端解决方案测试和 `git diff --check`。
11. 未将预先存在的 `skills-lock.json` 和生成的 `artifacts/script-logs/**` 证据纳入 git。

## 边界

1. 本计划不得启动 Gateway 全局授权、Console 登录 UI、FileStorage、Notification、高风险 Ops 审批或部署安装程序工作。
2. 不得在一次工作中迁移所有遗留脚本。必需的迁移目标是 `verify-fifth-slice-persistence-foundation.ps1` 和 `verify-fourth-slice-real-infra.ps1`。
3. 除非在另一份已批准计划中迁移 `export-gateway-openapi.ps1`、`verify-first-slice.ps1`、`verify-second-slice-ops.ps1` 或 `verify-third-slice-console.ps1`，否则不得移除这些脚本的豁免。
4. 本次不得添加特定 CI 提供商的 `.github` 文件。非 Windows 门禁由仓库本地脚本和记录的证据组成。
5. 除非门禁确实在 Windows 之外运行，并且证据文件记录 OS、PowerShell、.NET 和 Docker Compose 详情，否则不得声称支持 macOS/Linux。
6. 不得暂存无关的 `skills-lock.json`。

## 文件结构图

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

## 任务 0：稳定当前 IAM 审核交接

**文件：**

- 稍后暂存：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/IamEndpointAuthorization.cs`
- 稍后暂存：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Users/UserEndpoints.cs`
- 稍后暂存：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Endpoints/Roles/RoleEndpoints.cs`
- 稍后暂存：`backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamManagementEndpointAuthorizationTests.cs`
- 稍后暂存：`docs/architecture/iam-authentication-baseline.md`
- 稍后暂存：`docs/architecture/database-schema-catalog.md`
- 稍后暂存：`docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md`

- [x] **步骤 1：从 detached HEAD 创建工作分支**

运行：

```powershell
git switch -c codex/script-governance-backlog-completion
```

预期结果：从 `8c6bcde` 成功创建分支。如果分支已存在，运行 `git switch codex/script-governance-backlog-completion` 并继续。

- [x] **步骤 2：确认 IAM 审核变更是唯一的非脚本工作**

运行：

```powershell
git status --short --branch
```

预期结果：状态包含上面列出的 IAM endpoint/测试/文档变更、预先存在的 `skills-lock.json`，且没有已暂存文件。

- [x] **步骤 3：提交 IAM 审核前重新运行验证**

运行：

```powershell
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore
dotnet test backend/Nerv.IIP.sln --no-restore
pwsh scripts/check-script-governance.ps1
git diff --check
```

预期结果：每条命令都以 `0` 退出。`git diff --check` 可能在命令摘要前打印行尾警告，但不得报告空白错误。

- [x] **步骤 4：单独提交 IAM 审核修复**

运行：

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

预期结果：提交成功，`skills-lock.json` 保持未暂存。

## 任务 1：为优先脚本治理添加回归覆盖

**文件：**

- 修改：`scripts/tests/check-script-governance.Tests.ps1`

- [x] **步骤 1：添加无豁免运行治理门禁的辅助函数**

在 `Invoke-GovernanceCase` 之后追加此辅助函数，位置在 `scripts/tests/check-script-governance.Tests.ps1` 中：

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

- [x] **步骤 2：添加优先脚本断言**

在现有夹具场景之后、辅助 smoke 块之前添加这些调用：

```powershell
Invoke-GovernanceScriptCase -RelativePath 'scripts/verify-fifth-slice-persistence-foundation.ps1'
Invoke-GovernanceScriptCase -RelativePath 'scripts/verify-fourth-slice-real-infra.ps1'
```

- [x] **步骤 3：运行测试工具并验证预期红灯状态**

运行：

```powershell
pwsh scripts/tests/check-script-governance.Tests.ps1
```

预期结果：失败，因为第五和第四阶段 verify 脚本仍依赖基线豁免来允许缺失治理头、缺失辅助库用法和直接原生命令。

## 任务 2：迁移第五阶段 Verify 脚本

**文件：**

- 替换：`scripts/verify-fifth-slice-persistence-foundation.ps1`

- [x] **步骤 1：使用由辅助库治理的版本替换脚本**

将 `scripts/verify-fifth-slice-persistence-foundation.ps1` 的全部内容替换为：

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

- [x] **步骤 2：为第五阶段脚本运行无豁免门禁**

运行：

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

预期结果：通过。

## 任务 3：迁移第四阶段 Verify 脚本

**文件：**

- 替换：`scripts/verify-fourth-slice-real-infra.ps1`

- [x] **步骤 1：使用由辅助库治理的版本替换脚本**

将 `scripts/verify-fourth-slice-real-infra.ps1` 的全部内容替换为：

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

- [x] **步骤 2：为第四阶段脚本运行正确的无豁免门禁**

运行：

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

预期结果：通过。

## 任务 4：移除优先脚本豁免

**文件：**

- 修改：`scripts/script-governance-baseline.json`

- [x] **步骤 1：仅移除第四/第五阶段豁免**

将 `scripts/script-governance-baseline.json` 替换为：

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

- [x] **步骤 2：运行脚本治理测试工具**

运行：

```powershell
pwsh scripts/tests/check-script-governance.Tests.ps1
```

预期结果：通过，包括任务 1 添加的两个无豁免断言。

- [x] **步骤 3：运行仓库脚本治理门禁**

运行：

```powershell
pwsh scripts/check-script-governance.ps1
```

预期结果：通过，其余遗留豁免仅适用于 export、第一、第二和第三阶段脚本。

## 任务 5：添加非 Windows 兼容性门禁

**文件：**

- 创建：`scripts/check-script-compatibility.ps1`

- [x] **步骤 1：添加兼容性门禁脚本**

创建 `scripts/check-script-compatibility.ps1`：

```powershell
# Script-Governance:
#   Category: verify
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
  Invoke-RecordedPwshScript -ScriptPath (Join-Path $root "scripts/check-script-governance.ps1") -Name "compat-script-governance" -TimeoutSeconds 120
  Invoke-RecordedPwshScript -ScriptPath (Join-Path $root "scripts/tests/check-script-governance.Tests.ps1") -Name "compat-script-governance-tests" -TimeoutSeconds 180
  Invoke-RecordedNativeCommand -Command "git" -Arguments @("diff", "--check") -Name "compat-git-diff-check" -TimeoutSeconds 120 | Out-Null

  if (-not $FastOnly) {
    Invoke-RecordedNativeCommand -Command "docker" -Arguments @("compose", "version", "--short") -Name "compat-docker-compose-version" -TimeoutSeconds 60 | Out-Null
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

- [x] **步骤 2：运行 Windows smoke 验证但不声称兼容性**

运行：

```powershell
pwsh scripts/check-script-compatibility.ps1 -AllowWindows -FastOnly
```

预期结果：通过，并在 `artifacts/script-logs/script-compatibility/**/evidence.json` 下写入 evidence JSON。这只是 smoke 验证，不是 macOS/Linux 兼容性证据。

- [x] **步骤 3：添加新脚本后运行脚本治理门禁**

运行：

```powershell
pwsh scripts/check-script-governance.ps1
```

预期结果：通过。新兼容性脚本具有治理头、辅助库 dot-source，且没有直接调用被禁止的原生命令。

## 任务 6：运行非 Windows 兼容性门禁并记录证据

**文件：**

- 由脚本生成：`artifacts/script-logs/script-compatibility/**/evidence.json`

- [x] **步骤 1：验证本机可用 WSL Ubuntu**

运行：

```powershell
wsl -l -q
```

预期结果：输出包含 `Ubuntu`。如果缺少 `Ubuntu`，在另一台 macOS 或 Linux 机器上运行相同门禁；除非明确要求，否则不要将日志复制到 git。

- [x] **步骤 2：在 Ubuntu 中运行完整兼容性门禁**

运行：

```powershell
wsl -d Ubuntu -- bash -lc 'cd /mnt/c/Users/Mang/.codex/worktrees/bcca/Nerv-IIP && pwsh scripts/check-script-compatibility.ps1'
```

预期结果：通过，最终输出为 `Script compatibility gate verified.`。evidence JSON 必须显示 `isLinux: true`、`isWindows: false`，并包含脚本治理、治理测试、`git diff --check`、Docker Compose 版本和 IAM 持久化认证验证的成功记录。

- [x] **步骤 3：确认无需回退方案**

步骤 2 已通过，Ubuntu 中可用 Docker Compose v2，因此无需仅快速模式的回退运行。回退命令为：

```powershell
wsl -d Ubuntu -- bash -lc 'cd /mnt/c/Users/Mang/.codex/worktrees/bcca/Nerv-IIP && pwsh scripts/check-script-compatibility.ps1 -FastOnly'
```

如需回退，预期结果为：`compat-fast` 通过。由于完整门禁已通过，兼容性待办项已经关闭。

## 任务 7：更新架构和计划文档

**文件：**

- 修改：`docs/architecture/script-automation-governance.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/superpowers/plans/2026-05-17-script-automation-governance.md`

- [x] **步骤 1：更新脚本迁移矩阵**

在 `docs/architecture/script-automation-governance.md` 中，将第四和第五阶段脚本的迁移矩阵行更改为：

```markdown
| `verify-fifth-slice-persistence-foundation.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose、dotnet、solution tests 和 scoped PostgreSQL test environment；baseline exemption 已移除。 |
| `verify-fourth-slice-real-infra.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose、PostgreSQL reset、AppHub/Ops profile tests 和嵌套第三阶段脚本；baseline exemption 已移除。 |
```

- [x] **步骤 2：记录兼容性门禁入口**

在 `跨平台兼容门禁` 章节中，于三步兼容性序列之后添加此段落，该章节位于 `docs/architecture/script-automation-governance.md`：

```markdown
仓库提供 `scripts/check-script-compatibility.ps1` 作为本地兼容门禁入口。默认必须在 macOS 或 Linux 上运行；`-AllowWindows -FastOnly` 只用于 Windows 本地 smoke，不可作为兼容性声明依据。脚本会将 OS、PowerShell、.NET SDK、执行命令、退出码和日志位置写入 `artifacts/script-logs/script-compatibility/**/evidence.json`；full 模式还会记录 Docker Compose 版本并运行核心 verify 脚本。
```

- [x] **步骤 3：更新实施就绪状态**

在 `docs/architecture/implementation-readiness.md` 中，将脚本治理的当前结论更新为：

```markdown
20. 脚本自动化治理已冻结到 ADR 0010 和 docs/architecture/script-automation-governance.md；IAM、第五阶段和第四阶段核心 verify 脚本已迁移到 helper 门禁，新增或修改脚本必须声明分类、副作用、日志、清理和 helper 使用方式。
```

在“可以并行但不阻塞开工的事项”清单中，将现有脚本迁移项替换为：

```markdown
10. 剩余 legacy 脚本继续迁移到 docs/architecture/script-automation-governance.md 的 helper 和门禁；剩余顺序是 OpenAPI 导出、第三阶段 console、第二阶段 Ops、第一阶段 slice。
```

- [x] **步骤 4：验证通过后关闭脚本治理待办复选框**

在 `docs/superpowers/plans/2026-05-17-script-automation-governance.md` 中，将后续待办更新为：

```markdown
## Follow-up Backlog

- [x] Continue migrating legacy `verify` scripts to `scripts/lib/ScriptAutomation.ps1`, prioritizing `verify-fifth-slice-persistence-foundation.ps1` and `verify-fourth-slice-real-infra.ps1`.
- [x] Add and run a macOS/Linux compatibility gate: at minimum `pwsh scripts/check-script-governance.ps1`, `pwsh scripts/tests/check-script-governance.Tests.ps1`, `git diff --check`, and the migrated core verify script `pwsh scripts/verify-iam-persistent-auth-foundation.ps1` in a non-Windows environment.

Completion note: `scripts/check-script-compatibility.ps1` records compatibility evidence under `artifacts/script-logs/script-compatibility/**/evidence.json`. The fourth/fifth verify scripts have had their priority baseline exemptions removed. Remaining legacy scripts are tracked as follow-on migration work, not blockers for the next feature stage.
```

仅在任务 6 步骤 2 通过后使用已勾选的兼容性行。如果在 Windows 之外只有 `-FastOnly` 通过，保持第二个复选框未勾选，并添加阻塞说明。

## 任务 8：最终验证并提交

**文件：**

- 任务 1–7 更改的所有文件。

- [x] **步骤 1：运行脚本治理测试**

运行：

```powershell
pwsh scripts/tests/check-script-governance.Tests.ps1
pwsh scripts/check-script-governance.ps1
```

预期结果：两者都以 `0` 退出。

- [x] **步骤 2：在 Windows 上运行已迁移的优先 verify 脚本**

运行：

```powershell
pwsh scripts/verify-fifth-slice-persistence-foundation.ps1
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

预期最后几行：

```text
Fifth slice release-grade persistence foundation verified.
Fourth vertical slice real infrastructure verified.
```

- [x] **步骤 3：运行兼容性门禁**

运行：

```powershell
pwsh scripts/check-script-compatibility.ps1 -AllowWindows -FastOnly
wsl -d Ubuntu -- bash -lc 'cd /mnt/c/Users/Mang/.codex/worktrees/bcca/Nerv-IIP && pwsh scripts/check-script-compatibility.ps1'
```

预期结果：Windows smoke 和 Ubuntu 完整门禁均以 `0` 退出。如果 Ubuntu 完整门禁因 Docker Compose 不可用而失败，运行任务 6 中的 Ubuntu `-FastOnly` 命令，并且不要将完整兼容性待办标记为已关闭。

- [x] **步骤 4：运行仓库卫生检查**

运行：

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
git diff --check
git status --short
```

预期结果：后端测试和 diff 检查以 `0` 退出。`git status --short` 只显示预期的脚本治理变更和预先存在的 `skills-lock.json`。

- [x] **步骤 5：提交脚本治理待办收尾**

运行：

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

预期结果：提交成功。除非用户明确要求在 git 中保留兼容性证据，否则不得暂存 `skills-lock.json` 或生成的 `artifacts/script-logs/**` 证据文件。

## 执行顺序

1. 首先执行任务 0，以聚焦的提交保留当前 IAM 授权审核。
2. 任务 1 建立红灯脚本治理回归测试。
3. 任务 2 和任务 3 的写入集合互不相交，因此可以并行运行。
4. 两个脚本都无豁免通过后运行任务 4。
5. 迁移优先脚本后，任务 5 添加兼容性入口。
6. 任务 6 在任务 5 之后运行，以便使用新门禁。
7. 任务 7 仅在验证证据存在后更新持久文档。
8. 任务 8 执行最终验证和提交。

## 自检

规范覆盖：

1. 任务 2、3 和 4 覆盖优先遗留 verify 迁移。
2. 任务 1 和任务 8 覆盖脚本治理测试。
3. 任务 5 和任务 6 覆盖 macOS/Linux 兼容性门禁及证据。
4. 任务 7 覆盖文档和先前计划待办的关闭。
5. 任务 0 覆盖当前 IAM 审核交接。

风险标记扫描：

1. 不保留空任务章节。
2. 不保留无边界的“迁移一切”步骤。
3. 每个脚本变更任务都指定精确文件和具体替换内容。
4. 每个验证步骤都有具体命令和预期结果。

类型和命令一致性：

1. 辅助函数名称与 `scripts/lib/ScriptAutomation.ps1` 一致：`Invoke-DotNet`、`Invoke-DockerCompose`、`Invoke-PwshScript`、`Invoke-NativeCommandWithTimeout`、`Invoke-WithScopedEnvironment` 和 `New-ScriptAutomationLogDirectory`。
2. 已迁移的第四/第五阶段脚本名称与 baseline JSON 路径一致。
3. 兼容性证据路径始终为 `artifacts/script-logs/script-compatibility/**/evidence.json`。
