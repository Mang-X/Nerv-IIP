# Main Platform Development Entrypoint Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a root-level `.\nerv.ps1` development CLI that starts the whole local platform through Aspire, exposes the canonical port matrix, moves platform HTTP ports to `5100-5105`, and switches local MinIO containers to `pgsty/minio`.

**Architecture:** Keep the root CLI thin and put real process execution in governed scripts under `scripts/`. Aspire remains the topology source for full-platform startup; Docker Compose remains dependency-only for `-InfraOnly` and verification scripts. Port changes are applied consistently across launch settings, fallback URLs, AppHost endpoints, Vite config and docs.

**Tech Stack:** PowerShell 7, .NET 10, Aspire 13.3.3, Docker Compose, pnpm 11.1.2, Vite 8, Vue 3.

---

## File Structure

Create:

1. `nerv.ps1` - root command dispatcher for `dev`, `ports` and `help`.
2. `scripts/dev.ps1` - governed development startup script that calls `Invoke-DotNet` or `Invoke-DockerCompose`.
3. `scripts/tests/dev-entrypoint.Tests.ps1` - PowerShell smoke tests for the root command surface and generated output.

Modify:

1. `infra/aspire/Nerv.IIP.AppHost/Program.cs` - fixed local ports and `pgsty/minio` image tag.
2. `infra/docker-compose.dev.yml` - `pgsty/minio` image tag.
3. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Properties/launchSettings.json` - Gateway HTTP port `5100`.
4. `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Properties/launchSettings.json` - AppHub HTTP port `5101`.
5. `backend/services/Iam/src/Nerv.IIP.Iam.Web/Properties/launchSettings.json` - IAM HTTP port `5102`.
6. `backend/services/Ops/src/Nerv.IIP.Ops.Web/Properties/launchSettings.json` - Ops HTTP port `5103`.
7. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Properties/launchSettings.json` - FileStorage HTTP port `5104`.
8. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json` - AppHub/Ops local service URLs.
9. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs` - AppHub/Ops/IAM fallback URLs.
10. `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs` - IAM fallback URL.
11. `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json` - AppHub/Ops local service URLs.
12. `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs` - AppHub/Ops fallback URLs.
13. `frontend/apps/console/package.json` - Console dev script port `5105`.
14. `frontend/apps/console/vite.config.ts` - Vite dev server port `5105`.
15. `frontend/packages/api-client/src/transport/base-url.ts` - keep Gateway default `5100` and verify it matches the matrix.
16. `README.md` - add daily development entrypoint.
17. `docs/architecture/deployment-baseline.md` - record root CLI over governed scripts and local MinIO image baseline.
18. `docs/architecture/implementation-readiness.md` - update current readiness notes after the entrypoint lands.

---

### Task 1: Add Root CLI And Tests

**Files:**
- Create: `nerv.ps1`
- Create: `scripts/tests/dev-entrypoint.Tests.ps1`
- Modify: none

- [ ] **Step 1: Write the failing root command smoke tests**

Create `scripts/tests/dev-entrypoint.Tests.ps1`:

```powershell
# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs root development entrypoint smoke tests
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$nerv = Join-Path $repoRoot 'nerv.ps1'

function Invoke-Nerv {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $output = & pwsh -NoProfile -ExecutionPolicy Bypass -File $nerv @Arguments 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output | Out-String)
    }
}

$help = Invoke-Nerv -Arguments @('help')
if ($help.ExitCode -ne 0) {
    throw "Expected help to exit 0, got $($help.ExitCode). Output: $($help.Output)"
}

foreach ($expected in @('.\nerv.ps1 dev', '.\nerv.ps1 ports', '.\nerv.ps1 help')) {
    if (-not $help.Output.Contains($expected)) {
        throw "Help output did not contain '$expected'. Output: $($help.Output)"
    }
}

$ports = Invoke-Nerv -Arguments @('ports')
if ($ports.ExitCode -ne 0) {
    throw "Expected ports to exit 0, got $($ports.ExitCode). Output: $($ports.Output)"
}

foreach ($expected in @(
    '5100 PlatformGateway',
    '5101 AppHub',
    '5102 IAM',
    '5103 Ops',
    '5104 FileStorage',
    '5105 Console',
    '15432 PostgreSQL',
    '9000 MinIO API',
    '9001 MinIO Console'
)) {
    if (-not $ports.Output.Contains($expected)) {
        throw "Ports output did not contain '$expected'. Output: $($ports.Output)"
    }
}

$unknown = Invoke-Nerv -Arguments @('unknown-command')
if ($unknown.ExitCode -eq 0) {
    throw "Expected unknown command to fail. Output: $($unknown.Output)"
}

if (-not $unknown.Output.Contains("Unknown command 'unknown-command'")) {
    throw "Unknown command output was not helpful. Output: $($unknown.Output)"
}

Write-Host 'Development entrypoint smoke tests passed.'
```

