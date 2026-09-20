# Scheduling 订单紧急度快照保留架构

本文只描述 BusinessScheduling 订单紧急度快照的**当前所有权、归档/恢复边界和安全不变量**。配置、删除授权注入、存储桶准备、迁移、恢复演练和验证命令见 [`../../runbooks/scheduling-order-urgency-retention.md`](../../runbooks/scheduling-order-urgency-retention.md)；M2-L 前的混合正文冻结于 [`../../reports/m2-l-business-scheduling-order-urgency-retention-pre-split-2026-09-07.md`](../../reports/m2-l-business-scheduling-order-urgency-retention-pre-split-2026-09-07.md)。

## 所有权与生命周期

- BusinessScheduling 拥有 `order_urgency_snapshots` 生命周期策略、批次意图、成员关系、租约和恢复审计等业务事实；FileStorage 只负责合规对象存储传输与精确对象版本证据。
- 当前基线在线保留 180 天、总保留 1,095 天；即使订单最新快照早于在线窗口，也必须始终在线保留最新一条。精确配置仍以当前配置 producer 为准。
- 策略以 `organizationId + environmentId` 隔离，不支持通配 scope。功能默认 fail-closed；未显式启用、配置无效、法律保全生效或删除授权不足时不得删除源或归档版本。
- 源记录删除与归档版本删除是两种独立授权事实；归档写入和证据验证可以在没有删除授权时完成。

## 归档与幂等边界

1. Scheduling 先在数据库事务中持久化确定性 `pending` 批次意图及有序源成员关系，再进行远程 I/O；成员关系阻止同一源代际进入另一并发批次。
2. 批次选择以不可变快照 ID 收尾，批次标识从源代际稳定派生。运行中改变 batch/size 配置不得重写既有批次成员。
3. FileStorage 使用条件写入创建范围命名空间对象并返回非空精确版本 ID；Scheduling 必须持久化对象键、版本、SHA-256、字节长度和验证时间，再进入任何破坏性转换。
4. 重试只在精确对象版本的哈希与长度与确定性载荷一致时复用证据；已归档批次不因重试再上传一个未受管版本。
5. 源删除前必须重新验证：成员仍在在线窗口外、同订单仍存在更新快照、对象证据仍指向同一键/版本、法律保全未生效且授权有效。任一条件不满足则整个源批次保留。
6. 范围租约、批次生命周期 revision 与数据库事务共同形成 fencing；租约丢失或并发接管导致 revision 改变时，破坏性事务必须回滚。
7. 单条快照超过当前允许的归档载荷上限时保持在线并记录稳定失败分类，不得阻塞后续可归档记录。

## 恢复边界

恢复只读取批次记录的**精确对象版本**，重新验证哈希、长度、信封范围和持久化证据，并只重新水合缺失的不可变快照。恢复产生新的快照 ID，因此形成新的后续批次，不修改原终态批次；每次尝试都追加恢复审计，幂等重放也必须可审计。

恢复入口属于内部服务授权面，不是 Business Console 公共 UI。具体 route、actor 解析和操作步骤由当前代码/契约与 Runbook 负责，Architecture 不复制第二份 endpoint 清单。

## 可观测性边界

保留工作器对运行结果、快照处理结果、符合条件积压、最老积压年龄和稳定错误分类暴露可观测信号，并按受限的 organization/environment scope 区分。指标标签只能来自受治理配置，不得由任意请求数据注入；告警阈值和实际操作判读由 Observability producer 与 Runbook 管理。
