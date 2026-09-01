# 集成事件消费治理

本页规定公开/跨服务集成事件与业务服务本地集成事件的**分类与复核规则**。当前生产者/消费者查询见 [`../../reference/integration/event-consumption-matrix.md`](../../reference/integration/event-consumption-matrix.md)；ADR 0011 继续治理公开跨服务事件的信封、版本和幂等性基线。

## 什么算当前事实

1. 公开契约事件从 `backend/common/Contracts/**` 核对。
2. 业务服务本地集成事件从各服务 `Application/IntegrationEvents` 核对。
3. 只有仓库中实际注册并可运行的 `IntegrationEventConsumer`、`IIntegrationEventHandler`、`CapSubscribe` 等消费实现才算活动消费方；Issue、计划、注释或预留类型不能证明已消费。
4. producer/consumer 的可靠性与副作用必须继续回到 inbox/outbox、事务、幂等、dead-letter 和行为测试核实；矩阵只做索引，不替代测试证据。
5. 服务本地事件若未实现公共信封，不得因为写入 Reference 就被升级为跨服务契约。真正跨服务前必须先建立适用的公开契约与可靠性边界。

## 分类

| 分类 | 判定 |
| --- | --- |
| `consumed-internally` | 仓库内至少存在一个可核实的活动消费方，且该行描述当前真实处理关系。 |
| `needs-business-consumer` | 当前业务闭环明确需要消费方，但活动实现仍缺失；缺口跟踪留在 GitHub/Linear，不在矩阵维护完成状态。 |
| `audit-or-external-only` | 当前不要求平台内状态变更；事件用于审计、通知之外的外部扩展、分析或可观测性。 |
| `producer-only-until-feature` | 生产方事实有价值，但当前下游使用查询/API/解析边界或尚未出现需要消费的功能。 |
| `deprecated/covered-by-other-contract` | 当前业务交接已经使用更新或更窄的契约，该事件不再承担原交接职责。 |

矩阵中的分类是当前解释，不是永久属性。新增真实消费者、替换交接契约或删除处理器后必须重新判断。

## 强制复核触发器

发生下列任一变化时，必须在同一变更中复核 Reference 矩阵相关行：

- 新增、删除或重命名公开 `*IntegrationEvent` 契约；
- 新增、删除或改变活动消费 handler / `CapSubscribe`；
- producer 从本地事件升级为公开契约，或反向收窄；
- 事件 payload、版本、幂等键、业务副作用或 dead-letter 语义发生会改变消费关系的变化；
- 旧契约被另一契约覆盖，导致分类应转为 `deprecated/covered-by-other-contract`；
- 业务原本不需要消费方，后来出现明确的内部状态变更需求。

## 审核纪律

1. 不从历史 `#485`、旧扫描或“已发布但未消费”清单直接推导当前缺口，必须重新核对源码。
2. “存在订阅类型”不等于业务闭环成立；必须确认 handler 实际被注册、能处理当前信封，并有适当幂等/失败语义。
3. “没有内部消费者”不自动是缺陷。审计事实、外部扩展事实或查询面已经覆盖的交接可以合法保持 producer-only/audit-only。
4. Issue 编号可以作为 provenance 链接，但完成/开放状态、日期化复核过程和一次性扫描结果不得写成 Reference 的当前权威字段。
5. 不为事件矩阵新增永久扫描 registry 或自然语言 checker；如果某个关键消费关系必须机器保证，应在真正契约/消费者测试中验证。
