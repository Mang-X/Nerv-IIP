# 业务 WMS 执行 MVP 实施计划

> **面向智能体执行者：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**构建 WMS MVP，覆盖入库、上架、出库、拣货、装箱复核、盘点执行和 WCS 适配器任务边界。

**架构：**WMS 拥有仓储执行状态，而不拥有库存余额。入库和出库作业完成后，会发出事件或调用 Inventory，并使用幂等键过账库存移动。WCS 不作为独立系统实现；WMS 拥有适配器任务映射、外部任务身份、回调结果和重试诊断。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 集成事件、xUnit。

---

## 边界

1. WMS 不存储库存余额字段。
2. WMS 不拥有采购订单、销售订单或工单的业务状态。
3. WCS 适配器任务采用异步处理且可补偿；外部 WCS 不属于该事务。
4. 条码扫描可通过 BarcodeLabel 记录，但 WMS 保留自己的执行事实。

## 文件结构图

```text
backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/
  AggregatesModel/InboundOrderAggregate/InboundOrder.cs
  AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs
  AggregatesModel/WarehouseTaskAggregate/WarehouseTask.cs
  AggregatesModel/WcsTaskAggregate/WcsTask.cs
  AggregatesModel/CountExecutionAggregate/CountExecution.cs

backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/
  Application/Commands/CreateInboundOrderCommand.cs
  Application/Commands/CompleteInboundOrderCommand.cs
  Application/Commands/CreateOutboundOrderCommand.cs
  Application/Commands/CompleteOutboundOrderCommand.cs
  Application/Commands/DispatchWcsTaskCommand.cs
  Application/Commands/CompleteWcsTaskCommand.cs
  Application/IntegrationEvents/WmsIntegrationEvents.cs
  Endpoints/Wms/WmsEndpoints.cs
```

## 任务 1：搭建 WMS 服务脚手架

**文件：**

- 新增：`backend/services/Business/Wms/*`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建服务和测试**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Wms -o backend/services/Business/Wms --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Wms.Domain.Tests -o backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Wms.Web.Tests -o backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/Nerv.IIP.Business.Wms.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Nerv.IIP.Business.Wms.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj
```

- [ ] **步骤 2：提交脚手架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Wms
git commit -m "feat: scaffold wms service"
```

## 任务 2：实现入库与出库执行

**文件：**

- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/InboundOrderAggregate/InboundOrder.cs`
- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs`
- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/WarehouseTaskAggregate/WarehouseTask.cs`
- 新增：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WmsExecutionAggregateTests.cs`

- [ ] **步骤 1：编写失败的执行测试**

覆盖：

```csharp
var inbound = InboundOrder.Create("org-001", "env-dev", "purchase-receipt", "PR-001", new[] { InboundLine.Create("SKU-RM-1000", 19m) });
inbound.CreatePutawayTask("SKU-RM-1000", 19m, "A-01-01");
inbound.Complete(new[] { PutawayResult.Create("SKU-RM-1000", 19m, "A-01-01") }, "idem-in-001");

var outbound = OutboundOrder.Create("org-001", "env-dev", "sales-delivery", "DO-001", new[] { OutboundLine.Create("SKU-FG-1000", 2m) });
outbound.Pick("SKU-FG-1000", 2m, "FG-01-01", "LOT-001");
outbound.CompletePackReview("pack-ok", "idem-out-001");
```

断言完成操作必须提供幂等键，拣货数量不得超过请求数量，并且已完成的单据不可变。

- [ ] **步骤 2：实现事件**

创建 `InboundOrderCompletedDomainEvent`、`OutboundOrderCompletedDomainEvent`、`WarehouseTaskAssignedDomainEvent` 和 `CountExecutionCompletedDomainEvent`。

- [ ] **步骤 3：运行测试并提交**

运行：

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore
git add backend/services/Business/Wms
git commit -m "feat: add wms warehouse execution model"
```

预期：测试在提交前通过。

## 任务 3：实现 WCS 适配器边界

**文件：**

- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/WcsTaskAggregate/WcsTask.cs`
- 新增：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WcsTaskAggregateTests.cs`

- [ ] **步骤 1：编写失败的 WCS 测试**

覆盖：

```csharp
var task = WcsTask.Dispatch("org-001", "env-dev", "warehouse-task-001", "asrs", """{"source":"A-01-01","target":"STAGE-01"}""");
task.MarkCompleted("external-001", DateTimeOffset.UtcNow);
```

断言分派操作按仓库任务和适配器类型保持幂等，失败任务存储诊断代码和消息，并且已完成任务之后不能再标记为失败。

- [ ] **步骤 2：实现 WCS 事件**

创建 `WcsTaskDispatchedDomainEvent`、`WcsTaskCompletedDomainEvent` 和 `WcsTaskFailedDomainEvent`。

- [ ] **步骤 3：运行测试并提交**

运行：

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~WcsTaskAggregateTests
git add backend/services/Business/Wms
git commit -m "feat: add wms wcs adapter task boundary"
```

预期：测试在提交前通过。

## 任务 4：添加持久化、API、事件和权限

**文件：**

- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/ApplicationDbContext.cs`
- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/EntityConfigurations/*.cs`
- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/*.cs`
- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Queries/*.cs`
- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEvents/WmsIntegrationEvents.cs`
- 新增：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Endpoints/Wms/WmsEndpoints.cs`
- 新增：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsEndpointTests.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- 修改：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：配置 schema**

使用 `wms` schema。数据表包括 `inbound_orders`、`outbound_orders`、`warehouse_tasks`、`wcs_tasks`、`count_executions`。任何数据表都不得包含 `on_hand_quantity`、`available_quantity` 或 `stock_balance`。

- [ ] **步骤 2：添加路由**

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/wms/inbound-orders` | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/complete` | `business.wms.receipts.manage` |
| `GET /api/business/v1/wms/inbound-orders` | `business.wms.receipts.read` |
| `POST /api/business/v1/wms/outbound-orders` | `business.wms.shipments.manage` |
| `POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/complete` | `business.wms.shipments.manage` |
| `GET /api/business/v1/wms/outbound-orders` | `business.wms.shipments.read` |
| `POST /api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch` | `business.wms.automation.manage` |
| `POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/complete` | `business.wms.automation.manage` |

- [ ] **步骤 3：写入初始权限并运行测试**

运行：

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

预期：PASS。

- [ ] **步骤 4：提交 API 实现**

运行：

```powershell
git add backend/services/Business/Wms backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md
git commit -m "feat: expose wms execution api"
```

## 任务 5：添加验证与就绪状态说明

**文件：**

- 新增：`scripts/verify-business-wms-execution-mvp.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：添加验证脚本并运行**

运行：

```powershell
scripts/verify-business-wms-execution-mvp.ps1
git diff --check
```

预期：脚本运行全部 WMS 测试，并以退出码 `0` 结束。

- [ ] **步骤 2：提交文档**

运行：

```powershell
git add scripts/verify-business-wms-execution-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record wms execution readiness"
```

## 自审清单

1. WMS 只存储执行状态。
2. 已完成的入库/出库操作携带幂等键。
3. WCS 适配器失败可诊断且可补偿。
4. 库存余额仍只归 Inventory 所有。
