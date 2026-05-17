# Release-Grade Persistence Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fourth-stage PostgreSQL `EnsureCreated()` shortcut with migration-based AppHub/Ops verification and explicit local auto-migration guardrails.

**Architecture:** AppHub and Ops keep EF Core migrations and migration runners in their own Infrastructure projects. PostgreSQL tests and scripts apply migrations through `Database.MigrateAsync`; Web startup auto-migrates only when `Persistence:AutoMigrate=true`. Frontend feature work is excluded, with only generated API client and quality gates allowed if backend OpenAPI changes.

**Tech Stack:** .NET 10, EF Core 10.0.8, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1, netcorepal 3.3.0, xUnit, PowerShell, Docker Compose, pnpm 10.13.1 for optional frontend contract gates.

---

## Completion Record

2026-05-17 本阶段迁移发布底座门禁已通过：

> Historical note: the unchecked task list below is preserved as the original execution plan. The stage is complete; use the Completion Record and git history as the source of truth for status.

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

## Boundaries

1. Do not implement IAM, FileStorage, Notification, CAP business outbox, approval UI, or console pages in this plan.
2. Do not add frontend visual components, style tokens or app shell redesign. The design system needs a later spec.
3. Do not use `EnsureCreated()` in PostgreSQL verification or Web startup after this plan.
4. Do not move migrations into Web projects. Service Infrastructure projects own their schema.
5. Do not silently auto-migrate production-like service startup. Use `Persistence:AutoMigrate=true` only for local/dev verification entrypoints.

## File Structure Map

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

## Task 1: Add Repeatable Migration Tooling

**Files:**

