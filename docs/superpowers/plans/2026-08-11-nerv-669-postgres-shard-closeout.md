# NERV-669 真 PostgreSQL 分片缺口收口实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `InventoryDirectoryPostgresTests` 从默认快速分片移交给 `real-postgres` owner lane，并用反向门禁防止直接 Docker 测试再次漏入快速门禁。

**Architecture:** 生产测试类以显式 `CreateDockerAsync()` / `CreateExternalAsync()` 两条路径运行，并通过环境门禁属性在默认未启用状态下跳过；分片 manifest 以类级选择器整体排除它，MAN-661 政策以两个精确测试身份登记。分片治理从测试源码审计 `ProcessStartInfo` 构造函数、对象初始化器和 `Process.Start` 重载等直接 Docker CLI 入口；快速分片中的对应测试类型必须由所属分片排除，整个项目归属 heavy lane 时则由该 lane 的 owner script 与 evidence policy 管理。

**Tech Stack:** PowerShell 7、.NET 10、xUnit、JSON manifest/policy、Npgsql、Docker CLI。

## Global Constraints

- 所有新增或修改的人工文档使用简体中文；代码、命令、路径、标识符和配置键保持原文。
- 只处理 NERV-812 / #1499；不接入 GitHub-hosted heavy lane。审核后仅因真实 CI 的治理契约 step 超过原 5 分钟上限，把该 step 预算调整为 8 分钟；不改变 workflow 拓扑、lane 接线或 required check。
- 不改业务 HTTP endpoint、facade、OpenAPI、schema、migration 或前端。
- 生产脚本必须 dot-source `scripts/lib/ScriptAutomation.ps1`，不得直接调用受治理外部命令。
- 标识符比较必须使用显式 ordinal（序数）比较；不得使用 PowerShell 默认 `-eq` / `-contains` / `Sort-Object -Unique` 承担身份判定。
- 每项行为变更遵循 RED → GREEN；测试必须运行真实脚本/真实门禁，不得以源码字面量存在性作为唯一证明。

---

### Task 1: 先红后绿收口 Inventory 目录真实 PostgreSQL 分片

**Files:**
- Modify: `scripts/tests/backend-test-shards.Tests.ps1`
- Modify: `scripts/verify-backend-test-shards.ps1`
- Modify: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryDirectoryPostgresTests.cs`
- Modify: `scripts/backend-test-shards.json`
- Modify: `scripts/test-evidence-policy.json`

**Interfaces:**
- Consumes: `-BackendInventoryRoot` 测试 seam、`fastShards[].excludedTestClasses`、`NERV_IIP_TEST_POSTGRES`、`real-postgres.policyLane = postgres`。
- Produces: class selector `Nerv.IIP.Business.Inventory.Web.Tests.InventoryDirectoryPostgresTests`、两个精确 policy identities，以及 finding `Real dependency test type '<fully-qualified-type>' uses the audited Docker CLI primitive but is not excluded from its fast shard.`

- [ ] **Step 1: 写直接 Docker 反向门禁契约**

  在既有临时 `$temporaryProjectDirectory` 下写入 `DirectDockerTests.cs`，内容包含 namespace、`public sealed class DirectDockerTests`、`[Fact]` 方法和 `new ProcessStartInfo("docker")`。调用真实 validator 时断言退出码非零且完整 stdout 包含上述精确 finding；该断言针对行为输出，不读取 validator 源码。

  另增加生产 manifest 行为断言：`business-core-a.excludedTestClasses` 必须包含 `Nerv.IIP.Business.Inventory.Web.Tests.InventoryDirectoryPostgresTests`。复制 manifest 删除该选择器后调用真实 validator，必须得到相同的直接 Docker finding。复制 policy 删除 `inventory-directory-postgres` rule 后，真实 validator 必须以“未登记 environment-gated real-dependency skip”失败。

- [ ] **Step 2: 运行契约测试并验证 RED**

  Run: `pwsh scripts/tests/backend-test-shards.Tests.ps1`

  Expected: FAIL；当前生产 manifest 尚无 selector，且 validator 尚不能输出直接 Docker finding。确认失败来自缺失行为而非 PowerShell 语法或 fixture 路径。

- [ ] **Step 3: 实现最小反向审计**

  在 validator 中扫描 `BackendInventoryRoot`（未传时为仓库 `backend`）下测试项目目录的 `*.cs`，审计 `ProcessStartInfo` 构造函数、`FileName = "docker"` 对象初始化器和 `Process.Start` 重载；位置实参、命名实参及其合法换序均由真实 validator fixture 覆盖。从 namespace 与包含入口的 `class` 声明组合 fully-qualified type，以 `StringComparer.Ordinal` 集合比对所属快速分片的 `excludedTestClasses`；整个项目唯一归属 heavy lane 时交给该 lane 的 owner script 与 evidence policy，缺失、重复或未知归属仍 fail closed。无法解析 namespace/type 时也加入错误。诊断仅含仓库相对路径与类型，不含源码 body。

- [ ] **Step 4: 显式化两个 PostgreSQL 运行路径**

  新增 `InventoryDirectoryPostgresFactAttribute : FactAttribute`：当 `NERV_IIP_TEST_POSTGRES` 为空时设置精确 Skip 原因 `Set NERV_IIP_TEST_POSTGRES and ensure Docker is available to run Inventory directory PostgreSQL tests.`。两个真实依赖测试都改用该 attribute。

  将 `DirectoryPostgresScope.CreateAsync()` 拆成：

  - `CreateDockerAsync()`：始终创建 run-scoped Docker volume/container、解析动态端口、等待连接并负责清理，不读取外部连接串；
  - `CreateExternalAsync()`：只读取非空 `NERV_IIP_TEST_POSTGRES` 并返回不拥有容器资源的 scope，否则 fail closed。

  第一个测试调用两次 `CreateDockerAsync()`，第二个测试调用 `CreateExternalAsync()`；删除第一个测试的提前 `return`。

- [ ] **Step 5: 更新 manifest 与 policy**

  在 `business-core-a.excludedTestClasses` 的 ordinal 排序位置加入类选择器。新增 policy source：

  - id: `inventory-directory-postgres`
  - sourcePath: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryDirectoryPostgresTests.cs`
  - sourceOrdinal: `1`
  - sourceReasonPattern 精确匹配新 Skip 赋值

  新增同 id rule，`classification: environment-gated`、`allowedLanes: ["backend"]`、`requiredLane: "postgres"`、两个精确 `testIdentities`、`expectedRuntimeTestCount: 2`。

