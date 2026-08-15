# 业务服务注册、验证与就绪状态实施计划

> **供代理执行者使用：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 子技能逐任务实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**实施 #140，协调第 1 波次的共享集成变更，包括解决方案条目、AppHost 注册、验证脚本模式、授权/schema 文档和就绪状态跟踪。

**架构：**这是协调计划，而不是领域功能计划。它在各服务会话产出可编译项目和聚焦测试后应用共享变更，并确保服务所属的领域实现不会进入 AppHost、IAM、脚本和就绪状态文档。

**技术栈：**.NET 10、Aspire AppHost、受治理的 PowerShell 脚本、`scripts/lib/ScriptAutomation.ps1`、GitHub Issue/PR 交接说明、Markdown 架构文档。

---

## 输入

本计划使用以下服务会话的输出：

1. #127 ProductEngineering 缺口补齐。
2. #131 Inventory MVP。
3. #132 Quality 检验 MVP。
4. #135 MES CleanDDD 持久化。

每个服务 PR 的正文都必须包含 `Shared Changes Needed`。如果某个服务尚未合并或无法编译，则跳过其注册，并在就绪状态中将其记录为受阻。

## 文件

- 修改：`backend/Nerv.IIP.sln`
- 修改：`infra/aspire/Nerv.IIP.AppHost/Program.cs`
- 修改：`docs/architecture/authorization-matrix.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 创建：`scripts/verify-business-product-engineering-mvp.ps1`
- 创建：`scripts/verify-business-inventory-mvp.ps1`
- 创建：`scripts/verify-business-quality-inspection-mvp.ps1`
- 创建：`scripts/verify-business-mes-execution-mvp.ps1`
- 创建：`scripts/verify-business-wave1-foundation.ps1`

## 任务 1：收集服务输出

- [ ] **步骤 1：检查服务目录**

运行：

```powershell
rg --files backend/services/Business/ProductEngineering backend/services/Business/Inventory backend/services/Business/Quality backend/services/Business/Mes
```

预期：只有已存在并可编译的服务才会纳入注册范围。

- [ ] **步骤 2：检查共享变更说明**

对于每个第 1 波次服务分支或 PR，将其 `Shared Changes Needed` 章节复制到 #140 的 PR 描述中。如果该章节不存在，必须先检查服务测试和项目文件，再决定共享变更。

## 任务 2：添加解决方案条目

- [ ] **步骤 1：将缺失项目添加到后端解决方案**

对每个可编译但尚未加入解决方案的项目运行 `dotnet sln backend/Nerv.IIP.sln add`。使用 `rg --files` 输出中的精确路径。

必须考虑的第 1 波次候选项目：

```powershell
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/Nerv.IIP.Business.ProductEngineering.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/Nerv.IIP.Business.Quality.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Nerv.IIP.Business.Quality.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Nerv.IIP.Business.Quality.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj
```

只有 Inventory 和 MES 的目录存在后，才能添加其项目。

- [ ] **步骤 2：验证解决方案构建**

运行：

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期：解决方案构建成功。如果某个第 1 波次服务失败，只从本次 #140 集成批次中移除该服务，并记录阻塞原因。

## 任务 3：注册 AppHost 服务

- [ ] **步骤 1：添加 AppHost 注册**

按照现有服务注册风格修改 `infra/aspire/Nerv.IIP.AppHost/Program.cs`。只注册能够编译且已由端口矩阵分配稳定本地端口的 Web 项目。

候选服务名称：

1. `business-product-engineering`
2. `business-inventory`
3. `business-quality`
4. `business-mes`

- [ ] **步骤 2：验证 AppHost 构建**

运行：

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：AppHost 构建成功。

## 任务 4：添加验证脚本

- [ ] **步骤 1：创建服务验证脚本**

每个脚本都必须点源 `scripts/lib/ScriptAutomation.ps1`，并使用 `Invoke-DotNet` 等辅助函数。脚本内部不得直接调用 `dotnet`。

为已存在的服务创建以下脚本：

1. `scripts/verify-business-product-engineering-mvp.ps1`
2. `scripts/verify-business-inventory-mvp.ps1`
3. `scripts/verify-business-quality-inspection-mvp.ps1`
4. `scripts/verify-business-mes-execution-mvp.ps1`

每个脚本只运行其所属服务的聚焦 Domain 和 Web 测试。

- [ ] **步骤 2：创建第 1 波次聚合验证脚本**

创建 `scripts/verify-business-wave1-foundation.ps1`。该脚本应运行：

1. `scripts/verify-business-master-data-realignment.ps1`
2. 所有已存在的第 1 波次服务验证脚本。
3. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`。

- [ ] **步骤 3：运行脚本治理**

运行：

```powershell
scripts/check-script-governance.ps1
```

预期：脚本治理通过。

## 任务 5：更新授权、schema 目录和就绪状态

- [ ] **步骤 1：更新授权矩阵**

添加服务规格和计划中的第 1 波次权限。至少包含：

1. `business.engineering.boms.manage`
2. `business.engineering.routings.manage`
3. `business.engineering.changes.manage`
4. `business.inventory.locations.manage`
5. `business.inventory.movements.create`
6. `business.inventory.ledger.read`
7. `business.inventory.counts.manage`
8. `business.quality.inspection-plans.manage`
9. `business.quality.inspection-records.create`
10. `business.quality.inspection-records.read`
11. `business.mes.work-orders.manage`

- [ ] **步骤 2：更新数据库 schema 目录**

添加或刷新以下 schema 的条目：

1. `product_engineering`
2. `inventory`
3. `quality`
4. `mes`

只记录当前分支的 migration 中实际存在的表。

- [ ] **步骤 3：更新实施就绪状态**

在 `docs/architecture/implementation-readiness.md` 中更新：

1. 哪些第 1 波次服务可以编译。
2. 哪些验证脚本已存在并通过。
3. 哪些服务已注册到 AppHost。
4. 哪些下游 Issue 已解除阻塞。
5. 任何阻塞项，例如 Docker 不可用或服务分支尚未合并。

## 任务 6：最终验证

- [ ] **步骤 1：运行第 1 波次聚焦验证**

运行：

```powershell
scripts/verify-business-wave1-foundation.ps1
```

预期：对于已注册的服务，脚本以 `0` 退出。只有脚本明确依赖 Docker 时，才将无法执行的 Docker 相关检查报告为环境阻塞项。

- [ ] **步骤 2：运行后端构建**

运行：

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期：构建通过。

- [ ] **步骤 3：报告集成状态**

在 #140 的 PR 正文中加入 `Wave 1 Integration State` 章节。每个服务条目都必须使用以下精确状态词之一：`registered`、`skipped` 或 `blocked`，并在其后写明原因和验证命令。为 AppHost、验证脚本、已解除阻塞的下游 Issue 和阻塞项分别列出条目。
