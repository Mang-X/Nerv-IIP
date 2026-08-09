# Issue 414 MES 业务缺口实施计划

> **供代理执行者使用：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**通过生命周期状态、公开集成事件、NCR 处置消费和谱系字段，闭合 issue #414 中主要的 MES 后端业务循环缺口。

**架构：**MES 仍是执行事实的拥有者，并通过公开契约和集成事件与 Inventory、Quality 和 WMS 通信。Inventory 拥有库存过账，Quality 拥有 NCR 生命周期，WMS 拥有仓库执行；MES 仅记录请求/意图和本地执行状态。

**技术栈：**.NET 10、CleanDDD、EF Core、FastEndpoints、CAP 集成事件转换器、xUnit。

---

## 文件

- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/DomainEvents/MesDomainEvents.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/WorkOrderAggregate/WorkOrder.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReport.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReportMaterialConsumption.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/MaterialSupplyAggregate/MaterialIssueRequest.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/FinishedGoodsReceiptRequestAggregate/FinishedGoodsReceiptRequest.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/QualityAggregate/DefectRecord.cs`
- 创建： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs`
- 创建： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/MesProductionQueries.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/*.cs`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/*`
- 修改： `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj`
- 修改： `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/MesAggregateTests.cs`
- 创建： `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesIntegrationEventTests.cs`
- 创建： `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesQualityDispositionConsumerTests.cs`
- 修改： `docs/architecture/database-schema-catalog.md`
- 修改： `docs/architecture/implementation-readiness.md`

## 任务

- [ ] 为工单进度、挂起/取消/关闭、报工谱系和缺陷处置添加失败的 MES 领域测试。
- [ ] 实现最小领域字段、状态转换和领域事件。
- [ ] 为生产消耗、成品收货、物料发放和缺陷移交事件添加失败的转换器测试。
- [ ] 实现 MES 集成事件转换器，并添加 Inventory/Quality 契约引用。
- [ ] 添加失败的 Quality 处置消费者测试，然后实现幂等消费者。
- [ ] 更新命令处理器以传递产出批次/序列号、返工/报废原因，并通过领域方法发出聚合事件。
- [ ] 更新 EF 映射，并为新的 MES 字段添加 migration。
- [ ] 更新可追溯性/读取模型，使其返回真实的产出批次/序列号，不构造虚假数据。
- [ ] 更新就绪清单/schema 目录文档。
- [ ] 运行聚焦的 MES 领域/Web 测试、schema 测试和 MES 验证脚本。
