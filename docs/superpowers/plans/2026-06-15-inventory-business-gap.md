# Inventory 业务缺口实施计划

> **供代理执行者使用：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**通过添加 Inventory 拥有的库存状态、预留/分配、估值、盘点安全和 Quality 事件放行行为来闭合 #412。

**架构：**所有库存事实都保留在 `Business.Inventory` 中。消费检验结果时使用 Quality 公开契约。Inventory 不得引用 WMS、MES、ERP 或 Quality 的 Domain/Infrastructure/Web 项目。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、EF Core PostgreSQL、CAP 消费者、xUnit、`Nerv.IIP.Testing` schema 约定辅助工具。

---

### Task 1：领域规则

**文件：**
- 修改： `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`
- 创建： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockQualityStatus.cs`
- 创建： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockReservationAggregate/StockReservation.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockMovementAggregate/StockMovement.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountTask.cs`

- [ ] 为库存状态规范化/拒绝、预留/释放/分配、移动平均估值以及盘点冻结/版本检查编写失败测试。
- [ ] 运行 `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore`，确认新测试失败。
- [ ] 实现让测试通过的最小领域代码。
- [ ] 再次运行同一领域测试项目并确认通过。

### Task 2：命令、查询和 Endpoint

**文件：**
- 修改： `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Endpoints/Inventory/InventoryEndpoints.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Auth/InventoryPermissionCodes.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockMovements/PostStockMovementCommand.cs`
- 创建： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockReservations/ReserveStockCommand.cs`
- 创建： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockReservations/ReleaseStockReservationCommand.cs`
- 创建： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/GetStockAvailabilityQuery.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CreateStockCountTaskCommand.cs`
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs`

- [ ] 为预留 endpoint、状态转移 endpoint、估值响应字段、盘点旧版本拒绝和 endpoint 契约元数据编写失败的 Web 测试。
- [ ] 运行聚焦的 Web 测试并确认失败。
- [ ] 使用带取消令牌的异步 EF Core 查询实现命令处理器和 endpoint。
- [ ] 再次运行聚焦的 Web 测试并确认通过。

### Task 3：Quality 事件消费者

**文件：**
- 修改： `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryMovementRequestedConsumerTests.cs`
- 创建： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs`

- [ ] 为 `quality.InspectionPassed` 和 `quality.InspectionRejected` 编写失败的消费者测试。
- [ ] 针对 `Nerv.IIP.Contracts.Quality` 中的 `InspectionResultIntegrationEvent` 实现 CAP 消费者。
- [ ] 再次运行消费者测试。

### Task 4：持久化与文档

**文件：**
- 修改： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/ApplicationDbContext.cs`
- 创建： `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockReservationEntityTypeConfiguration.cs`
- 修改：现有 Inventory EF 配置
- 生成：新的 Inventory EF migration 和模型快照
- 修改： `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventorySchemaConventionTests.cs`
- 修改： `docs/architecture/database-schema-catalog.md`
- 修改： `docs/architecture/implementation-readiness.md`

- [ ] 为新表/列/check constraint 编写或更新 schema 约定测试。
- [ ] 使用 PostgreSQL profile 生成 EF migration。
- [ ] 使用 #412 行为更新 schema 目录/就绪清单。
- [ ] 运行 Inventory Web 测试和 schema 测试。

### Task 5：验证与 PR

- [ ] 运行 `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore`。
- [ ] 运行 `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore`。
- [ ] 如果本地前置条件允许，运行 `scripts/verify-business-inventory-mvp.ps1`。
- [ ] 运行 `git diff --check`。
- [ ] 提交并推送 `codex/issue-412-inventory-business-gap`，然后创建包含 `Closes #412` 的 PR。