- [ ] **Step 6: 运行 GREEN 与削弱验证**

  Run:

  ```powershell
  pwsh scripts/verify-backend-test-shards.ps1
  pwsh scripts/tests/backend-test-shards.Tests.ps1
  pwsh scripts/tests/test-evidence.Tests.ps1
  dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --configuration Release --filter "FullyQualifiedName~InventoryDirectoryPostgresTests"
  ```

  Expected: 三个脚本 exit 0；未设置 `NERV_IIP_TEST_POSTGRES` 的定向 .NET 命令发现两个测试且两者为 skip，证明默认命令不会启动容器。不得把这条 skip 运行冒充 owner-lane 执行通过。

  削弱 fixture 分别删除类选择器、删除 policy rule、加入未排除的直接 Docker 类型；每种都必须让真实 validator exit 1 并匹配各自 finding。恢复 fixture 后全套契约再次 exit 0。

- [ ] **Step 7: 提交 Task 1**

  ```bash
  git add scripts/verify-backend-test-shards.ps1 scripts/tests/backend-test-shards.Tests.ps1 backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryDirectoryPostgresTests.cs scripts/backend-test-shards.json scripts/test-evidence-policy.json
  git commit -m "fix(ci): 将 Inventory 目录 PostgreSQL 测试移出快速分片"
  ```

### Task 2: 更新权威状态并完成全范围验证

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`

**Interfaces:**
- Consumes: Task 1 的最终 selector、policy 和门禁行为。
- Produces: NERV-669 / NERV-812 当前事实与剩余边界的权威中文状态。

- [ ] **Step 1: 更新 implementation readiness**

  在 MAN-669 分片章节登记：`InventoryDirectoryPostgresTests` 已从 `business-core-a` 快速分片整体排除；两个运行模式由 `real-postgres` owner script 发现并要求 TRX 全通过；反向门禁当前审计直接 Docker CLI 原语。明确三条 heavy lane 仍未接 GitHub-hosted CI，本 PR 不声称 hosted runtime 通过。

- [ ] **Step 2: 运行受影响完整门禁**

  Run:

  ```powershell
  pwsh scripts/verify-backend-test-shards.ps1
  pwsh scripts/tests/backend-test-shards.Tests.ps1
  pwsh scripts/tests/test-evidence.Tests.ps1
  pwsh scripts/check-script-governance.ps1
  dotnet test backend/Nerv.IIP.sln --configuration Release
  ```

  Docker daemon 不可用时，不运行 `verify-backend-real-postgres-tests.ps1`，并如实报告“owner lane 未执行”，不得标为代码失败或通过。

- [ ] **Step 3: 检查范围与格式**

  Run:

  ```bash
  git diff --check origin/main...HEAD
  git diff --name-only origin/main...HEAD
  git status -sb
  ```

  Expected: 只有设计/计划、Inventory 测试、分片 validator/契约、manifest/policy 与 implementation readiness；无 workflow、endpoint、OpenAPI、schema、migration 或前端文件。

- [ ] **Step 4: 提交 Task 2**

  ```bash
  git add docs/architecture/implementation-readiness.md docs/superpowers/plans/2026-08-11-nerv-669-postgres-shard-closeout.md
  git commit -m "docs(ci): 记录 NERV-669 分片缺口收口"
  ```