- [ ] **Step 2: Run the smoke tests to verify they fail**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
```

Expected: FAIL because `nerv.ps1` does not exist.

- [ ] **Step 3: Add the root CLI wrapper**

Create `nerv.ps1`:

```powershell
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $Command = 'help',

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $RemainingArguments = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = $PSScriptRoot

function Write-NervHelp {
    Write-Host @'
Nerv-IIP development commands

Usage:
  .\nerv.ps1 dev [-NoBuild] [-InfraOnly] [-OpenDashboard]
  .\nerv.ps1 ports
  .\nerv.ps1 help

Commands:
  dev      Start the local platform through the governed development script.
  ports    Print the canonical local development port matrix.
  help     Print this help.
'@
}

function Write-NervPorts {
    Write-Host @'
Platform services:
  5100 PlatformGateway
  5101 AppHub
  5102 IAM
  5103 Ops
  5104 FileStorage
  5105 Console

Infrastructure services:
  15432 PostgreSQL
  6379 Redis
  5672 RabbitMQ AMQP
  15672 RabbitMQ Management
  9000 MinIO API
  9001 MinIO Console
  4317 OTLP gRPC
  4318 OTLP HTTP
'@
}

switch ($Command.ToLowerInvariant()) {
    'dev' {
        $devScript = Join-Path $repoRoot 'scripts/dev.ps1'
        & $devScript @RemainingArguments
        exit $LASTEXITCODE
    }
    'ports' {
        Write-NervPorts
        exit 0
    }
    'help' {
        Write-NervHelp
        exit 0
    }
    default {
        Write-Host "Unknown command '$Command'."
        Write-NervHelp
        exit 1
    }
}
```

- [ ] **Step 4: Run the smoke tests to verify they pass**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
```

Expected: PASS with `Development entrypoint smoke tests passed.`

- [ ] **Step 5: Commit**

Run:

```powershell
git add nerv.ps1 scripts/tests/dev-entrypoint.Tests.ps1
git commit -m "feat: add root development entrypoint"
```

---

### Task 2: Add Governed Development Startup Script

**Files:**
- Create: `scripts/dev.ps1`
- Modify: `scripts/tests/dev-entrypoint.Tests.ps1`

- [ ] **Step 1: Extend smoke tests for `dev -Help` without starting services**

Append this block before the final `Write-Host` in `scripts/tests/dev-entrypoint.Tests.ps1`:

```powershell
$devHelp = Invoke-Nerv -Arguments @('dev', '-Help')
if ($devHelp.ExitCode -ne 0) {
    throw "Expected dev -Help to exit 0, got $($devHelp.ExitCode). Output: $($devHelp.Output)"
}

foreach ($expected in @('-NoBuild', '-InfraOnly', '-OpenDashboard', 'Aspire AppHost')) {
    if (-not $devHelp.Output.Contains($expected)) {
        throw "dev -Help output did not contain '$expected'. Output: $($devHelp.Output)"
    }
}
```

- [ ] **Step 2: Run the smoke tests to verify they fail**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
```

Expected: FAIL because `scripts/dev.ps1` does not exist.

- [ ] **Step 3: Add `scripts/dev.ps1`**

Create `scripts/dev.ps1`:

```powershell
# Script-Governance:
#   Category: check
#   SideEffects:
#     - Starts the local Nerv-IIP platform through Aspire AppHost or dependency services through Docker Compose
#   Writes:
#     - artifacts/script-logs/** when -InfraOnly uses the Docker Compose helper
#   Cleanup:
#     - Stops the managed command if it times out through ScriptAutomation.ps1
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop for container resources
#     - Node.js 22.22.3
#     - pnpm 11.1.2

