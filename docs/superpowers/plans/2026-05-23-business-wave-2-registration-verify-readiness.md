# 业务第 2 波次注册、验证与就绪状态实施计划

> **供代理执行者使用：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐任务实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**协调第 2 波次服务在 #128、#133、#134 和 #136 的服务分支就绪后的共享集成。

**架构：**这是协调计划。它在各服务会话产出可编译项目和聚焦测试后，应用共享的解决方案、AppHost、验证脚本和就绪状态变更。它不得实现服务领域行为。

**技术栈：**.NET 10、Aspire AppHost、受治理的 PowerShell 脚本、`scripts/lib/ScriptAutomation.ps1`、Markdown 架构文档。

---

## 输入

本计划使用以下服务会话的输出：

1. #128 DemandPlanning MVP。
2. #133 BarcodeLabel MVP。
3. #134 BusinessApproval MVP。
4. #136 WMS 执行 MVP。

每个服务 PR/会话都应包含 `Shared Changes Needed`。如果某个服务无法编译，则跳过注册并将其记录为受阻。

## 文件

- 修改：`backend/Nerv.IIP.sln`
- 修改：`infra/aspire/Nerv.IIP.AppHost/Program.cs`
- 修改：`docs/architecture/authorization-matrix.md`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 创建：`scripts/verify-business-demand-planning-mvp.ps1`
- 创建：`scripts/verify-business-barcode-label-mvp.ps1`
- 创建：`scripts/verify-business-approval-mvp.ps1`
- 创建：`scripts/verify-business-wms-execution-mvp.ps1`
- 创建：`scripts/verify-business-wave2-execution.ps1`

## 任务 1：收集服务输出

- [ ] **步骤 1：检查服务目录**

运行：

```powershell
rg --files backend/services/Business/DemandPlanning backend/services/Business/BarcodeLabel backend/services/Business/Approval backend/services/Business/Wms
```

预期：只有已存在并可编译的服务才会纳入注册范围。

- [ ] **步骤 2：检查共享变更说明**

对于每个第 2 波次分支或会话，将其 `Shared Changes Needed` 章节复制到集成摘要中。如果该章节不存在，必须先检查服务测试和项目文件，再决定共享变更。

## 任务 2：添加解决方案条目

- [ ] **步骤 1：将就绪项目添加到后端解决方案**

对每个就绪的 Domain、Infrastructure、Web 和测试项目运行 `dotnet sln backend/Nerv.IIP.sln add`。

候选服务根目录：

1. `backend/services/Business/DemandPlanning`
2. `backend/services/Business/BarcodeLabel`
3. `backend/services/Business/Approval`
4. `backend/services/Business/Wms`

- [ ] **步骤 2：验证解决方案构建**

运行：

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期：解决方案构建成功。如果某个第 2 波次服务失败，只从本次集成批次中移除该服务，并记录阻塞原因。

## 任务 3：注册 AppHost 服务

- [ ] **步骤 1：添加 AppHost 数据库和服务注册**

使用现有第 1 波次业务服务的注册风格。候选服务名称：

1. `business-demand-planning`
2. `business-barcode-label`
3. `business-approval`
4. `business-wms`

第 1 波次之后建议使用的下一组本地端口是 `5112-5115`，但如果现有端口矩阵已更新，则保持其定义不变。

- [ ] **步骤 2：仅在需要时添加跨服务基础 URL**

如果本批次中的 WMS 通过 HTTP 调用 Inventory，则传入 `Inventory__BaseUrl` 或该服务既定的等效配置。如果 DemandPlanning 通过 HTTP 调用 ProductEngineering/Inventory，则传入 `ProductEngineering__BaseUrl` 和 `Inventory__BaseUrl`。

- [ ] **步骤 3：验证 AppHost 构建**

运行：

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期：AppHost 构建成功。

## 任务 4：添加验证脚本

- [ ] **步骤 1：创建服务验证脚本**

每个脚本都必须点源 `scripts/lib/ScriptAutomation.ps1`，并使用 `Invoke-DotNet` 等辅助函数。脚本内部不得直接调用 `dotnet`。

为已就绪的服务创建以下脚本：

1. `scripts/verify-business-demand-planning-mvp.ps1`
2. `scripts/verify-business-barcode-label-mvp.ps1`
3. `scripts/verify-business-approval-mvp.ps1`
4. `scripts/verify-business-wms-execution-mvp.ps1`

每个脚本只运行其所属服务的聚焦 Domain 和 Web 测试。

- [ ] **步骤 2：创建第 2 波次聚合验证脚本**

创建 `scripts/verify-business-wave2-execution.ps1`。该脚本应运行：

1. `scripts/verify-business-wave1-foundation.ps1`
2. 所有已就绪的第 2 波次服务验证脚本。
3. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`。

- [ ] **步骤 3：运行脚本治理**

运行：

```powershell
scripts/check-script-governance.ps1
```

预期：脚本治理通过。

## 任务 5：更新授权、schema 目录和就绪状态

- [ ] **步骤 1：更新授权矩阵和 IAM 初始权限数据**

添加服务规格中的权限：

1. `business.planning.*`
2. `business.barcodes.*`
3. `business.approvals.*`
4. `business.wms.*`

- [ ] **步骤 2：更新数据库 schema 目录**

添加或刷新以下条目：

1. `demand_planning`
2. `barcode`
3. `business_approval`
4. `wms`

只记录当前分支的 migration 中实际存在的表。

- [ ] **步骤 3：更新实施就绪状态**

记录：

1. 哪些第 2 波次服务可以编译。
2. 哪些验证脚本已存在并通过。
3. 哪些服务已注册到 AppHost。
4. 哪些下游 ERP Issue 已解除阻塞。
5. 哪些服务受阻或被有意延后。

## 任务 6：最终验证

- [ ] **步骤 1：运行第 2 波次聚焦验证**

运行：

```powershell
scripts/verify-business-wave2-execution.ps1
```

预期：对于已注册的服务，脚本以 `0` 退出。

- [ ] **步骤 2：运行后端构建**

运行：

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期：构建通过。

- [ ] **步骤 3：报告集成状态**

在会话摘要中加入 `Wave 2 Integration State` 章节。每个服务条目都必须使用以下精确状态词之一：`registered`、`skipped` 或 `blocked`，并在其后写明原因和验证命令。
