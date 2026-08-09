# 主平台开发入口实施计划

> **面向智能代理工作者：** 必须使用子技能：采用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：** 添加仓库根级 `.\nerv.ps1` 开发 CLI，通过 Aspire 启动整个本地平台，公开规范端口矩阵，将平台 HTTP 端口迁移到 `5100-5105`，并把本地 MinIO 容器切换到 `pgsty/minio`。

**架构：** 根 CLI 保持轻量，真实进程执行放入 `scripts/` 下受治理的脚本。Aspire 继续作为完整平台启动的拓扑来源；Docker Compose 继续只为 `-InfraOnly` 和验证脚本提供依赖服务。端口变更必须在启动设置、后备 URL、AppHost 端点、Vite 配置和文档中保持一致。

**技术栈：** PowerShell 7、.NET 10、Aspire 13.3.3、Docker Compose、pnpm 11.1.2、Vite 8、Vue 3。

---

## 文件结构

新增：

1. `nerv.ps1` - `dev`、`ports` 和 `help` 的根命令分派器。
2. `scripts/dev.ps1` - 调用 `Invoke-DotNet` 或 `Invoke-DockerCompose` 的受治理开发启动脚本。
3. `scripts/tests/dev-entrypoint.Tests.ps1` - 针对根命令界面及其输出的 PowerShell 冒烟测试。

修改：

1. `infra/aspire/Nerv.IIP.AppHost/Program.cs` - 固定本地端口和 `pgsty/minio` 镜像标签。
2. `infra/docker-compose.dev.yml` - `pgsty/minio` 镜像标签。
3. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Properties/launchSettings.json` - Gateway HTTP 端口 `5100`。
4. `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Properties/launchSettings.json` - AppHub HTTP 端口 `5101`。
5. `backend/services/Iam/src/Nerv.IIP.Iam.Web/Properties/launchSettings.json` - IAM HTTP 端口 `5102`。
6. `backend/services/Ops/src/Nerv.IIP.Ops.Web/Properties/launchSettings.json` - Ops HTTP 端口 `5103`。
7. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Properties/launchSettings.json` - FileStorage HTTP 端口 `5104`。
8. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json` - AppHub/Ops 本地服务 URL。
9. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs` - AppHub/Ops/IAM 后备 URL。
10. `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs` - IAM 后备 URL。
11. `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json` - AppHub/Ops 本地服务 URL。
12. `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs` - AppHub/Ops 后备 URL。
13. `frontend/apps/console/package.json` - Console 开发脚本端口 `5105`。
14. `frontend/apps/console/vite.config.ts` - Vite 开发服务器端口 `5105`。
15. `frontend/packages/api-client/src/transport/base-url.ts` - 保持 Gateway 默认端口 `5100`，并验证其符合端口矩阵。
16. `README.md` - 添加日常开发入口。
17. `docs/architecture/deployment-baseline.md` - 记录基于受治理脚本的根 CLI 和本地 MinIO 镜像基线。
18. `docs/architecture/implementation-readiness.md` - 入口落地后更新当前就绪状态说明。

---

### 任务 1：添加根 CLI 和测试

**文件：**
- 新增：`nerv.ps1`
- 新增：`scripts/tests/dev-entrypoint.Tests.ps1`
- 修改：无

- [ ] **步骤 1：编写会失败的根命令冒烟测试**

创建 `scripts/tests/dev-entrypoint.Tests.ps1`：

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

- [ ] **步骤 2：运行冒烟测试以验证其会失败**

运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
```

预期：失败，因为 `nerv.ps1` 不存在。

- [ ] **步骤 3：添加根 CLI 包装器**

创建 `nerv.ps1`：

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

- [ ] **步骤 4：运行冒烟测试以验证其通过**

运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
```

预期：通过，并输出 `Development entrypoint smoke tests passed.`。

- [ ] **步骤 5：提交**

运行：

```powershell
git add nerv.ps1 scripts/tests/dev-entrypoint.Tests.ps1
git commit -m "feat: add root development entrypoint"
```

---

### 任务 2：添加受治理的开发启动脚本

**文件：**
- 新增：`scripts/dev.ps1`
- 修改：`scripts/tests/dev-entrypoint.Tests.ps1`

- [ ] **步骤 1：扩展 `dev -Help` 冒烟测试且不启动服务**

在最后的 `Write-Host` 之前，将以下代码块追加到 `scripts/tests/dev-entrypoint.Tests.ps1`：

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

- [ ] **步骤 2：运行冒烟测试以验证其会失败**