[CmdletBinding()]
param(
    [switch] $NoBuild,
    [switch] $InfraOnly,
    [switch] $OpenDashboard,
    [switch] $Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $root 'scripts/lib/ScriptAutomation.ps1')

function Write-DevHelp {
    Write-Host @'
Nerv-IIP local development startup

Usage:
  .\nerv.ps1 dev [-NoBuild] [-InfraOnly] [-OpenDashboard]

Options:
  -NoBuild        Run Aspire AppHost with --no-build.
  -InfraOnly     Start only dependency services from infra/docker-compose.dev.yml.
  -OpenDashboard Print a note that Aspire dashboard URL discovery is manual in this version.

Default behavior:
  Starts the full local platform through the Aspire AppHost.
'@
}

function Assert-CommandAvailable {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string] $Purpose
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is required for $Purpose."
    }
}

if ($Help) {
    Write-DevHelp
    exit 0
}

Set-Location $root

if ($InfraOnly) {
    Assert-CommandAvailable -Name 'docker' -Purpose 'dependency-only startup'
    $composeFile = Join-Path $root 'infra/docker-compose.dev.yml'
    Invoke-DockerCompose -Arguments @('-f', $composeFile, 'up', '-d', 'postgres', 'redis', 'rabbitmq', 'minio', 'otel-collector') -WorkingDirectory $root -TimeoutSeconds 240 -Name 'dev-infra-only' | Out-Null
    Write-Host 'Dependency services are starting from infra/docker-compose.dev.yml.'
    exit 0
}

Assert-CommandAvailable -Name 'dotnet' -Purpose 'Aspire AppHost startup'
Assert-CommandAvailable -Name 'docker' -Purpose 'Aspire container resources'
Assert-CommandAvailable -Name 'node' -Purpose 'Console Vite startup'
Assert-CommandAvailable -Name 'pnpm' -Purpose 'Console Vite startup'

if ($OpenDashboard) {
    Write-Host 'Aspire dashboard URL discovery is manual in this version. Use the URL printed by dotnet run.'
}

$appHostProject = Join-Path $root 'infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj'
$arguments = @('run', '--project', $appHostProject)
if ($NoBuild) {
    $arguments += '--no-build'
}

