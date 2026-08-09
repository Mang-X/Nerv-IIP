# WMS 执行 MVP 实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**实施 #136，创建 WMS，覆盖入库、出库、上架、拣货、盘点执行和 WCS 适配器任务边界。

**架构：**WMS 是位于 `backend/services/Business/Wms` 下的 CleanDDD 业务服务。它拥有仓库执行状态和库存移动请求元数据，但 Inventory 仍是唯一拥有库存台账和移动事实的服务。WMS 通过公开 API/事件边界进行集成。

**技术栈：**.NET 10、NetCorePal CleanDDD 模板、FastEndpoints、EF Core PostgreSQL、xUnit、ADR 0011 集成事件转换、`Nerv.IIP.Testing` schema 约定辅助工具。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-wms-execution-mvp-design.md` 作为本计划的领域契约。

## 文件

- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/Nerv.IIP.Business.Wms.Domain.csproj`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Nerv.IIP.Business.Wms.Infrastructure.csproj`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/InboundOrderAggregate/InboundOrder.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/WarehouseTaskAggregate/WarehouseTask.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/CountExecutionAggregate/CountExecution.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/WcsTaskAggregate/WcsTask.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/InventoryMovementRequestAggregate/InventoryMovementRequest.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/DomainEvents/WmsDomainEvents.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Auth/WmsPermissionCodes.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Inventory/IInventoryMovementClient.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/*.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Queries/*.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEvents/WmsIntegrationEvents.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventConverters/WmsIntegrationEventConverters.cs`
- 创建：`backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Endpoints/Wms/WmsEndpoints.cs`
- 创建：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WmsExecutionAggregateTests.cs`
- 创建：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WcsTaskAggregateTests.cs`
- 创建：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsEndpointContractTests.cs`
- 创建：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsInventoryBoundaryTests.cs`
- 创建：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsIntegrationEventTests.cs`
- 创建：`backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsSchemaConventionTests.cs`

WAVE2-INTEG 请求的共享文件：

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-wms-execution-mvp.ps1`

## Task 1：在本地搭建 WMS 服务脚手架

- [ ] **步骤 1：创建服务项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Wms -o backend/services/Business/Wms --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Wms.Domain.Tests -o backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Wms.Web.Tests -o backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests --framework net10.0
```

- [ ] **步骤 2：移除模板演示代码**

运行：

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/Wms
```

预期：没有匹配项。

## Task 2：实施入库与出库执行

- [ ] **步骤 1：编写预期失败的执行测试**

覆盖：

1. 使用来源单据引用和明细行创建入库单。
2. 上架任务数量不能超过入库明细行数量。
3. 完成入库需要幂等键，并创建移动请求元数据。
4. 使用来源单据引用和明细行创建出库单。
5. 拣货数量不能超过出库明细行数量。
6. 完成装箱复核需要幂等键，并创建移动请求元数据。
7. 已完成的入库单/出库单不可变。

- [ ] **步骤 2：实施聚合根和领域事件**

实施入库单、出库单、仓库任务、盘点执行和库存移动请求聚合。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore
```

预期：WMS 领域测试通过。

## Task 3：实施 WCS 适配器边界

- [ ] **步骤 1：编写预期失败的 WCS 测试**

覆盖：

1. 按仓库任务和适配器类型保证派发幂等。
2. 已完成的任务此后不能转为失败。
3. 失败任务保存诊断代码和消息。
4. 重试增加尝试次数，但不改变原始仓库任务引用。

- [ ] **步骤 2：实施 WCS 聚合和事件**

实施 `WcsTask` 及派发、完成和失败状态的事件。

- [ ] **步骤 3：运行 WCS 测试**

运行：

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~WcsTaskAggregateTests
```

预期：WCS 测试通过。

## Task 4：添加持久化、API 和 Inventory 边界

- [ ] **步骤 1：配置 DbContext**

使用 `wms` schema 和 migration 历史表 `wms.__EFMigrationsHistory`。任何表都不得包含 `on_hand_quantity`、`available_quantity` 或 `stock_balance`。

- [ ] **步骤 2：生成 migration**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialWmsSchema --project backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Nerv.IIP.Business.Wms.Infrastructure.csproj --startup-project backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj --output-dir Migrations
```

- [ ] **步骤 3：添加 endpoint 和库存边界测试**

创建测试，覆盖路由结构、权限代码、operation ID、库存移动请求载荷和幂等键。

- [ ] **步骤 4：实施 command、query 和 FastEndpoints**

在 `Endpoints/Wms` 下实施规格中的 endpoint。将 Inventory 过账封装在 `IInventoryMovementClient` 后，使 Web 测试能够使用假客户端。

- [ ] **步骤 5：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --no-restore
```

预期：WMS Web 测试通过。

## Task 5：添加事件和 Schema 防护

- [ ] **步骤 1：添加事件转换器测试**

验证事件名：

1. `wms.InboundOrderCompleted`
2. `wms.OutboundOrderCompleted`
3. `wms.CountExecutionCompleted`
4. `wms.WcsTaskDispatched`
5. `wms.WcsTaskFailed`

- [ ] **步骤 2：添加 schema 约定测试**

除标准 schema 约定断言外，再加入一项 WMS 专用断言，确保任何已映射表名/列名都不会暗示 WMS 拥有库存余额。

## Task 6：向 WAVE2-INTEG 移交共享变更

- [ ] **步骤 1：记录共享变更**

在 PR/会话摘要中包含：

```markdown
## Shared Changes Needed

- Add WMS projects/tests to `backend/Nerv.IIP.sln`.
- Register WMS in AppHost.
- Add WMS permissions to IAM seed and `authorization-matrix.md`.
- Add `wms` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-wms-execution-mvp.ps1`.
- Add Inventory base URL environment variable if the first WMS adapter calls Inventory over HTTP.
```

- [ ] **步骤 2：运行最终聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。