运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
```

预期：失败，因为 `scripts/dev.ps1` 不存在。

- [ ] **步骤 3：添加 `scripts/dev.ps1`**

创建 `scripts/dev.ps1`：

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

- [ ] **步骤 4：添加交互式原生命令辅助函数**

在 `scripts/lib/ScriptAutomation.ps1` 的 `Invoke-DotNet` 之后添加以下函数：

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

这样既能将直接进程执行保留在共享辅助工具内，也允许 `.\nerv.ps1 dev` 把包括仪表板 URL 在内的 Aspire 输出流式传到当前终端。

- [ ] **步骤 5：运行冒烟测试和治理测试**

运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

预期：

```text
Development entrypoint smoke tests passed.
Script governance check passed.
```

- [ ] **步骤 6：提交**

运行：

```powershell
git add scripts/dev.ps1 scripts/tests/dev-entrypoint.Tests.ps1 scripts/lib/ScriptAutomation.ps1
git commit -m "feat: add governed dev startup script"
```

---

### 任务 3：统一后端与 Connector 端口

**文件：**
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Properties/launchSettings.json`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Properties/launchSettings.json`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Properties/launchSettings.json`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Properties/launchSettings.json`
- 修改：`backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Properties/launchSettings.json`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json`
- 修改：`backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- 修改：`connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`
- 修改：`connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs`

- [ ] **步骤 1：更新启动设置中的 HTTP 端口**

严格应用以下端口变更：

```text
PlatformGateway: http://localhost:5073 -> http://localhost:5100
AppHub:          http://localhost:5204 -> http://localhost:5101
IAM:             http://localhost:5283 -> http://localhost:5102
Ops:             http://localhost:5105 -> http://localhost:5103
FileStorage:     http://localhost:5261 -> http://localhost:5104
```

对每个 `https` 配置档，保留现有 HTTPS URL，只替换分号后的 HTTP 部分。

- [ ] **步骤 2：更新 Gateway 开发服务 URL**

将 `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json` 改为：

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

- [ ] **步骤 3：更新后端后备 URL**

在 `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Program.cs` 中替换后备地址：

```csharp
builder.Configuration["AppHub:BaseUrl"] ?? "http://localhost:5101"
builder.Configuration["Ops:BaseUrl"] ?? "http://localhost:5103"
builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102"
```

在 `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs` 中替换 IAM 后备地址：

```csharp
builder.Configuration["Iam:BaseUrl"] ?? "http://localhost:5102"
```

- [ ] **步骤 4：更新 Connector Host 开发 URL**

将 `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json` 改为：

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

在 `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs` 中替换后备地址：

```csharp
builder.Configuration["Platform:AppHubBaseUrl"] ?? "http://localhost:5101"
builder.Configuration["Platform:OpsBaseUrl"] ?? "http://localhost:5103"
```

- [ ] **步骤 5：验证当前运行时配置均不再使用旧本地服务端口**

运行：

```powershell
rg -n "localhost:(5073|5204|5283|5261|5173)|localhost:5104|localhost:5105" backend connector-hosts frontend infra README.md docs/architecture -g "!frontend/pnpm-lock.yaml"
```

预期：`5073`、`5204`、`5283`、`5261` 或 `5173` 均无匹配。`5104` 和 `5105` 仅在分别表示 FileStorage 与 Console 时允许匹配；Ops 不得继续使用 `5105`，IAM 不得继续使用 `5104`。

- [ ] **步骤 6：构建后端入口**

运行：

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore
```

预期：两项构建均通过。

- [ ] **步骤 7：提交**

运行：

```powershell
git add backend connector-hosts
git commit -m "chore: standardize local service ports"
```

---

### 任务 4：统一 AppHost、Console 端口与 MinIO 镜像

**文件：**
- 修改：`infra/aspire/Nerv.IIP.AppHost/Program.cs`
- 修改：`infra/docker-compose.dev.yml`
- 修改：`frontend/apps/console/package.json`
- 修改：`frontend/apps/console/vite.config.ts`

- [ ] **步骤 1：更新 AppHost MinIO 镜像和固定服务端点**

在 `infra/aspire/Nerv.IIP.AppHost/Program.cs` 中替换：

```csharp
var minio = builder.AddContainer("minio", "minio/minio")
```

为：

```csharp
var minio = builder.AddContainer("minio", "pgsty/minio", "RELEASE.2026-04-17T00-00-00Z")
```

然后为项目资源添加固定 HTTP 端点：

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

对于 Console 资源，在 `AddViteApp` 后添加端点：

```csharp
builder.AddViteApp("console", "../../../frontend/apps/console")
    .WithHttpEndpoint(port: 5105, name: "http")
    .WithPnpm()
```

- [ ] **步骤 2：更新 Docker Compose MinIO 镜像**

在 `infra/docker-compose.dev.yml` 中替换：

```yaml
image: minio/minio
```

为：

```yaml
image: pgsty/minio:RELEASE.2026-04-17T00-00-00Z
```

- [ ] **步骤 3：更新 Console 开发端口**

在 `frontend/apps/console/package.json` 中，将开发脚本改为：

```json
"dev": "vp dev --host 127.0.0.1 --port 5105"
```

在 `frontend/apps/console/vite.config.ts` 中改为：

```ts
server: {
  port: 5105,
  proxy: {
```

- [ ] **步骤 4：验证 AppHost 和前端**

