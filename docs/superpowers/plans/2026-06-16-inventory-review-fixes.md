# Inventory 审核修复实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**针对 PR #422 的审核发现，以代码事实为依据修复 Inventory 消费者可靠性、盘点冻结生命周期、状态转移安全性、估值一致性及公共契约兼容性。

**架构：**Inventory 的所有权保留在 Inventory 服务内。Quality 到 Inventory 的自动化仅使用公共 Quality 集成事件和 Inventory 台账，并由共享 CAP 消费者可靠性层保护。盘点并发采用显式的冻结/取消生命周期，将重盘要求表示为结构化领域结果，而非依赖消息文本控制流程。

**技术栈：**.NET 10、FastEndpoints、EF Core、NetCorePal CleanDDD、CAP 集成事件、xUnit。

---

### Task 1：Quality 消费者保护与重试幂等性

**文件：**
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs`
- 测试：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryMovementRequestedConsumerTests.cs`

- [x] 添加失败测试，证明不受支持的 Quality 事件类型会被拒绝并写入 `IIntegrationEventDeadLetterStore`，且不会发送命令。
- [x] 添加失败测试，证明重新投递的 Quality 事件若已有 `status-transfer-*` 移动记录，会在查询候选台账前返回。
- [x] 为受支持的 V1 通过/拒绝事件类型实现 `IntegrationEventConsumerGuard<InspectionResultIntegrationEvent>`。
- [x] 按 `IdempotencyKey:out` 和 `IdempotencyKey:in` 对 `StockMovements` 添加候选查询前的幂等检查。

### Task 2：盘点冻结生命周期

**文件：**
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountTask.cs`
- 创建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountRecountRequiredException.cs`
- 创建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CancelStockCountTaskCommand.cs`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Endpoints/Inventory/InventoryEndpoints.cs`
- 测试：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`
- 测试：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`

- [x] 添加失败的领域测试，证明取消操作会将任务改为 `cancelled` 并解除台账冻结。
- [x] 为 `POST /api/inventory/v1/count-tasks/{countTaskId}/cancel` 添加失败的命令/契约测试。
- [x] 使用 `StockCountRecountRequiredException` 取代基于消息文本的重盘处理。
- [x] 使用现有 CountsManage 权限实现取消命令和 endpoint。

### Task 3：状态转移安全性

**文件：**
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs`
- 测试：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`

- [x] 添加失败测试，证明状态转移会拒绝超过 `AvailableQuantity` 的数量。
- [x] 添加失败测试，证明冻结的源台账会返回 `KnownException`，而不是抛出未捕获的无效操作异常。
- [x] 添加可用数量保护，并将领域无效操作失败转换为 `KnownException`。

### Task 4：估值与契约兼容性

**文件：**
- 修改：`backend/common/Contracts/Nerv.IIP.Contracts.Inventory/InventoryIntegrationEvents.cs`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventConverters/InventoryIntegrationEventConverters.cs`
- 修改：构造位置参数 payload 的受影响测试。

- [x] 添加失败的领域测试，证明出库移动会忽略外部单位成本并使用当前移动平均成本。
- [x] 将新添加的 payload 成本/价值字段移至位置记录末尾。
- [x] 更新构造函数和事件转换器，使其与更安全的位置顺序一致。

### Task 5：验证与 PR 更新

- [x] 运行聚焦的 Inventory Domain/Web 测试。
- [x] 运行受治理的 `pwsh scripts/verify-business-inventory-mvp.ps1`。
- [x] 运行 `git diff --check`。
- [ ] 提交并推送到 PR 分支 `codex/issue-412-inventory-business-gap`。
- [ ] 在审核线程中以简洁的代码事实结果回复。