Invoke-DotNetInteractive -Arguments $arguments -WorkingDirectory $root -Name 'dev-apphost' | Out-Null
```

- [ ] **Step 4: Add interactive native-command helpers**

Add these functions to `scripts/lib/ScriptAutomation.ps1` after `Invoke-DotNet`:

```powershell
function Invoke-NativeCommandInteractive {
    param(
        [Parameter(Mandatory)]
        [string] $Command,

        [string[]] $Arguments = @(),

        [string] $WorkingDirectory = (Get-Location).Path,

        [string] $Name
    )

    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = [System.IO.Path]::GetFileNameWithoutExtension($Command)
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Command
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false

    foreach ($argument in $Arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $rootProcessId = $null

    try {
        $displayArguments = Protect-ScriptAutomationText ($Arguments -join ' ')
        Write-Diagnostic "Starting interactive $Command $displayArguments (cwd=$WorkingDirectory)"

        if (-not $process.Start()) {
            throw "Failed to start command '$Command'."
        }

        $rootProcessId = $process.Id
        $process.WaitForExit()
        $exitCode = $process.ExitCode
        $stopwatch.Stop()

        if ($exitCode -ne 0) {
            throw "Interactive command '$Command' exited with $exitCode after $($stopwatch.Elapsed)."
        }

        Write-Diagnostic "Interactive command completed: $Command (pid=$rootProcessId, durationMs=$($stopwatch.ElapsedMilliseconds))"

        return [pscustomobject]@{
            Command = $Command
            Arguments = $Arguments
            WorkingDirectory = $WorkingDirectory
            ExitCode = $exitCode
            Duration = $stopwatch.Elapsed
            ProcessId = $rootProcessId
        }
    }
    finally {
        if ($process -and -not $process.HasExited) {
            Stop-ProcessTree -ProcessId $process.Id -Reason "Finally cleanup for interactive $Command" | Out-Null
        }

        $process.Dispose()
    }
}

function Invoke-DotNetInteractive {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments,

        [string] $WorkingDirectory = (Get-Location).Path,

        [string] $Name = 'dotnet'
    )

    Invoke-NativeCommandInteractive -Command 'dotnet' -Arguments $Arguments -WorkingDirectory $WorkingDirectory -Name $Name
}
```

This keeps direct process execution inside the shared helper while allowing `.\nerv.ps1 dev` to stream Aspire output, including the dashboard URL, to the current terminal.

- [ ] **Step 5: Run smoke and governance tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

Expected:

```text
Development entrypoint smoke tests passed.
Script governance check passed.
```

- [ ] **Step 6: Commit**

Run:

```powershell
git add scripts/dev.ps1 scripts/tests/dev-entrypoint.Tests.ps1 scripts/lib/ScriptAutomation.ps1
git commit -m "feat: add governed dev startup script"
```

---

### Task 3: Standardize Backend And Connector Ports

**Files:**
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Properties/launchSettings.json`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Properties/launchSettings.json`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Properties/launchSettings.json`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Properties/launchSettings.json`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Properties/launchSettings.json`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json`
- Modify: `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`
- Modify: `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`

- [ ] **Step 1: Update launch settings HTTP ports**

Apply these exact port changes:

```text
PlatformGateway: http://localhost:5073 -> http://localhost:5100
AppHub:          http://localhost:5204 -> http://localhost:5101
IAM:             http://localhost:5283 -> http://localhost:5102
Ops:             http://localhost:5105 -> http://localhost:5103
FileStorage:     http://localhost:5261 -> http://localhost:5104
```

For each `https` profile, keep the existing HTTPS URL and replace only the HTTP segment after the semicolon.

- [ ] **Step 2: Update Gateway development service URLs**

Change `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json` to:

```json
{
  "ServiceName": "platform-gateway",
  "AppHub": {
    "BaseUrl": "http://localhost:5101"
  },
  "Ops": {
    "BaseUrl": "http://localhost:5103"
  },
  "Iam": {
    "BaseUrl": "http://localhost:5102"
  }
}
```

- [ ] **Step 3: Update backend fallback URLs**

In `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`, replace fallbacks:

```csharp
builder.Configuration["AppHub:BaseUrl"] ?? "http://localhost:5101"
builder.Configuration["Ops:BaseUrl"] ?? "http://localhost:5103"
builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102"
```

In `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`, replace the IAM fallback:

```csharp
builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102"
```

- [ ] **Step 4: Update Connector Host development URLs**

Change `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json` to:

```json
{
  "Platform": {
    "AppHubBaseUrl": "http://localhost:5101",
    "OpsBaseUrl": "http://localhost:5103"
  },
  "ConnectorHost": {
    "ConnectorHostId": "connector-host-001",
    "ConnectorSecret": "local-connector-secret",
    "OrganizationId": "org-001",
    "EnvironmentId": "env-dev"
  }
}
```

In `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`, replace fallbacks:

```csharp
builder.Configuration["Platform:AppHubBaseUrl"] ?? "http://localhost:5101"
builder.Configuration["Platform:OpsBaseUrl"] ?? "http://localhost:5103"
```

- [ ] **Step 5: Verify no current runtime config still uses old local service ports**

Run:

```powershell
rg -n "localhost:(5073|5204|5283|5261|5173)|localhost:5104|localhost:5105" backend connector-hosts frontend infra README.md docs/architecture -g "!frontend/pnpm-lock.yaml"
```

Expected: no hits for `5073`, `5204`, `5283`, `5261`, or `5173`. Hits for `5104` and `5105` are allowed only when they mean FileStorage and Console respectively; Ops must no longer use `5105`, and IAM must no longer use `5104`.

- [ ] **Step 6: Build backend entrypoints**

Run:

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore
```

Expected: both builds pass.

- [ ] **Step 7: Commit**

Run:

