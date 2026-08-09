# Inventory MVP 实施计划

> **面向智能体执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**通过创建 Inventory 服务来实施 #131，覆盖库存地点、库存台账、库存移动、可用量查询和盘点调整。

**架构：**Inventory 是 `backend/services/Business/Inventory` 下新增的 CleanDDD 业务服务。它只拥有库存事实，并通过公开编码或 ID 引用 MasterData。WMS、ERP、MES 和 DemandPlanning 通过 API/事件使用 Inventory，绝不通过共享表访问。

**技术栈：**.NET 10、NetCorePal CleanDDD 模板、FastEndpoints、EF Core PostgreSQL、xUnit、CAP 风格的集成事件转换、`Nerv.IIP.Testing` 数据库模式约定辅助工具。

---

## 规格

以 `docs/superpowers/specs/2026-05-23-inventory-mvp-design.md` 作为本计划的领域契约。

## 文件

- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/Nerv.IIP.Business.Inventory.Domain.csproj`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Nerv.IIP.Business.Inventory.Infrastructure.csproj`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLocationAggregate/StockLocation.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockMovementAggregate/StockMovement.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountTask.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/DomainEvents/InventoryDomainEvents.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/ApplicationDbContext.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockLocationEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockLedgerEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockMovementEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockCountTaskEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Auth/InventoryPermissionCodes.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockLocations/CreateStockLocationCommand.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockMovements/PostStockMovementCommand.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CreateStockCountTaskCommand.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/GetStockAvailabilityQuery.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEvents/InventoryIntegrationEvents.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventConverters/InventoryIntegrationEventConverters.cs`
- 新建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Endpoints/Inventory/InventoryEndpoints.cs`
- 新建：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`
- 新建：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`
- 新建：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryIntegrationEventTests.cs`
- 新建：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventorySchemaConventionTests.cs`

由 #140 请求的共享文件：

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-inventory-mvp.ps1`

## 任务 1：在本地搭建 Inventory 服务骨架

- [ ] **步骤 1：创建服务项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Inventory -o backend/services/Business/Inventory --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Inventory.Domain.Tests -o backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Inventory.Web.Tests -o backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests --framework net10.0
```

预期：Inventory 的领域层、基础设施层、Web 层和测试项目均已存在。

- [ ] **步骤 2：移除模板演示代码**

删除模板中的演示端点、示例聚合、示例数据库迁移、演示 SignalR Hub 和演示测试。验证没有文件包含 `OrderAggregate`、`DeliverRecord`、`LoginEndpoint`、`ChatHub` 或 `LockEndpoint`。

运行：

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/Inventory
```

预期：没有匹配项。

## 任务 2：实施领域模型

- [ ] **步骤 1：编写聚合测试**

创建 `InventoryAggregateTests.cs`，测试以下场景：

1. 过账入库移动会增加在手数量。
2. 过账出库移动会减少在手数量。
3. 使用相同载荷的重复幂等键会返回已有移动。
4. 使用不同载荷的重复幂等键会被拒绝。
5. 会导致在手数量变为负数的出库移动会被拒绝。
6. 盘点调整会创建调整移动并更新台账数量。

运行：

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore
```

实施前预期：由于 Inventory 聚合尚不存在，编译失败。

- [ ] **步骤 2：实施聚合根**

实施“文件”一节列出的聚合文件。必须提供以下方法：

1. `StockLocation.CreateOrUpdate(...)`
2. `StockLedger.ApplyMovement(...)`
3. `StockMovement.Post(...)`
4. `StockCountTask.Create(...)`
5. `StockCountTask.ConfirmAdjustment(...)`

实体 ID 使用 `Guid.CreateVersion7()`，并保持所有方法具有确定性，以便进行单元测试。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore
```

预期：Inventory 领域测试通过。

## 任务 3：添加持久化与事件

- [ ] **步骤 1：配置 DbContext**

创建 `ApplicationDbContext.cs` 和实体配置，使用 `inventory` 数据库模式。配置 `MigrationsHistoryTable("__EFMigrationsHistory", "inventory")`。

- [ ] **步骤 2：生成数据库迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialInventorySchema --project backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Nerv.IIP.Business.Inventory.Infrastructure.csproj --startup-project backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj --output-dir Migrations
```

预期：已创建 Inventory 初始数据库迁移。

- [ ] **步骤 3：添加事件转换器测试**

创建 `InventoryIntegrationEventTests.cs` 并验证以下事件名称：

1. `inventory.StockMovementPosted`
2. `inventory.StockCountVarianceConfirmed`
3. `inventory.StockAvailabilityChanged`

运行：

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore --filter FullyQualifiedName~InventoryIntegrationEventTests
```

预期：事件转换器测试通过。

## 任务 4：添加 API 接口

- [ ] **步骤 1：添加端点契约测试**

创建 `InventoryEndpointContractTests.cs`，覆盖以下场景：

1. 所有非健康检查端点都必须进行内部授权。
2. `POST /api/inventory/v1/locations` 创建库存地点。
3. `POST /api/inventory/v1/movements` 过账库存移动并返回移动 ID。
4. `GET /api/inventory/v1/availability` 返回在手量、预留量和可用量。
5. `POST /api/inventory/v1/count-tasks` 创建盘点任务。
6. `POST /api/inventory/v1/count-tasks/{countTaskId}/adjustments` 过账调整。
7. OpenAPI 操作 ID 保持稳定。

- [ ] **步骤 2：实施命令、查询和 FastEndpoints**

实施“文件”一节列出的文件。内部 API 使用 Inventory 规格中的权限代码和 `[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]`。

- [ ] **步骤 3：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore
```

预期：Inventory Web 层测试通过。

## 任务 5：向 #140 移交共享变更

- [ ] **步骤 1：记录共享变更**

在本次会话的 PR 正文中包含以下内容：

```markdown
## Shared Changes Needed

- Add Inventory projects/tests to `backend/Nerv.IIP.sln`.
- Register Inventory in AppHost.
- Add Inventory permissions to IAM seed and `authorization-matrix.md`.
- Add `inventory` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-inventory-mvp.ps1`.
```

- [ ] **步骤 2：运行最终聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。
