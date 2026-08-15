# Quality 业务缺口 #415 实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**通过添加结构化检验规格/AQL、经公共 Quality 事件触发的 Inventory 放行、NCR MRB 审核事实及 CAPA 生命周期支持，补齐 Quality issue #415。

**架构：**Quality 拥有检验/NCR/CAPA 事实并发布增强的公共事件。Inventory 消费 Quality 公共契约，并在服务本地过账库存转移移动；任何服务都不得跨越 Domain/Infrastructure 边界，也不得写入其他服务的 schema。

**技术栈：**.NET 10、CleanDDD/netcorepal、EF Core PostgreSQL、FastEndpoints、CAP 集成事件、xUnit、`Nerv.IIP.Testing` schema 约定 helper。

---

## 文件

- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.Quality/QualityIntegrationEvents.cs`
- 修改：`backend/tests/Nerv.IIP.Contracts.Quality.Tests/QualityContractJsonTests.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionPlanAggregate/InspectionPlan.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/NonconformanceReportAggregate/NonconformanceReport.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/CorrectiveActionAggregate/CorrectiveAction.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/DomainEvents/NonconformanceReportDomainEvents.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionPlanEntityTypeConfiguration.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionRecordEntityTypeConfiguration.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/NonconformanceReportEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/CorrectiveActionEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Repositories/CorrectiveActionRepository.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionPlans/CreateInspectionPlanCommand.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionRecords/CreateInspectionRecordCommand.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/NonconformanceReports/SubmitNonconformanceReportDispositionCommand.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventConverters/InspectionIntegrationEventConverters.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventConverters/NonconformanceReportIntegrationEventConverters.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/CorrectiveActions/CorrectiveActionCommands.cs`
- 创建：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/CorrectiveActions/CorrectiveActionEndpoints.cs`
- 修改：`backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Program.cs`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Program.cs`
- 修改：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/InspectionAggregateTests.cs`
- 创建：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/CorrectiveActionTests.cs`
- 修改：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionIntegrationEventTests.cs`
- 修改：`backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityEndpointContractTests.cs`
- 更新：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryMovementRequestedConsumerTests.cs`
- 修改：`docs/architecture/database-schema-catalog.md`
- 修改：`docs/architecture/implementation-readiness.md`

## Task 1：检验规格与 AQL 的红灯测试

- [ ] 添加失败的 Quality 领域测试：创建带有变量特性 `length` 及上下限的计划，并断言实测值超限的计划检验记录会被拒绝。
- [ ] 添加失败的 Quality 领域测试：创建包含 AQL 样本量、接收数和拒收数的属性特性，并断言通过/拒绝/有条件通过结果。
- [ ] 运行 `dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~InspectionAggregateTests`。
- [ ] 实现特性规格字段、抽样字段和计划检验记录计算，直至测试通过。

## Task 2：Quality 事件与 Inventory 放行的红灯测试

- [ ] 添加失败的 Quality 契约/事件测试，断言检验事件 payload 包含库存放行维度和数值型结果行事实。
- [ ] 添加失败的 Inventory Web 测试：`quality.InspectionPassed` 将库存从 `quality` 转移到 `unrestricted`，`quality.InspectionRejected` 将库存从 `quality` 转移到 `blocked`，并且显式的 Quality 库存放行维度可消除多个匹配台账的歧义。
- [ ] 运行聚焦的 Quality 契约测试和 Inventory 消费者测试，并确认实施前会失败。
- [ ] 实现事件 payload 增强，以及使用确定性幂等键的 Inventory 消费者。

## Task 3：NCR MRB 与 CAPA 的红灯测试

- [ ] 添加失败的 NCR 测试，断言处置类型 `rework`、`scrap`、`return-to-supplier` 和 `conditional-release` 至少需要一条 MRB 审核记录。
- [ ] 添加失败的 CAPA 测试，覆盖从 NCR 开启、添加遏制/纠正/预防措施、验证有效性及关闭。
- [ ] 运行聚焦的 Quality 领域测试，并确认实施前会失败。
- [ ] 实现 MRB 审核记录、CAPA 聚合、命令和内部 endpoint。

## Task 4：持久化、migration 与契约

- [ ] 更新新增 Quality 字段和 CAPA 表的 EF 配置，并添加注释。
- [ ] 使用 `dotnet tool run dotnet-ef migrations add AddQualityBusinessGap415 ...` 生成 Quality migration。
- [ ] 按需更新 schema 约定测试，并运行聚焦的 Quality Web 测试。
- [ ] 更新 schema 目录和就绪文档，说明新增的 Quality 与 Inventory 闭环行为。

## Task 5：验证与 PR

- [ ] 运行聚焦的后端测试：Quality Domain/Web、Inventory Web、Contracts Quality 和 Contracts IntegrationEvents。
- [ ] 运行 `dotnet test backend/Nerv.IIP.sln`，除非被无关的基线失败阻塞；若被阻塞，应报告确切失败。
- [ ] 在 `codex/issue-415-quality-business-gap` 上提交所有变更。
- [ ] 推送分支并创建 PR，在正文中包含 `Closes #415`。
