# 库存盘点审批审核修复实施计划

> **供代理执行者使用：**必须使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 子技能，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**让 Inventory 审批完成消费者通过既有命令工作单元路径，持久化已批准和已拒绝的库存盘点调整。

**架构：**CAP 消费者继续负责校验审批信封，并且只路由 Inventory 的 `inventory-count-variance` 文档。它发送一个新的内部命令，在应用层加载待处理调整、任务和台账；既有命令管线负责持久化状态并分发由此产生的领域事件。调整离开 `pending-approval` 状态后，重复投递成为无操作。

**技术栈：**.NET 10、CleanDDD 命令处理器、MediatR `ISender`、EF Core、CAP、xUnit。

## 全局约束

- 将变更限制在 Inventory 内，并保留实际台账不变量。
- 不得新增或变更 HTTP endpoint、schema、OpenAPI snapshot 或生成的客户端。
- 使用异步 EF Core API，并由命令工作单元负责持久化和领域事件分发。
- 保留既有 `IntegrationEventConsumerGuard` 的来源/类型/版本校验和死信行为。

---

### 任务 1：覆盖真实 CAP 消费者路径

**文件：**
- 修改：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/StockCountApprovalTests.cs`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment.cs`
- 创建：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CompleteStockCountAdjustmentApprovalCommand.cs`

**接口：**
- 消费：`ApprovalCompletedIntegrationEvent`、`ISender`、`ApplicationDbContext`。
- 产出：带组织 ID、环境 ID、盘点任务代码、审批链 ID 和完成结果的 `CompleteStockCountAdjustmentApprovalCommand`。

- [ ] **步骤 1：编写失败的消费者测试**

在审批完成测试中，以执行命令的 sender 替换直接 `DbContext` 持久化。断言已批准的投递会将持久化调整改为 `posted`、创建一条变动、解除台账冻结并将任务改为 `confirmed`；断言已拒绝/退回的投递会作废调整、保持现有量不变、解除台账冻结并将任务改为 `recount-required`。

- [ ] **步骤 2：运行目标测试确认失败**

运行：`dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --filter FullyQualifiedName~StockCountApprovalTests`

预期：失败，因为既有消费者没有 `ISender` 依赖，也没有发出负责持久化的命令。

- [ ] **步骤 3：添加完成命令，并让消费者通过该命令路由**

定义 `CompleteStockCountAdjustmentApprovalCommand : ICommand<CompleteStockCountAdjustmentApprovalResult>`。其处理器按组织、环境、盘点任务代码和审批链 ID 选择调整；除非调整处于 `pending-approval`，否则返回无操作结果；加载精确的台账维度；批准时调用 `ConfirmApprovedAdjustment`、添加变动并调用 `MarkPosted`；拒绝/退回时调用 `RequireRecountAfterApprovalRejection` 和 `VoidAfterApprovalRejection`。CAP 处理器校验来源/文档，并调用 `sender.Send(...)`。

- [ ] **步骤 4：运行目标测试确认通过**

运行：`dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --filter FullyQualifiedName~StockCountApprovalTests`

预期：通过，由执行命令的 sender 持久化批准和拒绝结果。

### 任务 2：消除审批客户端的静默回退

**文件：**
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs`
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Approval/StockCountApprovalClient.cs`
- 修改：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`

**接口：**
- 消费：必需的 `IStockCountApprovalClient` DI 注册，来自 `Program.cs`。
- 产出：调用方尝试在没有审批客户端时创建超阈值处理器，构造过程立即失败。

- [ ] **步骤 1：编写失败的构造函数/超阈值测试**

新增或调整测试，使配置为需要审批的处理器必须接收 `IStockCountApprovalClient`；测试不得接受伪造的审批链 ID。

- [ ] **步骤 2：运行目标测试确认失败**

运行：`dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --filter FullyQualifiedName~InventoryEndpointContractTests`

预期：失败，因为 `GeneratedStockCountApprovalClient` 仍在伪造审批链 ID。

- [ ] **步骤 3：将客户端依赖设为必需并删除桩实现**

要求将 `IStockCountApprovalClient` 注入 `ConfirmStockCountAdjustmentCommandHandler`；只在测试构造需要时保留可选的 options 参数。删除 `GeneratedStockCountApprovalClient`，并更新直接构造的测试以注入真正的测试替身。

- [ ] **步骤 4：运行聚焦的命令测试**

运行：`dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --filter "FullyQualifiedName~StockCountApprovalTests|FullyQualifiedName~InventoryEndpointContractTests"`

预期：通过，且不存在生产回退路径。

### 任务 3：恢复既有盘点调整事实不变量并验证

**文件：**
- 修改：`backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountAdjustmentAggregate/StockCountAdjustment.cs`
- 仅当不变量测试需要修正时，修改：`backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`。

**接口：**
- 消费：`StockCountAdjustment.Record(StockCountTask, StockMovement, string)`。
- 产出：已入账调整事实拒绝没有已分配标识符的变动；待审批事实仍是唯一允许变动为空的状态。

- [ ] **步骤 1：在本地运行失败的 CI 领域测试**

运行：`dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --filter FullyQualifiedName~Count_adjustment_fact_requires_assigned_movement_id`

预期：失败，因为当前分支允许 `Record` 接受尚未分配 ID 的变动。

- [ ] **步骤 2：恢复最小不变量**

让 `StockCountAdjustment.Record` 在构造已入账事实前拒绝 `movement.Id is null`。不得修改待审批工厂方法，该工厂方法有意不含变动。

- [ ] **步骤 3：运行聚焦测试和必需回归门禁**

运行：`dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --filter FullyQualifiedName~Count_adjustment_fact_requires_assigned_movement_id`

运行：`dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj`

运行：`dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj`

预期：所有测试通过；由于此后续修复不变更公开 endpoint 或 schema，因此无需重新生成 OpenAPI、schema 或前端产物。
