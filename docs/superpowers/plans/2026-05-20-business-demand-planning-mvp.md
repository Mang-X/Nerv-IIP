# 业务需求计划 MVP 实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**构建轻量级 DemandPlanning，涵盖需求来源、MPS、MRP 运行、计划采购建议、计划工单建议和需求追溯（pegging）。

**架构：**DemandPlanning 通过 API、契约或导入的快照消费已发布的工程版本和库存可用量。对于计划工单，它按 SKU、到期日和批量解析 ProductEngineering ProductionVersion，而不是直接选择未经聚合解析的 MBOM/路线 ID。它拥有计划运行和建议，但不创建正式采购订单、正式工单或库存移动。MRP 首先采用确定性的日时间桶计算，使首个纵切无需引入 APS 的复杂性也可测试。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 集成事件、xUnit。

---

## MasterData 重对齐依赖

执行本计划前，先完成 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`。DemandPlanning 必须消费 MasterData 中 SKU、UOM 换算、工作中心、工作日历、资源能力及设备/资源可用性基线的引用快照。只有在 MasterData 字段矩阵裁定某项不属于 SKU 或资源主数据事实后，计划域才可添加计划专用默认值或参数。

## 边界

1. 本纵切不包含 APS 优化器或约束求解器。
2. 不直接写入 ERP、MES 或 Inventory 表。
3. MVP 的 MRP 时间桶按日划分。
4. 在 ERP 或 MES 接受建议之前，建议属于计划事实。
5. 不得在 DemandPlanning 中创建与现有主数据平行的 SKU、UOM、工作中心、日历或设备事实。

## 文件结构图

```text
backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/
  DemandPlanningFacts.cs
  AggregatesModel/DemandSourceAggregate/DemandSource.cs
  AggregatesModel/MasterProductionScheduleAggregate/MasterProductionSchedule.cs
  AggregatesModel/MrpRunAggregate/MrpRun.cs
  AggregatesModel/PlanningSuggestionAggregate/PlanningSuggestion.cs
  DomainEvents/DemandPlanningDomainEvents.cs

backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/
  Application/Commands/CreateDemandSourceCommand.cs
  Application/Commands/RunMrpCommand.cs
  Application/Commands/AcceptPlanningSuggestionCommand.cs
  Application/Queries/ListMrpRunsQuery.cs
  Application/Queries/GetMrpPeggingQuery.cs
  Application/Queries/ListPlanningSuggestionsQuery.cs
  Application/Planning/MrpCalculator.cs
  Application/IntegrationEvents/DemandPlanningIntegrationEvents.cs
  Endpoints/Planning/PlanningEndpoints.cs