- Create: `dotnet-tools.json`
- Modify: `README.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Create a local .NET tool manifest**

Run:

```powershell
dotnet new tool-manifest
```

Expected: `dotnet-tools.json` exists at the repository root.

- [ ] **Step 2: Install dotnet-ef as a local tool**

Run:

```powershell
dotnet tool install dotnet-ef --version 10.0.8
```

Expected: manifest contains `dotnet-ef` version `10.0.8`.

- [ ] **Step 3: Verify the tool can run**

Run:

```powershell
dotnet tool run dotnet-ef --version
```

Expected: output includes `10.0.8`.

- [ ] **Step 4: Document restore usage**

Add to implementation readiness:

```markdown
第五阶段起仓库包含本地 `dotnet-tools.json`，用于固定 `dotnet-ef` 版本。首次生成或检查迁移前运行 `dotnet tool restore`，再使用 `dotnet tool run dotnet-ef ...`，避免依赖开发者全局工具。
```

## Task 2: Add AppHub Migration Runner And Initial Migration

**Files:**

- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/AppHubDatabaseMigrationRunner.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/*`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Modify: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubPostgresProfileTests.cs`

- [ ] **Step 1: Write the failing AppHub migration test**

Modify the PostgreSQL profile test so setup calls a migration runner instead of `EnsureCreatedAsync()`:

```csharp
await db.Database.EnsureDeletedAsync();
var migrationRunner = scope.ServiceProvider.GetRequiredService<AppHubDatabaseMigrationRunner>();
await migrationRunner.MigrateAsync();
```

Register the runner in the test service collection:

```csharp
services.AddScoped<AppHubDatabaseMigrationRunner>();
```

Run:

```powershell
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=15432;Database=nerv_iip_apphub_migration_red;Username=nerv;Password=nerv"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubPostgresProfileTests
```

Expected: FAIL because `AppHubDatabaseMigrationRunner` does not exist.

- [ ] **Step 2: Add the runner**

Create `AppHubDatabaseMigrationRunner.cs`:

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

- [ ] **Step 3: Register and guard auto-migration in Web startup**

In `Program.cs`, register the runner after persistence registration:

```csharp
if (usePostgreSql)
{
    builder.Services.AddScoped<AppHubDatabaseMigrationRunner>();
}
```

Replace the current `EnsureCreated()` block with:

```csharp
if (usePostgreSql && builder.Configuration.GetValue<bool>("Persistence:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider
        .GetRequiredService<AppHubDatabaseMigrationRunner>()
        .MigrateAsync();
}
```

Top-level statements can use `await`; no explicit `Main` method is needed.

- [ ] **Step 4: Generate AppHub initial migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__AppHubDb = "Host=localhost;Port=15432;Database=nerv_iip_apphub_migration_design;Username=nerv;Password=nerv"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialCreate --project backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj --startup-project backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj --context Nerv.IIP.AppHub.Infrastructure.ApplicationDbContext --output-dir Migrations
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__AppHubDb -ErrorAction SilentlyContinue
```

Expected: a `Migrations` folder appears under AppHub Infrastructure.

- [ ] **Step 5: Verify AppHub migration path passes**

Run:

```powershell
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=15432;Database=nerv_iip_apphub_migration_green;Username=nerv;Password=nerv"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter FullyQualifiedName~AppHubPostgresProfileTests
Remove-Item Env:\NERV_IIP_TEST_POSTGRES -ErrorAction SilentlyContinue
```

Expected: PASS.

## Task 3: Add Ops Migration Runner And Initial Migration

**Files:**

- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/OpsDatabaseMigrationRunner.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Migrations/*`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- Modify: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsPostgresProfileTests.cs`

- [ ] **Step 1: Write the failing Ops migration test**

Modify the PostgreSQL profile test setup:

```csharp
await db.Database.EnsureDeletedAsync();
var migrationRunner = scope.ServiceProvider.GetRequiredService<OpsDatabaseMigrationRunner>();
await migrationRunner.MigrateAsync();
```

Run:

```powershell
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=15432;Database=nerv_iip_ops_migration_red;Username=nerv;Password=nerv"
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsPostgresProfileTests
```

Expected: FAIL because `OpsDatabaseMigrationRunner` does not exist or is not registered.

- [ ] **Step 2: Add the runner**

Create `OpsDatabaseMigrationRunner.cs`:

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

- [ ] **Step 3: Register and guard auto-migration in Web startup**

In `Program.cs`, register the runner in PostgreSQL mode:

```csharp
if (usePostgreSql)
{
    builder.Services.AddScoped<OpsDatabaseMigrationRunner>();
}
```

Replace the current `EnsureCreated()` block with:

```csharp
if (usePostgreSql && builder.Configuration.GetValue<bool>("Persistence:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider
        .GetRequiredService<OpsDatabaseMigrationRunner>()
        .MigrateAsync();
}
```

- [ ] **Step 4: Generate Ops initial migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__OpsDb = "Host=localhost;Port=15432;Database=nerv_iip_ops_migration_design;Username=nerv;Password=nerv"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialCreate --project backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj --startup-project backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj --context Nerv.IIP.Ops.Infrastructure.ApplicationDbContext --output-dir Migrations
Remove-Item Env:\Persistence__Provider -ErrorAction SilentlyContinue
Remove-Item Env:\ConnectionStrings__OpsDb -ErrorAction SilentlyContinue
```

Expected: a `Migrations` folder appears under Ops Infrastructure.

- [ ] **Step 5: Verify Ops migration path passes**

Run:

```powershell
$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=15432;Database=nerv_iip_ops_migration_green;Username=nerv;Password=nerv"
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter FullyQualifiedName~OpsPostgresProfileTests
Remove-Item Env:\NERV_IIP_TEST_POSTGRES -ErrorAction SilentlyContinue
```

Expected: PASS.

## Task 4: Add Fifth-Stage Verification Script

**Files:**

- Create: `scripts/verify-fifth-slice-persistence-foundation.ps1`
- Modify: `scripts/verify-fourth-slice-real-infra.ps1`
- Modify: `README.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Create the fifth-stage verification script**

Create `scripts/verify-fifth-slice-persistence-foundation.ps1`:

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

- [ ] **Step 2: Keep the fourth-stage script migration-safe**

Ensure `scripts/verify-fourth-slice-real-infra.ps1` still passes after the AppHub/Ops tests move to migrations. Do not reintroduce `EnsureCreated()` anywhere.

- [ ] **Step 3: Run the fifth-stage script**

Run:

```powershell
pwsh scripts/verify-fifth-slice-persistence-foundation.ps1
```

Expected final line:

```text
Fifth slice release-grade persistence foundation verified.
```

## Task 5: Document Frontend Deferral And Design System Planning

**Files:**

- Create: `docs/architecture/frontend-design-system-planning.md`
- Modify: `README.md`
- Modify: `docs/architecture/api-contract-and-codegen.md`
- Modify: `docs/architecture/frontend-structure.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Add the Design System planning note**

Create a doc that states:

```markdown
# Frontend Design System Planning

The console has a working third-stage skeleton, but the visual design system is not selected. Backend SDK, migrations and deployment verification must not wait on UI work, and UI work must not start by accident while backend foundations are still settling.

Before adding new console pages or restyling packages/ui, create a separate Superpowers spec that decides component library, token model, icon policy, density, accessibility baseline, theme strategy and migration path from the current local primitives.
```

- [ ] **Step 2: Update API contract rules**

Add:

```markdown
Backend SDK and OpenAPI changes may regenerate `frontend/packages/api-client`, but this does not authorize new console views. If a backend contract is not needed by the current console, keep the generated client change mechanical and covered by generated contract tests.
```

- [ ] **Step 3: Update readiness and README**

Add the fifth-stage plan and verification command to the existing plan/status lists. State that frontend feature work is intentionally deferred until the design system spec exists.

## Task 6: Final Verification

**Files:**

- No new files unless a previous task uncovered a missing test or doc.

- [ ] **Step 1: Run backend and connector tests**

Run:

```powershell
dotnet test backend/Nerv.IIP.sln
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln
```

Expected: both exit `0`.

- [ ] **Step 2: Run frontend quality gates only if frontend files changed**

If any `frontend/` file changed, run:

```powershell
pnpm -C frontend check
pnpm -C frontend fmt
pnpm -C frontend lint
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

Expected: all exit `0`.

- [ ] **Step 3: Run repository whitespace check**

Run:

```powershell
git diff --check
```

Expected: exit `0`.

## Execution Order

1. Task 1 first, because migration generation must use a pinned local tool.
2. Tasks 2 and 3 can be assigned to separate workers only if their write sets remain disjoint.
3. Task 4 depends on Tasks 2 and 3.
4. Task 5 can run in parallel with Task 2 or Task 3 because it only touches docs.
5. Task 6 runs last.

## Self Review

Spec coverage:

1. Migration replacement for `EnsureCreated()` is covered by Tasks 2, 3 and 4.
2. Tooling repeatability is covered by Task 1.
3. Frontend deferral and Design System planning are covered by Task 5.
4. Verification is covered by Task 6.

Placeholder scan:

1. No `TBD` or `TODO` markers remain.
2. Commands use concrete paths and expected outputs.
3. The only optional branch is frontend gates, and it is tied to whether frontend files changed.

Type consistency:

1. AppHub runner name is `AppHubDatabaseMigrationRunner` throughout.
2. Ops runner name is `OpsDatabaseMigrationRunner` throughout.
3. Migration command context names match current Infrastructure `ApplicationDbContext` namespaces.
