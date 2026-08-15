# 发布级持久化基础实施计划

> **面向智能体执行者：** 必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 使用基于迁移的 AppHub/Ops 验证和显式本地自动迁移护栏，替换第四阶段 PostgreSQL 的 `EnsureCreated()` 捷径。

**架构：** AppHub 和 Ops 将 EF Core 迁移及迁移运行器保留在各自的 Infrastructure 项目中。PostgreSQL 测试和脚本通过 `Database.MigrateAsync` 应用迁移；Web 启动仅在 `Persistence:AutoMigrate=true` 时自动迁移。不包含前端功能工作；仅当后端 OpenAPI 发生变化时，才允许更新生成的 API 客户端并运行质量门禁。

**技术栈：** .NET 10、EF Core 10.0.8、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1、netcorepal 3.3.0、xUnit、PowerShell、Docker Compose，以及用于可选前端契约门禁的 pnpm 10.13.1。

---

## 完成记录

2026-05-17 本阶段迁移发布底座门禁已通过：

> 历史说明：下面未勾选的任务清单作为原始执行计划保留。该阶段已经完成；状态以“完成记录”和 git 历史为事实来源。

AppHub `IGuidStronglyTypedId` 主键已按 NetCorePal 约定改为 EF `UseGuidVersion7ValueGenerator()` 生成；领域构造函数不再手动调用 `Guid.CreateVersion7()`。新增 `Postgres_store_generates_guid_strong_ids_on_add` 覆盖“构造时无 ID，保存时由 EF 生成 ID”的约束。

建表、注释、schema catalog 和可视化元数据的长期规范已补入：

- `docs/architecture/database-schema-conventions.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/database-release-runbook.md`
- `docs/architecture/observability-baseline.md`

```powershell
pwsh scripts/verify-fifth-slice-persistence-foundation.ps1
```

最终输出：

```text
Fifth slice release-grade persistence foundation verified.
```

同时复跑第四阶段真实基础设施门禁已通过：

```powershell
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

最终输出：

```text
Fourth vertical slice real infrastructure verified.
```

## 边界

1. 本计划不得实施 IAM、FileStorage、Notification、CAP 业务 outbox、审批 UI 或控制台页面。
2. 不得添加前端视觉组件、样式令牌或重新设计应用外壳。设计系统需要后续规范。
3. 本计划完成后，不得在 PostgreSQL 验证或 Web 启动中使用 `EnsureCreated()`。
4. 不得将迁移移动到 Web 项目中。服务的 Infrastructure 项目拥有其 schema。
5. 不得在类似生产环境的服务启动中静默自动迁移。仅对本地/开发验证入口使用 `Persistence:AutoMigrate=true`。

## 文件结构图

```text
dotnet-tools.json
backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/
  AppHubDatabaseMigrationRunner.cs
  Migrations/
backend/services/AppHub/src/Nerv.IIP.AppHub.Web/
  Program.cs
backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/
  AppHubPostgresProfileTests.cs
backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/
  OpsDatabaseMigrationRunner.cs
  Migrations/
backend/services/Ops/src/Nerv.IIP.Ops.Web/
  Program.cs
backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/
  OpsPostgresProfileTests.cs
scripts/
  verify-fifth-slice-persistence-foundation.ps1
docs/architecture/
  frontend-design-system-planning.md
