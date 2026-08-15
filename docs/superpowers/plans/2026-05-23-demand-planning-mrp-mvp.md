# DemandPlanning MPS/MRP MVP 实施计划

> **供代理执行者使用：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 子技能，逐项任务实施本计划。步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：**通过创建 DemandPlanning 实现 #128，使其成为需求来源、MPS、按日分桶的确定性 MRP 运行、计划采购/工单建议和需求追溯的事实来源。

**架构：**DemandPlanning 是位于 `backend/services/Business/DemandPlanning` 下的 CleanDDD 业务服务。它通过公共 API/契约适配器消费 ProductEngineering 和 Inventory，并且只存储计划快照。它不创建 ERP 采购单据、MES 工单或 Inventory 库存移动。

**技术栈：**.NET 10、NetCorePal CleanDDD 模板、FastEndpoints、EF Core PostgreSQL、xUnit、ADR 0011 集成事件转换、`Nerv.IIP.Testing` schema 约定辅助工具。

---

## 规格

使用 `docs/superpowers/specs/2026-05-23-demand-planning-mrp-mvp-design.md` 作为本计划的领域契约。

## 文件

- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/Nerv.IIP.Business.DemandPlanning.Domain.csproj`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Nerv.IIP.Business.DemandPlanning.Infrastructure.csproj`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/DemandSourceAggregate/DemandSource.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/MasterProductionScheduleAggregate/MasterProductionSchedule.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/MrpRunAggregate/MrpRun.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/PlanningSuggestionAggregate/PlanningSuggestion.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/DomainEvents/DemandPlanningDomainEvents.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Auth/DemandPlanningPermissionCodes.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/*.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Queries/*.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEvents/DemandPlanningIntegrationEvents.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEventConverters/DemandPlanningIntegrationEventConverters.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Endpoints/Planning/PlanningEndpoints.cs`
- 创建：`backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/DemandPlanningAggregateTests.cs`
- 创建：`backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/MrpCalculatorTests.cs`
- 创建：`backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningEndpointContractTests.cs`
- 创建：`backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningIntegrationEventTests.cs`
- 创建：`backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningSchemaConventionTests.cs`

请求 WAVE2-INTEG 处理的共享文件：

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-demand-planning-mvp.ps1`
- `scripts/verify-business-wave2-execution.ps1`

## 任务 1：在本地搭建 DemandPlanning 服务骨架

- [ ] **步骤 1：创建服务项目**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.DemandPlanning -o backend/services/Business/DemandPlanning --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.DemandPlanning.Domain.Tests -o backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.DemandPlanning.Web.Tests -o backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests --framework net10.0
```

预期：DemandPlanning Domain、Infrastructure、Web 和测试项目均已存在。

- [ ] **步骤 2：移除模板演示代码**

移除模板演示端点、示例聚合、示例迁移、演示用 SignalR hub 和演示测试。验证没有文件包含 `OrderAggregate`、`DeliverRecord`、`LoginEndpoint`、`ChatHub` 或 `LockEndpoint`。

运行：

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/DemandPlanning
```

预期：没有匹配项。

## 任务 2：实现计划领域和 MRP 计算器

- [ ] **步骤 1：编写失败的领域测试**

创建 `DemandPlanningAggregateTests.cs`，覆盖：

1. 创建需求来源必须提供组织、环境、SKU、数量和到期日期。
2. MRP 运行可以携带输入快照元数据，从已创建流转到运行中，再流转到已完成。
3. 计划建议只能接受一次，且在已拒绝或已关闭后不能接受。
4. 需求追溯链接保留需求来源和版本引用。

- [ ] **步骤 2：编写确定性 MRP 计算器测试**

使用规格中的测试夹具创建 `MrpCalculatorTests.cs`：

1. 需求 `SKU-FG-1000`，数量 `10`，到期日期 `2026-06-01`。
2. 成品可用量 `2`。
3. MBOM 行 `SKU-FG-1000 -> SKU-RM-1000`，数量 `3`。
4. 组件可用量 `5`。
5. 预期工单建议数量 `8`。
6. 预期采购建议数量 `19`。

- [ ] **步骤 3：实现聚合根和纯计算器**

实现聚合文件和 `MrpCalculator`。保持计算器的确定性，且不包含数据库或服务调用。输入适配器应在调用计算器前准备不可变记录。

- [ ] **步骤 4：运行聚焦测试**

运行：

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MrpCalculatorTests
```

预期：领域测试和计算器测试通过。

## 任务 3：添加持久化和事件

- [ ] **步骤 1：配置 DbContext 和 schema**

使用 schema `demand_planning` 和以下表：

1. `demand_sources`
2. `master_production_schedules`
3. `mrp_runs`
4. `planning_suggestions`
5. `mrp_pegging_links`

将迁移历史表配置为 `demand_planning.__EFMigrationsHistory`。

- [ ] **步骤 2：生成迁移**

运行：

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialDemandPlanningSchema --project backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Nerv.IIP.Business.DemandPlanning.Infrastructure.csproj --startup-project backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj --output-dir Migrations
```

- [ ] **步骤 3：添加事件转换器测试**

验证事件名称：

1. `demandPlanning.MrpRunCompleted`
2. `demandPlanning.PlannedPurchaseSuggested`
3. `demandPlanning.PlannedWorkOrderSuggested`
4. `demandPlanning.PlanningSuggestionAccepted`

运行：

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~DemandPlanningIntegrationEventTests
```

预期：事件转换器测试通过。

## 任务 4：添加 API 接口面

- [ ] **步骤 1：添加端点契约测试**

创建 `DemandPlanningEndpointContractTests.cs`，覆盖：

1. 所有端点都要求预期的权限代码。
2. 路由形状和操作 ID 保持稳定。
3. 创建需求来源会返回需求来源 ID。
4. MRP 运行通过测试夹具支持的 ProductEngineering/Inventory 适配器创建建议。
5. 需求追溯端点返回需求、来源和版本引用。
6. 对同一下游引用接受建议时具有幂等性，并拒绝相互冲突的重复请求。

- [ ] **步骤 2：实现命令、查询和 FastEndpoints**

实现规格中的端点。对 ProductEngineering 和 Inventory 的访问应封装在适配器之后，以便在测试中替换为测试夹具实现。

- [ ] **步骤 3：运行 Web 测试**

运行：

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore
```

预期：DemandPlanning Web 测试通过。

## 任务 5：向 WAVE2-INTEG 交接共享变更

- [ ] **步骤 1：记录共享变更**

在 PR/会话摘要中包含：

```markdown
## Shared Changes Needed

- Add DemandPlanning projects/tests to `backend/Nerv.IIP.sln`.
- Register DemandPlanning in AppHost with a PostgreSQL database and InMemory messaging by default.
- Add DemandPlanning permissions to IAM seed and `authorization-matrix.md`.
- Add `demand_planning` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-demand-planning-mvp.ps1`.
```

- [ ] **步骤 2：运行最终聚焦验证**

运行：

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore
```

预期：两条命令均通过。
