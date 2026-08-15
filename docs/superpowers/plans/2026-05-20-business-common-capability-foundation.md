# 业务通用能力基础实施计划

> **面向代理执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**构建后续 ERP、WMS 和 MES 纵切所需的通用业务能力：Inventory、Quality、BarcodeLabel 和 BusinessApproval。

**架构：**在 `backend/services/Business` 下实施四个职责集中的业务服务：Inventory 拥有库存余额和库存移动事实；Quality 拥有检验和不合格事实；BarcodeLabel 拥有标签和扫码事实；Approval 拥有业务审批链。这些服务可以交换集成事件和稳定单据引用，但绝不共享数据库 schema。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 集成事件、xUnit。

---

## 范围

这是一个多服务基础计划，因为这四个服务是 WMS、MES 和 ERP 紧密关联的前置条件，但仍保持各自独立的持久化边界。

本计划依赖 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`。Inventory 必须使用 MasterData 的 SKU 追溯策略和 UOM 换算；Quality 必须使用 SKU、业务伙伴、设备/工作中心以及可复用特性定义，但不得拥有 SKU 或业务伙伴主数据事实；BarcodeLabel 必须使用 SKU 和默认条码策略；BusinessApproval 可以引用业务组织属性，但不得复制 IAM 角色或权限。

## 边界

1. Inventory 是库存余额和库存移动的唯一所有者。
2. 此处不实施 WMS 和 MES 执行步骤。
3. BusinessApproval 不取代 Ops 审批。
4. Quality 不直接改变库存余额；它发布供 WMS 或 Inventory 使用的检验结果。
5. BarcodeLabel 不拥有业务单据状态。
6. Inventory 拥有实际批次、序列号、炉批、到期时间、库位状态和库存移动事实；MasterData 拥有 SKU 追溯策略和 UOM 规则。
7. Quality 拥有检验标准、记录、COA、不合格项和放行决策；仅当可复用引用定义跨领域时，才由 MasterData 拥有这些定义。

## 文件结构图

```text
backend/services/Business/Inventory/
backend/services/Business/Quality/
backend/services/Business/BarcodeLabel/
backend/services/Business/Approval/

Each service:
  src/Nerv.IIP.Business.{Context}.Domain/
  src/Nerv.IIP.Business.{Context}.Infrastructure/
  src/Nerv.IIP.Business.{Context}.Web/
  tests/Nerv.IIP.Business.{Context}.Domain.Tests/
  tests/Nerv.IIP.Business.{Context}.Web.Tests/
