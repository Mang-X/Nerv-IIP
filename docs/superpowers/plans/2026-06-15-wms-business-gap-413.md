# WMS 业务缺口 #413 实施计划

> **供代理执行者使用：**必需子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**在保留 Inventory 公开契约边界的同时，闭合 #413 的 WMS 过账失败和任务执行循环。

**架构：**对于被 Inventory 业务规则拒绝的有效移动请求，Inventory 发出公开的过账失败集成事件。WMS 消费该事件以标记请求/订单状态，并基于现有 WarehouseTask 领域行为公开任务执行 endpoint。变基到 #412 后，Inventory 预留 API 已可用；WMS 现在创建出库拣选任务时预留库存，并将预留 ID 带入 movement-requested，使 Inventory 在出库过账期间分配库存。FEFO/FIFO、ASN 策略、定向上架、LPN/HU 以及预留释放/取消补偿仍作为已记录的公开契约后续事项。

**技术栈：**.NET 10、CleanDDD、FastEndpoints、EF Core、CAP 集成事件、xUnit。

---

## 任务

- [x] 将 `StockMovementPostingFailedIntegrationEvent` 添加到 `Nerv.IIP.Contracts.Inventory`，并添加聚焦的契约测试。
- [x] 在 `InventoryMovementRequestedIntegrationEventHandlerForPostingMovement` 中捕获业务过账拒绝并发布失败事件，同时让信封校验失败继续沿用现有 DLQ 路径。
- [x] 为 `inventory.StockMovementPostingFailed` 添加 WMS 命令/消费者测试。
- [x] 实现 WMS 失败请求命令和消费者；当请求引用入库/出库订单时，将其状态转为 `InventoryPostingFailed`。
- [x] 为进度与完成 endpoint 添加 WMS 任务执行契约测试。
- [x] 实现 `RecordWarehouseTaskProgressCommand`、`CompleteWarehouseTaskCommand`、endpoint 和 operation ID。
- [x] 按组织和环境限定 WCS 完成/失败命令的范围。
- [x] 变基到 Inventory #412 预留模型，并为出库拣选添加 WMS 到 Inventory 的预留客户端覆盖。
- [x] 在 WMS 出库行和移动请求上持久化 Inventory 预留 ID，并通过 `inventory.InventoryMovementRequested` 传播该 ID。
- [x] 添加 Inventory 消费者覆盖，证明出库移动请求会分配所提供的预留 ID。
- [x] 更新就绪清单/文档，以反映已交付纵切，以及预留释放/取消补偿、FEFO、ASN、定向上架和 LPN/HU 的显式延后公开契约。
- [x] 提交前运行聚焦的 Inventory/WMS 测试和最终仓库检查。
