# Notification 当前架构

本文描述 Nerv-IIP 当前通用通知能力的事实所有权、事件输入和投递边界。M2-K 迁移前的混合基线包含 Phase 规划、实现批次和 provider 运维细节，已按原 Git blob 冻结到 [`../../reports/notification-baseline-pre-m2-k-2026-09-07.md`](../../reports/notification-baseline-pre-m2-k-2026-09-07.md)。

## 事实所有权

Notification 拥有 NotificationIntent、NotificationMessage、NotificationTask、DeliveryAttempt、收件人通道绑定、用户偏好、订阅、消息已读/归档状态，以及 Notification 消费侧的持久化死信事实。

Notification 不拥有 IAM 用户/权限、AppHub 实例、Ops 动作与审计、Knowledge 索引、AI 工具治理或 Observability 阈值规则；这些事实只作为受控事件、资源引用或通知意图输入。

## 当前交互边界

1. 领域服务优先发布已经发生的 IntegrationEvent，或在需要强交互时提交明确 NotificationIntent；Notification 负责接收人解析、去重、偏好、消息/待办与投递状态。
2. 外部通道投递是异步最终一致过程，投递失败不能回滚原业务事务。
3. 站内消息与待办是平台通知视图；邮件、企业 IM、Webhook 等通过 provider 适配层接入，业务服务不得直连 provider。
4. Notification 通过 IAM 解析组织、环境、主体与授权范围，但不维护平行权限模型；资源详情在读取时仍需重新鉴权。
5. Observability 告警规则的阈值与触发语义归 Observability；Notification 只消费告警意图并完成消息、去重、静默与通道投递。

## 权威路由

- 可观测性与告警所有权：[`observability.md`](observability.md)。
- IAM 授权边界：[`iam-authentication.md`](iam-authentication.md)。
- 部署/provider 配置与排障：[`../../runbooks/deployment.md`](../../runbooks/deployment.md)。
- 当前 Schema 人工目录：[`../../reference/data/database-schema-catalog.md`](../../reference/data/database-schema-catalog.md)。

精确事件名、endpoint、provider 配置、重试参数、阶段完成状态和一次性验收证据以当前代码、配置、Contracts、迁移和测试为准。