```powershell
git add backend connector-hosts
git commit -m "chore: standardize local service ports"
```

---

### Task 4: Standardize AppHost, Console Port And MinIO Image

**Files:**
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `infra/docker-compose.dev.yml`
- Modify: `frontend/apps/console/package.json`
- Modify: `frontend/apps/console/vite.config.ts`

- [ ] **Step 1: Update AppHost MinIO image and fixed service endpoints**

In `infra/aspire/Nerv.IIP.AppHost/Program.cs`, replace:

```csharp
var minio = builder.AddContainer("minio", "minio/minio")
```

with:

```csharp
var minio = builder.AddContainer("minio", "pgsty/minio", "RELEASE.2026-04-17T00-00-00Z")
```

Then add fixed HTTP endpoints to the project resources:

```csharp
var apphub = builder.AddProject<Projects.Nerv_IIP_AppHub_Web>("apphub")
    .WithHttpEndpoint(port: 5101, name: "http")
```

```csharp
var iam = builder.AddProject<Projects.Nerv_IIP_Iam_Web>("iam")
    .WithHttpEndpoint(port: 5102, name: "http")
```

```csharp
var ops = builder.AddProject<Projects.Nerv_IIP_Ops_Web>("ops")
    .WithHttpEndpoint(port: 5103, name: "http")
```

```csharp
var fileStorage = builder.AddProject<Projects.Nerv_IIP_FileStorage_Web>("file-storage")
    .WithHttpEndpoint(port: 5104, name: "http")
```

```csharp
var gateway = builder.AddProject<Projects.Nerv_IIP_PlatformGateway_Web>("gateway")
    .WithHttpEndpoint(port: 5100, name: "http")
```

For the Console resource, add the endpoint after `AddViteApp`:

```csharp
builder.AddViteApp("console", "../../../frontend/apps/console")
    .WithHttpEndpoint(port: 5105, name: "http")
    .WithPnpm()
```

- [ ] **Step 2: Update Docker Compose MinIO image**

In `infra/docker-compose.dev.yml`, replace:

```yaml
image: minio/minio
```

with:

```yaml
image: pgsty/minio:RELEASE.2026-04-17T00-00-00Z
```

- [ ] **Step 3: Update Console dev port**

In `frontend/apps/console/package.json`, change the dev script to:

```json
"dev": "vp dev --host 127.0.0.1 --port 5105"
```

In `frontend/apps/console/vite.config.ts`, change:

```ts
server: {
  port: 5105,
  proxy: {
```

- [ ] **Step 4: Verify AppHost and frontend**

