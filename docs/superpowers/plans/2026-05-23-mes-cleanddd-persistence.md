# MES CleanDDD 持久化实施计划

> **面向智能体执行者：**必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**通过将 MES 从仅存在于 Web 层的内存排程状态迁移到 CleanDDD 领域层、基础设施层和 PostgreSQL 持久化来实施 #135，同时保留当前排程、紧急订单和重新排程行为。

**架构：**这是现有 MES 行为的迁移计划。将当前 Web 端点和测试保留为行为契约，引入领域聚合以持久保存工单和执行事实，再以基于仓储的应用服务替换 `MesPlanningStore`。MES 只能通过公开 ID 或事件载荷引用 ProductEngineering 的 ProductionVersion、Inventory、WMS、Quality、Telemetry 和 Maintenance。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、EF Core PostgreSQL、xUnit、`Nerv.IIP.Testing` 数据库模式约定辅助工具。

---

## 当前代码事实

现有 MES 文件包括：

1. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Planning/MesPlanningStore.cs`
2. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Scheduling/RuleScheduler.cs`
3. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/CreateRushWorkOrderCommand.cs`
4. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Schedules/RescheduleCommand.cs`
5. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
6. `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RuleSchedulerTests.cs`
7. `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RushWorkOrderCommandTests.cs`
8. `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RescheduleCommandTests.cs`

除非现有测试证明行为错误，否则不得从头重写排程器。

## 文件

- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/Nerv.IIP.Business.Mes.Domain.csproj`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Nerv.IIP.Business.Mes.Infrastructure.csproj`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/WorkOrderAggregate/WorkOrder.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReport.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ScheduleAggregate/ScheduleResult.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/FinishedGoodsReceiptRequestAggregate/FinishedGoodsReceiptRequest.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/DomainEvents/MesDomainEvents.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/WorkOrderEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/OperationTaskEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/ProductionReportEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/ScheduleResultEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/FinishedGoodsReceiptRequestEntityTypeConfiguration.cs`
- 新建：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/MesPersistenceServiceCollectionExtensions.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Planning/MesPlanningStore.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/CreateRushWorkOrderCommand.cs`
- 修改：`backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Schedules/RescheduleCommand.cs`
- 新建：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/MesAggregateTests.cs`
- 新建：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj`
- 新建：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesPersistenceContractTests.cs`
- 新建：`backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesSchemaConventionTests.cs`

由 #140 请求的共享文件：

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-mes-execution-mvp.ps1`

## 任务 1：冻结当前 Web 行为

- [ ] **步骤 1：运行当前 MES 测试**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

预期：当前 MES Web 层测试通过。如果测试失败，必须先记录失败，再更改行为。

- [ ] **步骤 2：添加持久化回归测试**

创建 `MesPersistenceContractTests.cs`，断言：

1. 启用持久化后，重新创建服务作用域不会丢失紧急工单。
2. 重新排程使用已持久化的工单和排程事实。
3. Maintenance 资产不可用事件会更新已持久化的排程约束。

实施前预期：由于持久化类型尚不存在，编译失败或测试失败。

## 任务 2：添加领域层项目和聚合

- [ ] **步骤 1：创建领域层和基础设施层项目**

运行：

```powershell
dotnet new classlib -n Nerv.IIP.Business.Mes.Domain -o backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain --framework net10.0
dotnet new classlib -n Nerv.IIP.Business.Mes.Infrastructure -o backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Mes.Domain.Tests -o backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests --framework net10.0
```

预期：项目已存在。添加 Web 层到领域层和基础设施层的项目引用，以及基础设施层到领域层的项目引用。

- [ ] **步骤 2：编写聚合测试**

创建 `MesAggregateTests.cs`，覆盖以下场景：

1. WorkOrder 引用一个 ProductEngineering `productionVersionId`。
2. WorkOrder 下达时根据工艺路线步骤快照创建工序任务。
3. 对于相同的工单和约束，规则排程结果具有确定性。
4. ProductionReport 记录合格数量、报废数量和工序完工情况。
5. FinishedGoodsReceiptRequest 引用工单、SKU、数量和 UOM，但不过账 Inventory 移动。

- [ ] **步骤 3：实施领域聚合**

实施“文件”一节列出的聚合文件。新 ID 使用 `Guid.CreateVersion7()`。保持排程计算具有确定性且无副作用。

- [ ] **步骤 4：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj --no-restore
```

预期：MES 领域测试通过。

## 任务 3：添加 PostgreSQL 持久化

- [ ] **步骤 1：实施 DbContext 和映射**

创建 `ApplicationDbContext.cs`，使用 `mes` 数据库模式和 `MigrationsHistoryTable("__EFMigrationsHistory", "mes")`。为每个持久化业务字段添加表注释和列注释。

- [ ] **步骤 2：生成数据库迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialMesExecutionSchema --project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Nerv.IIP.Business.Mes.Infrastructure.csproj --startup-project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj --output-dir Migrations
```

预期：已创建 MES 初始数据库迁移。

- [ ] **步骤 3：添加数据库模式约定测试**

创建 `MesSchemaConventionTests.cs`，并使用现有的 `Nerv.IIP.Testing` 数据库模式约定辅助工具。

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MesSchemaConventionTests
```

预期：数据库模式约定测试通过。

## 任务 4：用基于仓储的服务替换内存存储

- [ ] **步骤 1：保留当前 API 请求与响应契约**

除非用已有记录的理由更新现有契约测试，否则不得更改当前 MES 路由路径或响应结构。

- [ ] **步骤 2：调整命令处理器**

修改紧急工单和重新排程命令，使其使用已持久化的工单、排程和约束事实。保留 `RuleScheduler` 作为确定性排程组件。

- [ ] **步骤 3：运行 Web 回归测试**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

预期：现有 MES Web 层测试和新增持久化测试均通过。

## 任务 5：向 #140 移交共享变更

- [ ] **步骤 1：记录共享变更**

在本次会话的 PR 正文中包含以下内容：

```markdown
## Shared Changes Needed

- Add MES Domain and Infrastructure projects/tests to `backend/Nerv.IIP.sln`.
- Register MES in AppHost after Web project compiles.
- Add MES schema entries to `database-schema-catalog.md`.
- Add or refresh `scripts/verify-business-mes-execution-mvp.ps1`.
- Update readiness to say MES has durable Domain/Infrastructure persistence after focused tests pass.
```

- [ ] **步骤 2：运行最终聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。
