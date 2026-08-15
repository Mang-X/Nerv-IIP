# WMS 与 Inventory RPC 幂等性

本文记录 MAN-390 / GitHub #706 的实施细节：两条 WMS 到 Inventory 的同步 RPC 链必须在 Inventory 已提交后仍能承受调用方超时。架构决策见 ADR 0019。

## 范围

涵盖的同步链路：

1. WMS 拣货任务创建时预留 Inventory 库存。
2. WMS 盘点执行创建时创建 Inventory 盘点任务并冻结目标台账。

移动过账仍通过既有 WMS 拥有的 `inventory_movement_requests` 及 Inventory 移动请求消费方路径以事件驱动方式完成。

## 决策

使用由调用方生成的稳定幂等键的同步 RPC，并通过重试恢复。

WMS 从持久的 WMS 业务身份标识派生键，而不是从短暂的任务 ID 派生：

1. 拣货预留键：`wms-pick-res:<hash(organizationId:environmentId:outboundOrderNo:lineNo)>`。该键形状已由 `CreatePickingTaskCommandHandler` 通过 `WmsInventoryReservationIdempotencyKeys.ForPickingTask` 实现；本 PR 保留该实现，并为其增加跨边界重试证据。
2. 盘点冻结键：`wms-count-freeze:<hash(organizationId:environmentId:countNo)>`。

Inventory 将该键持久化在已提交事实中，并将重复键作为恢复查询处理：

1. 相同键、相同载荷返回既有预留或盘点任务结果。
2. 相同键、不同载荷以幂等冲突拒绝。
3. 不同幂等键发生盘点任务编号冲突时，在创建第二次冻结前拒绝。

Inventory 盘点任务的回退键使用 `count-code:` 命名空间，从而显式调用方键不能在同一唯一索引中与盘点任务编号回退键冲突。

## 超时恢复

若 Inventory 提交后、WMS 持久化公开 Inventory ID 前 WMS 超时，操作员或调用方重试同一 WMS 命令。WMS 重新计算相同键并再次调用 Inventory。Inventory 返回已经提交的预留或盘点任务，WMS 将返回的公开 ID 持久化到出库行或盘点执行中。

这使补偿路径保持本地且确定：重试即对账查询。本切片不引入额外的跨服务表共享、下游虚假 ID 或尽力而为的清理任务。

## 验证

`WmsInventoryRpcIdempotencyAcceptanceTests` 分两层覆盖跨边界行为：

1. 快速的内存 WMS 和 Inventory 上下文证明：在模拟提交后超时后，WMS 重试会重新计算同一键，并恢复同一预留或盘点任务。
2. 由 `NERV_IIP_TEST_POSTGRES` 启用的选择性真实 PostgreSQL 测试，针对已迁移的 PostgreSQL 数据库运行 WMS 与 Inventory。它们通过 Inventory MediatR、UnitOfWork 与命令锁管线验证盘点冻结超时恢复，以及相同键并发重试的收敛。
3. 第二条选择性 PostgreSQL 路径让携带相同 `count_task_code` 的两个不同幂等键并发到达 `SaveChangesAsync`。这会执行数据库唯一索引，清理失败的 EF 跟踪器状态，在管线中重跑命令，并返回领域盘点任务编号冲突，且不留下额外冻结。