Run:

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
pnpm -C frontend --filter @nerv-iip/console typecheck
```

Expected: both pass.

- [ ] **Step 5: Verify image and port strings**

Run:

```powershell
rg -n "minio/minio|127.0.0.1 --port 5173|port: 5173" infra frontend
rg -n "pgsty/minio:RELEASE.2026-04-17T00-00-00Z|WithHttpEndpoint\\(port: 5105|--port 5105|port: 5105" infra frontend
```

Expected: first command has no output. Second command shows the Compose image, AppHost MinIO image or endpoint changes, and Console port updates.

- [ ] **Step 6: Commit**

Run:

```powershell
git add infra frontend/apps/console
git commit -m "chore: align apphost ports and minio image"
```

---

### Task 5: Update Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/architecture/deployment-baseline.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Add README daily development section**

Add this section after the "技术基线" list and before "仓库规划":

````markdown
## 日常开发启动

主平台本地联调入口是仓库根目录的轻量 CLI：

```powershell
.\nerv.ps1 dev
```

该命令通过 `scripts/dev.ps1` 启动平台级 Aspire AppHost。Aspire 是完整本地拓扑入口，会编排 PlatformGateway、AppHub、IAM、Ops、FileStorage、Connector Host、Console 和本地依赖服务。

只需要启动 PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector 等依赖服务时，使用：

```powershell
.\nerv.ps1 dev -InfraOnly
```

查看本地端口矩阵：

```powershell
.\nerv.ps1 ports
```

平台 HTTP 服务固定为 `5100-5105`：Gateway `5100`、AppHub `5101`、IAM `5102`、Ops `5103`、FileStorage `5104`、Console `5105`。Console 避开 Vite 默认 `5173`，降低与其他前端项目冲突的概率。
````

- [ ] **Step 2: Update deployment baseline current stage**

In `docs/architecture/deployment-baseline.md`, add this current-stage note near the existing "当前阶段" list:

```markdown
8. 本地开发统一入口收敛为根目录 `.\nerv.ps1 dev`，该命令只作为薄 CLI 包装，真实启动逻辑仍位于受脚本治理约束的 `scripts/dev.ps1`。完整平台启动走 Aspire AppHost；`.\nerv.ps1 dev -InfraOnly` 只启动 `infra/docker-compose.dev.yml` 中的依赖服务。
9. 本地 MinIO 容器镜像使用 `pgsty/minio:RELEASE.2026-04-17T00-00-00Z`，避免继续依赖停止更新的 `minio/minio` Docker image line；FileStorage 仍通过对象存储 provider 抽象与 MinIO 或等价 S3-compatible backend 交互。
```

- [ ] **Step 3: Update implementation readiness**

In `docs/architecture/implementation-readiness.md`, update the local execution/readiness section with:

```markdown
- 根目录 `.\nerv.ps1 dev` 已成为主平台本地联调入口；`.\nerv.ps1 ports` 输出 canonical local port matrix。
- 平台 HTTP 服务端口收敛到 `5100-5105`，其中 Console 使用 `5105` 而不是 Vite 默认 `5173`。
- 本地 MinIO runtime image 使用 `pgsty/minio:RELEASE.2026-04-17T00-00-00Z`。
```

- [ ] **Step 4: Verify documentation no longer gives old current guidance**

Run:

```powershell
rg -n "localhost:(5073|5204|5283|5261|5173)|minio/minio" README.md docs/architecture infra frontend backend connector-hosts -g "!frontend/pnpm-lock.yaml"
```

Expected: no hits in current guidance files. Historical `docs/superpowers/plans` and old specs are not part of this check.

- [ ] **Step 5: Commit**

Run:

```powershell
git add README.md docs/architecture/deployment-baseline.md docs/architecture/implementation-readiness.md
git commit -m "docs: document unified development startup"
```

---

### Task 6: Final Verification

**Files:**
- Modify: none unless verification exposes a defect.

- [ ] **Step 1: Run script command tests**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/check-script-governance.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

Expected:

```text
Development entrypoint smoke tests passed.
Script governance fixture tests passed.
Script governance check passed.
```

- [ ] **Step 2: Run build/typecheck verification**

Run:

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
dotnet build backend/Nerv.IIP.sln --no-restore
dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore
pnpm -C frontend --filter @nerv-iip/console typecheck
```

Expected: all commands exit 0.

- [ ] **Step 3: Verify root command output**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 help
pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 ports
pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 dev -Help
```

Expected: help mentions `dev`, `ports`, and `help`; ports output includes `5100-5105`; dev help mentions `Aspire AppHost`, `-NoBuild`, `-InfraOnly`, and `-OpenDashboard`.

- [ ] **Step 4: Optional short AppHost smoke test**

Run this only when Docker Desktop is running and the developer is ready to stop the process manually:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 dev
```

Expected: Aspire AppHost starts and prints dashboard/resource output. Stop it with `Ctrl+C` after resources begin starting. Do not leave the command running at final handoff.

- [ ] **Step 5: Final grep checks**

Run:

```powershell
rg -n "minio/minio|localhost:(5073|5204|5283|5261|5173)" README.md docs/architecture infra frontend backend connector-hosts -g "!frontend/pnpm-lock.yaml"
rg -n "5100 PlatformGateway|5101 AppHub|5102 IAM|5103 Ops|5104 FileStorage|5105 Console" nerv.ps1 README.md docs/architecture
```

Expected: first command has no output. Second command shows the port matrix in `nerv.ps1` and README/docs.

- [ ] **Step 6: Confirm no final verification patch is pending**

Run:

```powershell
git status --short
```

Expected: no output. If this command shows modified files, inspect those files, run the relevant verification command again, and create a normal fix commit with the exact files changed.