```

## 任务 1：添加可重复的迁移工具链

**文件：**

- 创建：`dotnet-tools.json`
- 修改：`README.md`
- 修改：`docs/architecture/implementation-readiness.md`

- [ ] **步骤 1：创建本地 .NET 工具清单**

运行：

```powershell
dotnet new tool-manifest
```

预期结果：仓库根目录存在 `dotnet-tools.json`。

- [ ] **步骤 2：将 dotnet-ef 安装为本地工具**

运行：

```powershell
dotnet tool install dotnet-ef --version 10.0.8
```

预期结果：清单包含 `dotnet-ef`，其版本为 `10.0.8`。

- [ ] **步骤 3：验证工具可以运行**

运行：

```powershell
dotnet tool run dotnet-ef --version
```

预期结果：输出包含 `10.0.8`。

- [ ] **步骤 4：记录还原用法**

在实施就绪状态文档中添加：

```markdown
第五阶段起仓库包含本地 `dotnet-tools.json`，用于固定 `dotnet-ef` 版本。首次生成或检查迁移前运行 `dotnet tool restore`，再使用 `dotnet tool run dotnet-ef ...`，避免依赖开发者全局工具。
```

## 任务 2：添加 AppHub 迁移运行器和初始迁移

**文件：**

- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/AppHubDatabaseMigrationRunner.cs`
- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/*`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- 修改：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubPostgresProfileTests.cs`

- [ ] **步骤 1：编写预期失败的 AppHub 迁移测试**

修改 PostgreSQL profile 测试，使初始化调用迁移运行器而不是 `EnsureCreatedAsync()`：

```csharp
await db.Database.EnsureDeletedAsync();
var migrationRunner = scope.ServiceProvider.GetRequiredService<AppHubDatabaseMigrationRunner>();
await migrationRunner.MigrateAsync();
```

在测试服务集合中注册运行器：

```csharp
services.AddScoped<AppHubDatabaseMigrationRunner>();
```

运行：

```powershell
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=15432;Database=nerv_iip_apphub_migration_red;Username=nerv;Password=nerv"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubPostgresProfileTests
```

预期结果：失败，因为 `AppHubDatabaseMigrationRunner` 不存在。

- [ ] **步骤 2：添加运行器**

创建 `AppHubDatabaseMigrationRunner.cs`：

```csharp
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure;

public sealed class AppHubDatabaseMigrationRunner(ApplicationDbContext dbContext)
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Database.MigrateAsync(cancellationToken);
    }
}
```

- [ ] **步骤 3：在 Web 启动中注册自动迁移并加设护栏**

在 `Program.cs` 中，于注册持久化之后注册运行器：

```csharp
if (usePostgreSql)
{
    builder.Services.AddScoped<AppHubDatabaseMigrationRunner>();
}
```

将当前 `EnsureCreated()` 块替换为：

```csharp
if (usePostgreSql && builder.Configuration.GetValue<bool>("Persistence:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider
        .GetRequiredService<AppHubDatabaseMigrationRunner>()
        .MigrateAsync();
}
```

顶级语句可以使用 `await`；无需显式 `Main` 方法。

- [ ] **步骤 4：生成 AppHub 初始迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__AppHubDb = "Host=localhost;Port=15432;Database=nerv_iip_apphub_migration_design;Username=nerv;Password=nerv"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialCreate --project backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj --startup-project backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj --context Nerv.IIP.AppHub.Infrastructure.ApplicationDbContext --output-dir Migrations
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__AppHubDb -ErrorAction SilentlyContinue
```

预期结果：AppHub Infrastructure 下出现 `Migrations` 文件夹。

- [ ] **步骤 5：验证 AppHub 迁移路径通过**

运行：

```powershell
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=15432;Database=nerv_iip_apphub_migration_green;Username=nerv;Password=nerv"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubPostgresProfileTests
Remove-Item Env:\NERV_IIP_TEST_POSTGRES -ErrorAction SilentlyContinue
```

预期结果：通过。

## 任务 3：添加 Ops 迁移运行器和初始迁移

**文件：**

- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/OpsDatabaseMigrationRunner.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/*`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- 修改：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsPostgresProfileTests.cs`

- [ ] **步骤 1：编写预期失败的 Ops 迁移测试**

修改 PostgreSQL profile 测试初始化：

```csharp
await db.Database.EnsureDeletedAsync();
var migrationRunner = scope.ServiceProvider.GetRequiredService<OpsDatabaseMigrationRunner>();
await migrationRunner.MigrateAsync();
```

运行：

```powershell
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=15432;Database=nerv_iip_ops_migration_red;Username=nerv;Password=nerv"
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsPostgresProfileTests
```

预期结果：失败，因为 `OpsDatabaseMigrationRunner` 不存在或未注册。

- [ ] **步骤 2：添加运行器**

创建 `OpsDatabaseMigrationRunner.cs`：

```csharp
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Ops.Infrastructure;

public sealed class OpsDatabaseMigrationRunner(ApplicationDbContext dbContext)
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.Database.MigrateAsync(cancellationToken);
    }
}
```

- [ ] **步骤 3：在 Web 启动中注册自动迁移并加设护栏**

在 `Program.cs` 中以 PostgreSQL 模式注册运行器：

```csharp
if (usePostgreSql)
{
    builder.Services.AddScoped<OpsDatabaseMigrationRunner>();
}
```

将当前 `EnsureCreated()` 块替换为：

```csharp
if (usePostgreSql && builder.Configuration.GetValue<bool>("Persistence:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider
        .GetRequiredService<OpsDatabaseMigrationRunner>()
        .MigrateAsync();
}
```

- [ ] **步骤 4：生成 Ops 初始迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__OpsDb = "Host=localhost;Port=15432;Database=nerv_iip_ops_migration_design;Username=nerv;Password=nerv"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialCreate --project backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj --startup-project backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj --context Nerv.IIP.Ops.Infrastructure.ApplicationDbContext --output-dir Migrations
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__OpsDb -ErrorAction SilentlyContinue
```

预期结果：Ops Infrastructure 下出现 `Migrations` 文件夹。

- [ ] **步骤 5：验证 Ops 迁移路径通过**

运行：

```powershell
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=15432;Database=nerv_iip_ops_migration_green;Username=nerv;Password=nerv"
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsPostgresProfileTests
Remove-Item Env:\NERV_IIP_TEST_POSTGRES -ErrorAction SilentlyContinue
```

预期结果：通过。

## 任务 4：添加第五阶段验证脚本

**文件：**

- 创建：`scripts/verify-fifth-slice-persistence-foundation.ps1`
- 修改：`scripts/verify-fourth-slice-real-infra.ps1`
- 修改：`README.md`
- 修改：`docs/architecture/implementation-readiness.md`

- [ ] **步骤 1：创建第五阶段验证脚本**

创建 `scripts/verify-fifth-slice-persistence-foundation.ps1`：

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-TcpPort {
  param([string]$HostName, [int]$Port, [int]$TimeoutSeconds = 90)
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  do {
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
      $connectTask = $client.ConnectAsync($HostName, $Port)
      if ($connectTask.Wait(1000) -and $client.Connected) { return }
    }
    catch { Start-Sleep -Milliseconds 500 }
    finally { $client.Dispose() }
    Start-Sleep -Milliseconds 500
  } while ((Get-Date) -lt $deadline)
  throw "TCP port $HostName`:$Port did not become available within $TimeoutSeconds seconds."
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

$composeFile = Join-Path $root "infra/docker-compose.dev.yml"
$postgresPort = if ([string]::IsNullOrWhiteSpace($env:NERV_IIP_POSTGRES_PORT)) { "15432" } else { $env:NERV_IIP_POSTGRES_PORT }
$env:NERV_IIP_POSTGRES_PORT = $postgresPort

docker compose -f $composeFile up -d postgres redis rabbitmq
Wait-TcpPort -HostName "localhost" -Port ([int]$postgresPort)
Wait-TcpPort -HostName "localhost" -Port 6379 -TimeoutSeconds 60
Wait-TcpPort -HostName "localhost" -Port 5672 -TimeoutSeconds 60

dotnet tool restore

$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_apphub_migration_verify;Username=nerv;Password=nerv"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubPostgresProfileTests

$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=$postgresPort;Database=nerv_iip_ops_migration_verify;Username=nerv;Password=nerv"
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsPostgresProfileTests
Remove-Item Env:\NERV_IIP_TEST_POSTGRES -ErrorAction SilentlyContinue

dotnet test backend/Nerv.IIP.sln
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln

Write-Host "Fifth slice release-grade persistence foundation verified."
```

- [ ] **步骤 2：保持第四阶段脚本的迁移安全性**

确保 AppHub/Ops 测试迁移到 migration 后，`scripts/verify-fourth-slice-real-infra.ps1` 仍然通过。不得在任何位置重新引入 `EnsureCreated()`。

- [ ] **步骤 3：运行第五阶段脚本**

运行：

```powershell
pwsh scripts/verify-fifth-slice-persistence-foundation.ps1
```

预期最后一行：

```text
Fifth slice release-grade persistence foundation verified.
```

## 任务 5：记录前端延后与设计系统规划

**文件：**

- 创建：`docs/architecture/frontend-design-system-planning.md`
- 修改：`README.md`
- 修改：`docs/architecture/api-contract-and-codegen.md`
- 修改：`docs/architecture/frontend-structure.md`
- 修改：`docs/architecture/implementation-readiness.md`

- [ ] **步骤 1：添加设计系统规划说明**

创建一份文档，说明：

```markdown
# Frontend Design System Planning

The console has a working third-stage skeleton, but the visual design system is not selected. Backend SDK, migrations and deployment verification must not wait on UI work, and UI work must not start by accident while backend foundations are still settling.

Before adding new console pages or restyling packages/ui, create a separate Superpowers spec that decides component library, token model, icon policy, density, accessibility baseline, theme strategy and migration path from the current local primitives.
```

- [ ] **步骤 2：更新 API 契约规则**

添加：

```markdown
Backend SDK and OpenAPI changes may regenerate `frontend/packages/api-client`, but this does not authorize new console views. If a backend contract is not needed by the current console, keep the generated client change mechanical and covered by generated contract tests.
```

- [ ] **步骤 3：更新就绪状态文档和 README**

将第五阶段计划和验证命令添加到现有计划/状态清单中。说明在设计系统规范形成之前，前端功能工作会有意延后。

## 任务 6：最终验证

**文件：**

- 除非前一任务发现缺失的测试或文档，否则不新增文件。

- [ ] **步骤 1：运行后端和 Connector Host 测试**

运行：

```powershell
dotnet test backend/Nerv.IIP.sln
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
```

预期结果：两者都以 `0` 退出。

- [ ] **步骤 2：仅当前端文件发生变化时运行前端质量门禁**

如果任何 `frontend/` 文件发生变化，运行：

```powershell
pnpm -C frontend check
pnpm -C frontend fmt
pnpm -C frontend lint
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

预期结果：全部以 `0` 退出。

- [ ] **步骤 3：运行仓库空白检查**

运行：

```powershell
git diff --check
```

预期结果：以 `0` 退出。

## 执行顺序

1. 首先执行任务 1，因为迁移生成必须使用已锁定版本的本地工具。
2. 只有任务 2 和任务 3 的写入集合保持互不相交时，才能将它们分配给不同执行者。
3. 任务 4 依赖任务 2 和任务 3。
4. 任务 5 只涉及文档，因此可以与任务 2 或任务 3 并行运行。
5. 最后运行任务 6。

## 自检

规范覆盖：

1. 任务 2、3 和 4 覆盖使用 migration 替换 `EnsureCreated()`。
2. 任务 1 覆盖工具链可重复性。
3. 任务 5 覆盖前端延后和设计系统规划。
4. 任务 6 覆盖验证。

占位符扫描：

1. 不保留 `TBD` 或 `TODO` 标记。
2. 命令使用具体路径和预期输出。
3. 唯一可选分支是前端门禁，并且与前端文件是否发生变化绑定。

类型一致性：

1. AppHub 运行器始终命名为 `AppHubDatabaseMigrationRunner`。
2. Ops 运行器始终命名为 `OpsDatabaseMigrationRunner`。
3. 迁移命令的上下文名称与当前 Infrastructure 的 `ApplicationDbContext` 命名空间一致。