```

## 任务 1：搭建 DemandPlanning 服务骨架

**文件：**

- 创建：`backend/services/Business/DemandPlanning/*`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建项目和测试**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.DemandPlanning -o backend/services/Business/DemandPlanning --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.DemandPlanning.Domain.Tests -o backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.DemandPlanning.Web.Tests -o backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/Nerv.IIP.Business.DemandPlanning.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Nerv.IIP.Business.DemandPlanning.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj
```

- [ ] **步骤 2：提交服务骨架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/DemandPlanning
git commit -m "feat: scaffold demand planning service"
```

## 任务 2：添加计划领域模型和 MRP 计算器

**文件：**

- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/DemandSourceAggregate/DemandSource.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/MasterProductionScheduleAggregate/MasterProductionSchedule.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/MrpRunAggregate/MrpRun.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/PlanningSuggestionAggregate/PlanningSuggestion.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs`
- 创建：`backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/DemandPlanningAggregateTests.cs`
- 创建：`backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/MrpCalculatorTests.cs`

- [ ] **步骤 1：编写预期失败的测试**

MRP 计算器测试必须使用以下测试夹具：

| 输入 | 值 |
| --- | --- |
| 需求 | SKU-FG-1000，数量 10，到期日 2026-06-01 |
| 现有库存 | SKU-FG-1000，数量 2 |
| MBOM | SKU-FG-1000 需要 SKU-RM-1000，数量 3 |
| 现有物料库存 | SKU-RM-1000，数量 5 |

预期建议：

| 建议 | 数量 |
| --- | --- |
| SKU-FG-1000 的计划工单 | 8 |
| SKU-RM-1000 的计划采购 | 19 |

断言需求追溯关系将两项建议都关联回需求来源和输入版本引用。

- [ ] **步骤 2：实现按日时间桶计算的 MRP**

`MrpCalculator` 接受以下不可变输入记录：

```csharp
public sealed record MrpDemandInput(string DemandSourceId, string SkuCode, decimal Quantity, DateOnly DueDate);
public sealed record MrpInventoryInput(string SkuCode, decimal AvailableQuantity);
public sealed record MrpBomInput(string ParentSkuCode, string ComponentSkuCode, decimal QuantityPerParent, string VersionId);
public sealed record MrpSuggestionResult(string SuggestionType, string SkuCode, decimal Quantity, DateOnly DueDate, string? ProductionVersionId, IReadOnlyCollection<string> PeggingRefs);
```

计算器先扣减可用成品，再按净生产数量展开组件需求，随后扣减组件库存，并且只返回净需求为正数的建议。

- [ ] **步骤 3：运行测试并提交**

运行：

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MrpCalculatorTests
git add backend/services/Business/DemandPlanning
git commit -m "feat: add deterministic demand planning model"
```

预期：提交前测试通过。

## 任务 3：添加持久化、事件和 API

**文件：**

- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEvents/DemandPlanningIntegrationEvents.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/*.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Queries/*.cs`
- 创建：`backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Endpoints/Planning/PlanningEndpoints.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- 修改：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：配置 schema**

使用 schema `demand_planning`，以及表 `demand_sources`、`master_production_schedules`、`mrp_runs`、`planning_suggestions`、`mrp_pegging_links`。

- [ ] **步骤 2：实现集成事件**

创建以下事件：

```csharp
public sealed record MrpRunCompletedIntegrationEvent(string RunId, DateOnly HorizonStart, DateOnly HorizonEnd, int SuggestionCount);
public sealed record PlannedPurchaseSuggestedIntegrationEvent(string SuggestionId, string SkuCode, decimal Quantity, DateOnly DueDate, IReadOnlyCollection<string> PeggingRefs);
public sealed record PlannedWorkOrderSuggestedIntegrationEvent(string SuggestionId, string SkuCode, decimal Quantity, DateOnly DueDate, string ProductionVersionId, IReadOnlyCollection<string> VersionRefs);
```

- [ ] **步骤 3：添加路由**

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/planning/demands` | `business.planning.demands.manage` |
| `GET /api/business/v1/planning/demands` | `business.planning.demands.read` |
| `POST /api/business/v1/planning/mrp-runs` | `business.planning.mrp.run` |
| `GET /api/business/v1/planning/mrp-runs` | `business.planning.mrp.read` |
| `GET /api/business/v1/planning/mrp-runs/{runId}/pegging` | `business.planning.mrp.read` |
| `GET /api/business/v1/planning/suggestions` | `business.planning.mrp.read` |
| `POST /api/business/v1/planning/suggestions/{suggestionId}/accept` | `business.planning.suggestions.manage` |

- [ ] **步骤 4：写入初始权限数据并运行测试**

运行：

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

预期：通过。

- [ ] **步骤 5：提交 API**

运行：

```powershell
git add backend/services/Business/DemandPlanning backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md
git commit -m "feat: expose demand planning api"
```

## 任务 4：添加验证与就绪状态说明

**文件：**

- 创建：`scripts/verify-business-demand-planning-mvp.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：添加验证脚本**

该脚本运行全部 DemandPlanning 测试；如果任何建议数量与确定性测试夹具不同，脚本必须失败。

- [ ] **步骤 2：运行最终验证**

运行：

```powershell
scripts/verify-business-demand-planning-mvp.ps1
git diff --check
```

预期：两条命令的退出码均为 `0`。

- [ ] **步骤 3：提交文档**

运行：

```powershell
git add scripts/verify-business-demand-planning-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record demand planning readiness"
```

## 自审清单

1. MRP 建议可通过需求追溯关系解释。
2. DemandPlanning 不创建正式 ERP 或 MES 单据。
3. 文档已将日时间桶规则明确为 MVP 的计算边界。
4. 权限和 operation ID 保持稳定。
