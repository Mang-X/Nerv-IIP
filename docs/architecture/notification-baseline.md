# Notification 通知能力基线

本文档定义 Nerv-IIP 主平台的通用通知能力边界。Notification 是平台控制面的一部分，负责把平台事实、待处理事项和明确通知意图转换为用户可见的站内通知、待办入口和外部通道投递。

Notification 不是业务规则引擎、告警阈值系统或营销触达系统。它统一处理“如何通知、通知谁、是否重复、是否已读、是否投递成功”，但不替业务服务决定“业务上为什么需要通知”。

## 目标

1. 为 AppHub、Ops、AI Integration、Knowledge 和后续行业扩展提供统一通知入口。
2. 首批优先支持站内通知、待办、已读未读、基础接收人解析和幂等去重。
3. 按 provider 扩展邮件、企业微信、钉钉、Webhook 等外部通道；短信仍保留为后续 provider。
4. 统一记录通知意图、用户消息、投递尝试、失败原因和偏好命中结果，便于排障和审计追踪。
5. 防止各服务各自直连外部通知通道，导致接收人、权限、偏好、去重和投递状态分散。

## 非目标

1. 不定义行业业务告警规则，例如设备温度、产线节拍、质量异常等阈值策略。
2. 不替代 Observability 的指标告警、日志采集、追踪和告警后端。
3. 不替代 Ops 的 AuditRecord；通知可以引用审计事实，但审计事实仍归 Ops。
4. 不把 RabbitMQ/CAP 集成事件直接暴露成用户通知。
5. 不做营销群发、运营触达、客户旅程编排或复杂消息推荐。

## 事实归属

Notification 当前已落地并拥有以下事实：

1. NotificationIntent：某个服务希望把一个平台事实或待处理事项通知给人的意图。
2. NotificationMessage：面向单个用户或主体的用户可见消息。
3. NotificationTask：由通知意图派生的待办/任务入口。
4. DeliveryAttempt：对站内、邮件、企业 IM、Webhook 等通道的一次投递尝试。
5. NotificationRecipientChannelBinding：收件人到外部通道账号或 webhook URL 的映射，不保存 provider secret。
6. NotificationPreference：按通知类型与通道记录用户级开关，critical severity 可强制投递。
7. NotificationSubscription：按通知类型与通道记录外部投递订阅。
8. NotificationMessage 的状态字段：承载已读、未读、归档、忽略等用户状态。
9. IntegrationEventDeadLetter：Notification 消费侧无法处理的集成事件副本，支持 Pending、Replayed、Failed 和 Ignored 状态，用于 poison message 隔离、人工诊断和重放。

Notification 不拥有以下事实：

1. 用户、角色、组织、环境和授权事实，归 IAM。
2. 应用目录、实例状态、能力清单和心跳事实，归 AppHub。
3. 运维任务、审批请求、审计记录和动作结果，归 Ops。
4. 知识源、引入任务、检索索引和引用回显，归 Knowledge。
5. 模型提供方、MCP 工具、Skill 和工具治理事实，归 AI Integration。

## 触发模型

1. 领域服务优先通过 IntegrationEvent 表达已发生事实，例如 `InstanceStatusChanged`、`OperationFailed`、`ApprovalRequested` 或 `IngestionJobFailed`。
2. Notification 消费这些事实后，根据平台级通知策略、接收范围、用户偏好和去重规则生成 NotificationIntent 与 NotificationMessage。
3. 对强交互场景，例如审批、人工确认、任务失败处理，发布方可以提交明确 NotificationIntent，但仍不得直接调用外部通道 provider。
4. 外部通道投递采用异步最终一致性；投递失败不能回滚原业务事务。
5. Notification 必须按 `sourceService`、`sourceEventType`、`sourceEventId`、`organizationId`、`environmentId` 和 `dedupeKey` 处理幂等，避免 CAP 重试造成重复消息。

## 接收人与权限

1. Notification 通过 IAM 解析用户、角色、组织、环境和授权范围。
2. 发布方可以提供建议接收范围，例如请求人、审批人、环境管理员、资源负责人或指定角色。
3. 最终能否看到通知内容，必须以查询时的 IAM 权限和资源范围为准。
4. 通知正文不得携带敏感字段、密钥、底层对象存储 key 或不可脱敏的业务数据。
5. 如果用户失去资源权限，历史通知可以保留最小摘要，但资源详情跳转必须重新鉴权。

## 通道策略

1. 站内通知是首批默认通道，也是所有外部通道失败时的兜底视图。
2. 待办用于需要用户动作的通知，例如审批、确认、失败处理或补充信息。
3. 邮件、企业微信、钉钉和 Webhook 通过 DeliveryProvider 适配层接入，不进入领域模型作为固定依赖；短信仍是后续 provider。
4. Provider 凭证、模板映射和限流策略属于 Notification 的 Infrastructure 或部署配置，不进入其它服务。
5. 通道投递必须记录 DeliveryAttempt、失败分类、重试状态和最后错误摘要。

## 首批能力分层

### Phase 1. 站内通知与待办

1. 创建 Notification 服务骨架。
2. 支持 NotificationIntent、NotificationMessage、NotificationTask、消息状态和基础 DeliveryAttempt。
3. 支持按用户查询未读、全部、待办和资源相关通知。
4. 支持标记已读、批量已读、归档或忽略。
5. 消费 Ops 的 `OperationFailed`、`ApprovalRequested` 和 AppHub 的 `InstanceMarkedUnreachable` 作为首批事件来源。

### Phase 2. 外部通道

1. 增加邮件、企业微信、钉钉和 Webhook provider 首批实现。
2. 增加 NotificationRecipientChannelBinding、NotificationPreference 和 NotificationSubscription。
3. 支持按严重性、事件类型、组织环境和资源范围过滤外部通道。
4. 支持投递重试、退避、通道限流和失败可观测性。

### Phase 2.5. 消费可靠性治理

1. 集成事件 envelope/type/version 等消费前拒绝写入 persistent DLQ，不执行业务副作用。
2. CAP subscriber 业务 handler 在重试耗尽后写入 persistent DLQ，避免 poison message 无限占用消费循环。
3. PlatformGateway 和 Console 提供 DLQ 查询、详情、单条/批量 replay 和人工 ignore 管理入口。
4. Notification 提供 DLQ backlog metrics endpoint 和最小阈值通知 worker：当 persistent DLQ 的 `Pending + Failed` 可处理积压达到 `Notification:DeadLetterAlerts:Threshold`，且 `Enabled`、`OrganizationId`、`EnvironmentId`、`RecipientRefs` 已显式配置时，Notification 会通过自身 intent 管道提交一条 critical 运维任务通知；跨服务统一看板、长期指标存储和通用 Observability 告警引擎仍属于 Observability 后续切片。

### Phase 3. 合并与治理

1. 增加通知合并、静默窗口、频率限制和抑制规则。
2. 增加通知模板版本管理和多语言文本。
3. 增加面向运维排障的投递诊断查询。

## 边界规则

1. AppHub、Ops、AI Integration、Knowledge 和行业扩展不得各自实现站内通知表。
2. AppHub、Ops、AI Integration、Knowledge 和行业扩展不得直接依赖短信、邮件、企业 IM 或 Webhook provider。
3. Notification 不直接修改 AppHub 实例状态、Ops 任务状态、Knowledge 引入状态或 AI Integration 工具执行状态。
4. Notification 可以保存 resourceRef、sourceEventId、correlationId 和展示摘要，但不复制其它服务的领域模型。
5. Gateway 只聚合通知查询和动作入口，不拥有通知领域规则。
