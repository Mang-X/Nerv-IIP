# DemandPlanning MRP 缺口实施计划

> **供代理执行者使用：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**为 DemandPlanning 实施 issue #409 的 MRP 净需求强化。

**架构：**MRP 保持为由不可变快照提供输入的纯计算单元。DemandPlanning 存储计算结果和建议下达日期，但所有上游业务事实仍由 ProductEngineering、Inventory、ERP、MES 和 MasterData 拥有。

**技术栈：**.NET 10、FastEndpoints、EF Core PostgreSQL、xUnit、NetCorePal CleanDDD 模式。

---

## 文件

- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/RunMrpCommand.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/PlanningSuggestionAggregate/PlanningSuggestion.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/PlanningSuggestionEntityTypeConfiguration.cs`
- 添加： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/*_AddPlanningSuggestionReleaseDate.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Queries/DemandPlanningQueries.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEvents/DemandPlanningIntegrationEvents.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEventConverters/DemandPlanningIntegrationEventConverters.cs`
- 修改： `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Program.cs`
- 修改： `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/MrpCalculatorTests.cs`
- 修改： `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/PlanningInputAdapterTests.cs`
- 修改： `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningEndpointContractTests.cs`
- 修改： `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/DemandPlanningAggregateTests.cs`
- 修改： `docs/architecture/database-schema-catalog.md`
- 修改： `docs/architecture/implementation-readiness.md`

## Task 1：红灯测试

- [ ] 为计划收货、多级 BOM、下达日期提前期、日桶批量规则和安全库存添加计算器测试。
- [ ] 添加适配器测试，证明 ProductEngineering 批量值和 ERP 采购订单计划收货会进入快照。
- [ ] 运行 `dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MrpCalculatorTests`，确认新测试因缺失行为而失败。

## Task 2：计算器与快照实施

- [ ] 扩展 `MrpCalculationInput`、`ProductionVersionSnapshot` 和快照结果记录。
- [ ] 实现包含计划收货和安全库存的分桶净额计算。
- [ ] 根据生产版本可用性实现带自制/外购拆分的递归 BOM 展开。
- [ ] 实现下达日期偏移以及 L4L/min/max/multiple 批量规则。
- [ ] 运行聚焦的计算器测试，并保持原有确定性夹具通过。

## Task 3：持久化/API 下达日期

- [ ] 在 `PlanningSuggestion`、工厂创建、EF 配置、查询响应和集成事件载荷中添加 `ReleaseDate`。
- [ ] 为 `planning_suggestions.release_date` 生成或手工维护 EF migration 和模型快照。
- [ ] 更新 `ReleaseDate` 的聚合与 endpoint 契约测试。
- [ ] 运行 DemandPlanning Domain/Web 聚焦测试。

## Task 4：上游适配器接线

- [ ] 在 `ProductionVersionSnapshot` 中保留 ProductEngineering 的 `LotSizeMin` 和 `LotSizeMax`。
- [ ] 添加 ERP 采购订单计划收货客户端，使用未结采购订单行的剩余数量。
- [ ] 在 `Program.cs` 中使用 `Erp:BaseUrl` 注册 ERP 客户端。
- [ ] 由于当前 MES 工单列表缺少 UOM，继续将 MES 计划收货记录为待处理事项。

## Task 5：文档与验证

- [ ] 更新数据库 schema 目录和就绪清单，记录 `release_date` 以及仍存在的 MES 计划收货限制。
- [ ] 对 DemandPlanning Domain 和 Web 项目运行 `dotnet test`。
- [ ] 若脚本可用且环境未造成阻塞，运行 `scripts/verify-business-demand-planning-mrp-mvp.ps1`。
- [ ] 运行 `git diff --check`。
- [ ] 提交并推送 `codex/issue-409-demand-planning-mrp-gap`，然后创建包含 `Closes #409` 的 PR。
