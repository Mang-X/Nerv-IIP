# ADR 0019：WMS 到 Inventory 的 RPC 幂等性

- 状态：已接受
- 日期：2026-07-07

## 背景

WMS 有两条同步 Inventory RPC 调用链，可能在 Inventory 已提交、但 WMS 尚未存储返回的 Inventory 标识符时超时：

1. 创建拣货任务时预留 Inventory 库存；
2. 创建盘点执行时创建 Inventory 盘点任务并冻结目标台账。

如果没有稳定的恢复键，调用方重试可能产生第二次 Inventory 副作用，也可能使 WMS 无法获得已提交的 Inventory 标识符。MAN-390 / GitHub #706 要求这些调用链在超时和重试后收敛，且不得写入共享数据库或使用伪造的下游 ID。

## 决策

使用同步 RPC、由调用方生成的稳定幂等键，以及基于查询的重试恢复机制。

WMS 根据持久业务标识派生键：

1. 拣货预留：`wms-pick-res:<hash(organizationId:environmentId:outboundOrderNo:lineNo)>`；
2. 盘点冻结：`wms-count-freeze:<hash(organizationId:environmentId:countNo)>`。

Inventory 将该键持久化到已提交的业务事实上。使用相同键的重试属于恢复查询：

1. 键和载荷均相同时，返回现有预留或盘点任务结果；
2. 键相同而载荷不同时，以幂等冲突拒绝；
3. 若盘点任务编码与另一幂等键冲突，则在创建第二次冻结前拒绝。

Inventory 的盘点任务回退键也使用 `count-code:<countTaskCode>` 命名空间，使调用方提供的键不会与旧版盘点编码回退空间冲突。

对于服务进程内并发的盘点冻结重试，Inventory 通过现有命令锁行为，按组织、环境和解析后的幂等键串行执行 `CreateStockCountTaskCommand`。数据库唯一索引仍是已提交事实的持久化兜底保障。

## 已考虑的替代方案

1. **事件驱动的冻结和回调回执**：本次不采用，因为调用方需要同步应答来创建 WMS 盘点执行和拣货任务。新增事件回执表会扩大涉及范围，且运维人员重试时仍需要查询路径。
2. **超时后的尽力清理**：不采用，因为 WMS 无法判断 Inventory 是否已在超时前提交。清理可能释放本应由后续重试恢复的有效预留或盘点冻结。
3. **共享对账表**：不采用，因为 ADR 0003 和 ADR 0012 要求服务数据所有权相互隔离；WMS 不得读写 Inventory schema。

## 后果

补偿路径具有确定性：重试同一 WMS 命令。WMS 重新计算相同的键，Inventory 返回已提交的业务事实，WMS 再在本地持久化返回的 Inventory ID。

这样可将超时恢复限制在两个事实所属服务内，且不引入跨服务流程管理器。传输超时后，运维人员仍需使用常规重试或 DLQ 工具重新驱动 WMS 命令。

验证必须包含跨边界的 WMS 和 Inventory 行为。快速内存测试可以覆盖命令流，但当 `NERV_IIP_TEST_POSTGRES` 可用时，真实 PostgreSQL 配置档测试必须覆盖唯一索引以及重试/并发行为。
