# 业务 MES 执行 MVP 实施计划

> **面向智能体执行者：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**构建 MES MVP，覆盖工单、工序任务、规则排程、报工、完工入库请求和生产日报。

**架构：**MES 拥有制造执行事实，并引用 ProductEngineering 的 ProductionVersion ID；这些 ID 可解析到已发布的 MBOM/工艺路线版本。MES 接受 DemandPlanning 提供的计划工单建议，但不负责 MRP 计算。完工入库向 WMS 发起请求；库存余额仍归 Inventory 所有。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 集成事件、xUnit。

---

## MasterData 重对齐依赖

执行本计划前，必须先完成 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`。MES 必须通过 MasterData 契约解析 SKU、UOM、工作中心、工作日历、设备资产、团队和人员技能引用。对于流程制造，MES 拥有批次执行、实际消耗/产出、批记录、偏差、清洗执行和谱系；它不拥有配方版本或静态物料/资源主数据事实。

## 边界

1. 不包含 APS 优化器；排程采用确定性规则排程。
2. 不直接写入库存余额。
3. 不直接变更维护事实；MES 消费可用性事件。
4. 工单必须提供 ProductEngineering 的 productionVersionId，并能解析到已发布的 MBOM 和工艺路线引用。
5. 流程批记录、实际工艺值和偏差属于 MES 执行事实；可复用物料属性、UOM 和静态资源能力仍属于 MasterData 事实。

## 文件结构图

```text
backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/
  AggregatesModel/WorkOrderAggregate/WorkOrder.cs
  AggregatesModel/OperationTaskAggregate/OperationTask.cs
  AggregatesModel/ProductionReportAggregate/ProductionReport.cs
  AggregatesModel/ScheduleResultAggregate/ScheduleResult.cs
  AggregatesModel/FinishedGoodsReceiptRequestAggregate/FinishedGoodsReceiptRequest.cs

backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/
  Application/Scheduling/RuleScheduler.cs
  Application/Commands/CreateWorkOrderFromSuggestionCommand.cs
  Application/Commands/ReleaseWorkOrderCommand.cs
  Application/Commands/ReportOperationCommand.cs
  Application/Commands/CreateFinishedGoodsReceiptRequestCommand.cs
  Application/Queries/GetScheduleGanttQuery.cs
  Application/IntegrationEvents/MesIntegrationEvents.cs
  Endpoints/Mes/MesEndpoints.cs
```

## 任务 1：搭建 MES 服务脚手架

**文件：**

- 新增：`backend/services/Business/Mes/*`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建服务和测试**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Mes -o backend/services/Business/Mes --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Mes.Domain.Tests -o backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Mes.Web.Tests -o backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/Nerv.IIP.Business.Mes.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Nerv.IIP.Business.Mes.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj
```

- [ ] **步骤 2：提交脚手架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Mes
git commit -m "feat: scaffold mes service"
```

## 任务 2：实现工单与报工模型

**文件：**

- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/WorkOrderAggregate/WorkOrder.cs`
- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`
- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReport.cs`
- 新增：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/MesAggregateTests.cs`

- [ ] **步骤 1：编写失败测试**

覆盖：

```csharp
var workOrder = WorkOrder.FromPlanningSuggestion("org-001", "env-dev", "suggestion-wo-001", "SKU-FG-1000", 8m, "production-version-A");
workOrder.Release("approval-chain-003");
var task = OperationTask.Create("org-001", "env-dev", workOrder.Id.Value, 10, "WC-CNC-01", 8m);
var report = task.Report(5m, 1m, "surface-defect", 120, "idem-report-001");
```

断言创建时必须提供可追溯至 MBOM/工艺路线引用的 productionVersionId，合格数量与缺陷数量之和不得超过剩余数量，缺陷数量必须附带原因，并且报工必须提供幂等键。

- [ ] **步骤 2：实现事件**

创建 `WorkOrderReleasedDomainEvent`、`OperationReportedDomainEvent`、`FinishedGoodsReceiptRequestedDomainEvent` 和 `DowntimeRecordedDomainEvent`。

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj --no-restore
git add backend/services/Business/Mes
git commit -m "feat: add mes work order reporting model"
```

预期：测试在提交前通过。

## 任务 3：实现规则排程器与甘特图查询

**文件：**

- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ScheduleResultAggregate/ScheduleResult.cs`
- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Scheduling/RuleScheduler.cs`
- 新增：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RuleSchedulerTests.cs`

- [ ] **步骤 1：编写失败的排程器测试**

测试数据：

| 工序 | 工作中心 | 时长 |
| --- | --- | --- |
| 10 | WC-CNC-01 | 60 分钟 |
| 20 | WC-ASSY-01 | 45 分钟 |

日历为 2026-06-01 UTC 08:00 至 16:00。预期排程先安排工序 10，并在工序 10 完成后安排工序 20；同一工作中心内不得发生重叠。

- [ ] **步骤 2：实现排程器**

`RuleScheduler` 按工单优先级、交期、工序顺序和工作中心最早可用时段排序。它返回不可变的 `ScheduleResult` 条目，其中包含 UTC 开始/结束时间戳和原因文本 `rule-sequenced`。

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore --filter FullyQualifiedName~RuleSchedulerTests
git add backend/services/Business/Mes
git commit -m "feat: add mes rule scheduling"
```

预期：测试在提交前通过。

## 任务 4：添加持久化、API、事件和权限

**文件：**

- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs`
- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/*.cs`
- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/*.cs`
- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/*.cs`
- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEvents/MesIntegrationEvents.cs`
- 新增：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- 新增：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesEndpointTests.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- 修改：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：配置 schema**

使用 `mes` schema。数据表包括 `work_orders`、`operation_tasks`、`production_reports`、`schedule_results`、`finished_goods_receipt_requests`。

- [ ] **步骤 2：添加路由**

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/mes/work-orders/from-suggestion` | `business.mes.work-orders.manage` |
| `POST /api/business/v1/mes/work-orders/{workOrderId}/release` | `business.mes.work-orders.manage` |
| `GET /api/business/v1/mes/work-orders` | `business.mes.work-orders.read` |
| `POST /api/business/v1/mes/operation-tasks/{operationTaskId}/reports` | `business.mes.reporting.write` |
| `GET /api/business/v1/mes/reports` | `business.mes.reporting.read` |
| `POST /api/business/v1/mes/schedules/run` | `business.mes.schedules.manage` |
| `GET /api/business/v1/mes/schedules/gantt` | `business.mes.schedules.read` |

- [ ] **步骤 3：运行测试并提交**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
git add backend/services/Business/Mes backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md
git commit -m "feat: expose mes execution api"
```

预期：测试在提交前通过。

## 任务 5：添加验证与就绪状态说明

**文件：**

- 新增：`scripts/verify-business-mes-execution-mvp.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：添加并运行验证**

运行：

```powershell
scripts/verify-business-mes-execution-mvp.ps1
git diff --check
```

预期：脚本运行 MES 领域层/Web 层测试，并以退出码 `0` 结束。

- [ ] **步骤 2：提交文档**

运行：

```powershell
git add scripts/verify-business-mes-execution-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record mes execution readiness"
```

## 自审清单

1. 工单引用 ProductEngineering 的 productionVersionId 值，且这些值可解析到已发布的 MBOM 和工艺路线版本。
2. 报工具有幂等性，并拒绝超量报工。
3. 规则排程具有确定性，并明确记录为 MVP 边界。
4. 完工入库是向 WMS 发起的请求，而不是写入 Inventory 库存余额。