```

## 任务 1：为四个通用能力服务搭建脚手架

**文件：**

- 新建：`backend/services/Business/Inventory/*`
- 新建：`backend/services/Business/Quality/*`
- 新建：`backend/services/Business/BarcodeLabel/*`
- 新建：`backend/services/Business/Approval/*`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建服务和测试项目**

按上下文分别运行一次，并使用准确的上下文名称 `Inventory`、`Quality`、`BarcodeLabel`、`Approval`：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Inventory -o backend/services/Business/Inventory --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Inventory.Domain.Tests -o backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Inventory.Web.Tests -o backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests --framework net10.0
dotnet new netcorepal-web -n Nerv.IIP.Business.Quality -o backend/services/Business/Quality --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Quality.Domain.Tests -o backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Quality.Web.Tests -o backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests --framework net10.0
dotnet new netcorepal-web -n Nerv.IIP.Business.BarcodeLabel -o backend/services/Business/BarcodeLabel --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.BarcodeLabel.Domain.Tests -o backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.BarcodeLabel.Web.Tests -o backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests --framework net10.0
dotnet new netcorepal-web -n Nerv.IIP.Business.Approval -o backend/services/Business/Approval --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Approval.Domain.Tests -o backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Approval.Web.Tests -o backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests --framework net10.0
```

- [ ] **步骤 2：将所有项目加入解决方案**

运行：

```powershell
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/Nerv.IIP.Business.Inventory.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Nerv.IIP.Business.Inventory.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/Nerv.IIP.Business.Quality.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Nerv.IIP.Business.Quality.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Nerv.IIP.Business.Quality.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/Nerv.IIP.Business.BarcodeLabel.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Nerv.IIP.Business.BarcodeLabel.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Nerv.IIP.Business.BarcodeLabel.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/Nerv.IIP.Business.Approval.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Nerv.IIP.Business.Approval.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/Nerv.IIP.Business.Approval.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/Nerv.IIP.Business.Approval.Web.Tests.csproj
```

- [ ] **步骤 3：提交脚手架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Inventory backend/services/Business/Quality backend/services/Business/BarcodeLabel backend/services/Business/Approval
git commit -m "feat: scaffold business common capability services"
```

## 任务 2：实施 Inventory 库存事实

**文件：**

- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLocationAggregate/StockLocation.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockMovementAggregate/StockMovement.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountTask.cs`
- 新建：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`

- [ ] **步骤 1：编写失败的 Inventory 测试**

覆盖：

```csharp
StockLocation.Create("org-001", "env-dev", "WH-A", "A-01-01");
StockMovement.Post("org-001", "env-dev", "receipt", "SKU-RM-1000", 10m, "WH-A", "A-01-01", "PO-1000", "idem-001");
StockLedger.ApplyMovement(movement);
StockCountTask.Create("org-001", "env-dev", "COUNT-001", "WH-A").ConfirmVariance("SKU-RM-1000", "A-01-01", 8m, 10m, "approval-chain-001");
```

断言 `idempotencyKey` 为必填项、负数移动数量会被拒绝，且可用数量绝不会变为负数。

- [ ] **步骤 2：实施领域和事件**

创建 `StockMovementPostedDomainEvent`、`StockCountTaskCreatedDomainEvent` 和 `StockCountVarianceConfirmedDomainEvent`。`StockLedger` 暴露 `OnHandQuantity`、`AvailableQuantity` 和 `FrozenQuantity`。

- [ ] **步骤 3：增加 Inventory 持久化和 API**

使用 schema `inventory` 和以下路由：

| 路由 | 权限 |
| --- | --- |
| `POST /api/inventory/v1/locations` | `business.inventory.locations.manage` |
| `POST /api/inventory/v1/movements` | `business.inventory.movements.create` |
| `GET /api/inventory/v1/availability` | `business.inventory.ledger.read` |
| `POST /api/inventory/v1/count-tasks` | `business.inventory.counts.manage` |
| `POST /api/inventory/v1/count-tasks/{countTaskId}/adjustments` | `business.inventory.counts.manage` |

运行：

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore
```

预期：通过。

- [ ] **步骤 4：提交 Inventory**

运行：

```powershell
git add backend/services/Business/Inventory docs/architecture/database-schema-catalog.md
git commit -m "feat: add inventory stock facts"
```

## 任务 3：实施 Quality 检验事实

**文件：**

- 新建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionPlanAggregate/InspectionPlan.cs`
- 新建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs`
- 新建：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/QualityAggregateTests.cs`

- [ ] **步骤 1：编写失败的 Quality 测试**

覆盖：

```csharp
var plan = InspectionPlan.Create("org-001", "env-dev", "receiving", "PO-RECEIPT-001", "SKU-RM-1000");
var record = plan.RecordResult("inspector-001", "passed", 10m, 0m, Array.Empty<string>());
```

断言检验结果必须是 `passed`、`rejected`、`conditional-release` 之一；被拒绝的记录必须提供处置原因；附件 ID 仅作为文件引用。

- [ ] **步骤 2：实施 schema、事件和端点**

使用 schema `quality`、事件 `InspectionPassedDomainEvent`、`InspectionRejectedDomainEvent`、`NonconformanceDispositionCompletedDomainEvent`，以及以下路由：

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/quality/inspection-plans` | `business.quality.inspection-plans.manage` |
| `POST /api/business/v1/quality/inspection-records` | `business.quality.inspection-records.create` |
| `GET /api/business/v1/quality/inspection-records` | `business.quality.inspection-records.read` |

- [ ] **步骤 3：运行测试并提交**

运行：

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
git add backend/services/Business/Quality docs/architecture/database-schema-catalog.md
git commit -m "feat: add quality inspection facts"
```

预期：提交前测试通过。

## 任务 4：实施 Barcode 和 Approval 事实

**文件：**

- 新建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/BarcodeRuleAggregate/BarcodeRule.cs`
- 新建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelPrintBatchAggregate/LabelPrintBatch.cs`
- 新建：`backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/ScanRecordAggregate/ScanRecord.cs`
- 新建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalTemplateAggregate/ApprovalTemplate.cs`
- 新建：`backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`

- [ ] **步骤 1：编写失败的测试**

Barcode 测试覆盖根据模板确定性生成标签、按来源设备和幂等键保证扫码幂等，以及拒绝空条码值。Approval 测试覆盖审批链创建、按顺序批准步骤、附带评论的拒绝操作，以及防止审批人重复操作。

- [ ] **步骤 2：实施 schema 和端点**

使用 schema `barcode` 和 `business_approval`。增加以下路由：

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/barcodes/templates` | `business.barcodes.templates.manage` |
| `POST /api/business/v1/barcodes/print-batches` | `business.barcodes.print` |
| `POST /api/business/v1/barcodes/scans` | `business.barcodes.scans.write` |
| `POST /api/business/v1/approvals/chains` | `business.approvals.manage` |
| `POST /api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve` | `business.approvals.manage` |
| `GET /api/business/v1/approvals/chains/{chainId}` | `business.approvals.read` |

- [ ] **步骤 3：运行测试并提交**

运行：

```powershell
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/Nerv.IIP.Business.Approval.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/Nerv.IIP.Business.Approval.Web.Tests.csproj --no-restore
git add backend/services/Business/BarcodeLabel backend/services/Business/Approval docs/architecture/database-schema-catalog.md
git commit -m "feat: add barcode and business approval capabilities"
```

预期：所有测试通过。

## 任务 5：初始化权限并增加验证

**文件：**

- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- 新建：`scripts/verify-business-common-capability-foundation.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：初始化权限**

将 `business.inventory.*`、`business.quality.*`、`business.barcodes.*` 和 `business.approvals.*` 中列于 `docs/architecture/authorization-matrix.md` 的权限加入 IAM 初始管理员角色。

- [ ] **步骤 2：创建验证脚本**

该脚本运行 `Inventory`、`Quality`、`BarcodeLabel` 和 `Approval` 下的所有 Domain 和 Web 测试，然后运行 IAM 初始数据测试。

- [ ] **步骤 3：运行最终验证**

运行：

```powershell
scripts/verify-business-common-capability-foundation.ps1
git diff --check
```

预期：两条命令都以 `0` 退出。

- [ ] **步骤 4：提交验证变更**

运行：

```powershell
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs scripts/verify-business-common-capability-foundation.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record common capability readiness"
```

## 自我审核清单

1. Inventory 是唯一包含库存余额字段的服务。
2. Quality 结果不直接修改库存余额。
3. Barcode 扫码命令和 Inventory 库存移动命令要求幂等键。
4. Approval 服务文档明确说明其用于业务单据，而非 Ops 任务。