运行：

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
pnpm -C frontend --filter @nerv-iip/console typecheck
```

预期：两项均通过。

- [ ] **步骤 5：验证镜像和端口字符串**

运行：

```powershell
rg -n "minio/minio|127.0.0.1 --port 5173|port: 5173" infra frontend
rg -n "pgsty/minio:RELEASE.2026-04-17T00-00-00Z|WithHttpEndpoint\\(port: 5105|--port 5105|port: 5105" infra frontend
```

预期：第一条命令没有输出。第二条命令显示 Compose 镜像、AppHost MinIO 镜像或端点变更，以及 Console 端口更新。

- [ ] **步骤 6：提交**

运行：

```powershell
git add infra frontend/apps/console
git commit -m "chore: align apphost ports and minio image"
```

---

### 任务 5：更新文档

**文件：**
- 修改：`README.md`
- 修改：`docs/architecture/deployment-baseline.md`
- 修改：`docs/architecture/implementation-readiness.md`

- [ ] **步骤 1：添加 README 日常开发章节**

在“技术基线”列表之后、“仓库规划”之前添加以下章节：

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

- [ ] **步骤 2：更新部署基线的当前阶段**

在 `docs/architecture/deployment-baseline.md` 现有“当前阶段”列表附近添加以下当前阶段说明：

```markdown
8. 本地开发统一入口收敛为根目录 `.\nerv.ps1 dev`，该命令只作为薄 CLI 包装，真实启动逻辑仍位于受脚本治理约束的 `scripts/dev.ps1`。完整平台启动走 Aspire AppHost；`.\nerv.ps1 dev -InfraOnly` 只启动 `infra/docker-compose.dev.yml` 中的依赖服务。
9. 本地 MinIO 容器镜像使用 `pgsty/minio:RELEASE.2026-04-17T00-00-00Z`，避免继续依赖停止更新的 `minio/minio` Docker image line；FileStorage 仍通过对象存储 provider 抽象与 MinIO 或等价 S3-compatible backend 交互。
```

- [ ] **步骤 3：更新实施就绪状态**

在 `docs/architecture/implementation-readiness.md` 中，用以下内容更新本地执行/就绪状态章节：

```markdown
- 根目录 `.\nerv.ps1 dev` 已成为主平台本地联调入口；`.\nerv.ps1 ports` 输出 canonical local port matrix。
- 平台 HTTP 服务端口收敛到 `5100-5105`，其中 Console 使用 `5105` 而不是 Vite 默认 `5173`。
- 本地 MinIO runtime image 使用 `pgsty/minio:RELEASE.2026-04-17T00-00-00Z`。
```

- [ ] **步骤 4：验证文档不再提供旧的当前指引**

运行：

```powershell
rg -n "localhost:(5073|5204|5283|5261|5173)|minio/minio" README.md docs/architecture infra frontend backend connector-hosts -g "!frontend/pnpm-lock.yaml"
```

预期：当前指引文件中没有匹配。历史 `docs/superpowers/plans` 和旧规格不在本次检查范围内。

- [ ] **步骤 5：提交**

运行：

```powershell
git add README.md docs/architecture/deployment-baseline.md docs/architecture/implementation-readiness.md
git commit -m "docs: document unified development startup"
```

---

### 任务 6：最终验证

**文件：**
- 修改：无；除非验证暴露缺陷。

- [ ] **步骤 1：运行脚本命令测试**

运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/dev-entrypoint.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/tests/check-script-governance.Tests.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1
```

预期：

```text
Development entrypoint smoke tests passed.
Script governance fixture tests passed.
Script governance check passed.
```

- [ ] **步骤 2：运行构建/类型检查验证**

运行：

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
dotnet build backend/Nerv.IIP.sln --no-restore
dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore
pnpm -C frontend --filter @nerv-iip/console typecheck
```

预期：所有命令均以退出码 0 结束。

- [ ] **步骤 3：验证根命令输出**

运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 help
pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 ports
pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 dev -Help
```

预期：帮助文本提及 `dev`、`ports` 和 `help`；端口输出包含 `5100-5105`；开发帮助提及 `Aspire AppHost`、`-NoBuild`、`-InfraOnly` 和 `-OpenDashboard`。

- [ ] **步骤 4：可选的短时 AppHost 冒烟测试**

仅当 Docker Desktop 正在运行且开发人员准备好手动停止进程时，才运行：

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 dev
```

预期：Aspire AppHost 启动并打印仪表板/资源输出。资源开始启动后使用 `Ctrl+C` 停止。最终交接时不得让该命令继续运行。

- [ ] **步骤 5：最终 grep 检查**

运行：

```powershell
rg -n "minio/minio|localhost:(5073|5204|5283|5261|5173)" README.md docs/architecture infra frontend backend connector-hosts -g "!frontend/pnpm-lock.yaml"
rg -n "5100 PlatformGateway|5101 AppHub|5102 IAM|5103 Ops|5104 FileStorage|5105 Console" nerv.ps1 README.md docs/architecture
```

预期：第一条命令没有输出。第二条命令显示 `nerv.ps1` 和 README/文档中的端口矩阵。

- [ ] **步骤 6：确认没有待处理的最终验证补丁**

运行：

```powershell
git status --short
```

预期：没有输出。如果该命令显示修改文件，请检查这些文件，重新运行相关验证命令，并仅用实际变更的文件创建常规修复提交。
