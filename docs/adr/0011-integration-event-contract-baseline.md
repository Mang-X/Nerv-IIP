# ADR 0011: 集成事件契约基线

- Status: Accepted
- Date: 2026-05-19

## Context

Nerv-IIP 已经在 ADR 0001 中冻结服务边界，在 ADR 0003 中选择 CAP outbox + 可配置 messaging provider 作为异步集成事件与后台异步任务分发基线，并要求跨服务状态传播优先通过集成事件完成，禁止通过共享数据库表或直接写入他服务 schema 协作。默认单机 profile 使用 InMemory message queue，RabbitMQ 是跨进程和生产扩展 profile。

当前 AppHub、Ops、IAM、File Storage、Notification、Connector Host 等边界会逐步引入跨服务事件。若不尽早冻结 envelope、版本、路由和失败处理规则，不同服务很容易各自定义事件形态，导致消费者无法稳定追踪来源、重放消息、做幂等处理或区分业务失败与 poison message。

集成事件是公开契约，不是服务内部领域事件的直接序列化结果。它必须和 OpenAPI、SDK、Connector Protocol 一样具备稳定事实来源、版本策略和可验证的兼容边界。

## Decision

1. 所有跨服务 IntegrationEvent 必须采用统一 event envelope。Envelope 标准字段固定为 `eventId`、`eventType`、`eventVersion`、`occurredAtUtc`、`sourceService`、`correlationId`、`causationId`、`organizationId`、`environmentId`、`actor`、`idempotencyKey` 和 `payload`。
2. `eventId` 是全局唯一事件实例 ID，由发布方生成，消费者不得重新生成或用消息队列 delivery tag 替代。
3. `eventType` 使用稳定语义名称，格式为 `领域名.事件名`，例如 `iam.OrganizationCreated`、`apphub.ApplicationInstanceRegistered`、`ops.OperationTaskCompleted`。事件名称表达已经发生的事实，不使用命令式动词。
4. `eventVersion` 使用正整数主版本。首个公开版本为 `1`；同一 `eventType` 下新增可选字段、放宽枚举兼容值或新增消费者可忽略的 metadata 不提升版本；删除字段、改变字段语义、改变必填性、改变组织/环境隔离语义或改变业务事实含义必须新增版本。
5. `occurredAtUtc` 必须是 UTC 时间，表示领域事实发生时间，不是消息入队、出队或消费者处理时间。
6. `sourceService` 使用服务边界名称，首批固定为 `iam`、`file-storage`、`apphub`、`ops`、`notification`、`ai-integration`、`knowledge`、`platform-gateway`、`connector-host`。新增服务边界必须先更新架构文档或 ADR。
7. `correlationId` 贯穿入口请求、命令处理、outbox 发布、消费者处理和日志；`causationId` 指向导致当前事件产生的命令、事件或操作任务 ID；没有上游因果时允许为空。
8. `organizationId` 与 `environmentId` 是平台隔离上下文。租户无关的系统事件必须显式说明为空语义；不得用空值逃避授权、隔离或审计判断。
9. `actor` 表示导致事件发生的主体，至少包含主体类型和 ID；系统自动动作使用系统 actor，不伪造人工用户。
10. `idempotencyKey` 用于发布方和消费者共同识别业务幂等语义。默认值为 `eventType:eventVersion:eventId`；当业务需要以命令、外部回调或操作任务为幂等边界时，发布方必须提供稳定业务键。
11. `payload` 只承载事件事实，不重复 envelope 字段；payload 字段必须可 JSON 序列化，并避免包含 access token、refresh token、密码、密钥、完整连接串、文件正文、大体积日志正文或不必要的个人敏感信息。
12. 集成事件 schema 的代码事实来源固定在 `backend/common/Contracts/` 下的版本化 Contracts 项目中，例如 `Nerv.IIP.Contracts.Iam`、当前已存在的 `Nerv.IIP.Contracts.AppHubQueries`、`Nerv.IIP.Contracts.Ops` 或后续 `Nerv.IIP.Contracts.IntegrationEvents`。服务内部 Domain event、EF entity、Infrastructure model、queue message adapter 和测试 fixture 都不得成为公开 schema 的事实来源。
13. Contracts 项目应定义 envelope、payload DTO、事件类型常量、版本常量和 JSON 序列化测试；发布方与消费者通过 Contracts 或 Platform SDK 消费同一契约，不复制 DTO。
14. Topic/routing key 命名采用小写点分格式：`nerv-iip.<deployment-env>.<source-service>.<bounded-context>.<event-name>.v<event-version>`。例如 `nerv-iip.prod.iam.identity.organization-created.v1`。
15. `<deployment-env>` 表示部署环境类别或实例环境标识，由部署 profile 统一注入；它不是 envelope 中的 `environmentId`，不得把客户名称、数据库名、连接串片段或内部主机名放入 routing key。
16. `<event-name>` 使用 kebab-case 的过去式事实名。routing key 中的版本必须与 envelope 的 `eventVersion` 一致。
17. 消费者订阅应尽量按 source service、bounded context、event name 和 version 精确绑定；需要通配订阅时必须在消费者文档或测试中说明过滤规则，避免误消费其它租户、环境或版本的事件。
18. 发布方默认使用 CAP outbox 或等价可靠发布机制，outbox 记录必须与产生事件的领域事实处于同一事务边界。不得在事务提交前直接把跨服务事件发送到具体 broker；当前 broker profile 包括默认 InMemory 和显式 RabbitMQ。
19. 消费者必须具备幂等处理能力，并以 `eventId` 加消费者名称作为最低幂等键；当业务幂等边界强于事件实例时，还必须使用 `idempotencyKey` 或业务唯一约束防止重复副作用。
20. 消费者处理必须先完成 schema/version 校验、组织/环境上下文校验和幂等判断，再执行业务副作用。
21. 临时失败使用有上限的重试策略，重试必须保留原始 `eventId`、`correlationId`、`causationId` 和 `idempotencyKey`。重试次数、退避窗口和超时由消息基础设施 profile 配置，但消费者不得无限重试。
22. 超过重试上限、schema 无法解析、版本不受支持、缺少必需隔离字段、授权上下文非法或业务约束明确不可恢复的消息进入 DLQ。DLQ 消息必须保留原始 envelope、payload、失败原因、消费者名称、首次失败时间、最后失败时间和尝试次数。
23. Poison message 指重复导致同一消费者失败且无法通过重试恢复的消息。处理规则是隔离到 DLQ、告警、记录诊断上下文，并由运维或发布方修复后决定丢弃、补偿或 replay；不得通过吞异常把 poison message 标记为成功。
24. Replay 只能从受控来源执行：outbox、归档事件存储、DLQ 或经批准的修复脚本。Replay 必须保持原始 `eventId`、`eventType`、`eventVersion`、`occurredAtUtc`、`sourceService` 和业务 payload，不得伪造成新业务事实；需要重新投递时可以追加 replay metadata，但不得改变原事实。
25. Replay 消费者仍必须执行幂等校验。重放用于修复投递、投影或下游状态，不用于绕过版本兼容、权限隔离或业务校验。
26. 事件版本迁移采用并行发布和显式退役策略。发布方可以在迁移窗口内同时发布 v1 与 v2；消费者升级完成、回放窗口结束、DLQ 清理完成后，旧版本才可以退役。
27. 破坏性事件变更必须同步更新 Contracts、SDK、发布方测试、消费者契约测试、迁移说明和相关架构文档；只修改消息 adapter 或 RabbitMQ binding 不构成契约变更完成。

## Rationale

1. 统一 envelope 能让日志、审计、追踪、重试、DLQ 和 replay 使用同一组字段，降低跨服务诊断成本。
2. 将 `occurredAtUtc`、`correlationId`、`causationId`、`actor`、组织和环境上下文放入 envelope，可以避免消费者从业务 payload 中猜测隔离与因果关系。
3. 使用正整数事件版本比把 NuGet 包版本、应用版本或数据库迁移版本混入事件契约更直接，也更适合 routing key 绑定与并行迁移。
4. Topic/routing key 显式携带环境、服务、上下文、事件名和版本，有利于 RabbitMQ binding、DLQ 分类、运维观察和消费者最小订阅。
5. 以 `backend/common/Contracts/` 作为代码事实来源，符合 ADR 0001 对 common 窄共享库的约束，也避免服务内部模型泄漏成跨进程契约。
6. CAP outbox 或等价机制把事件发布和领域事实提交放在同一事务边界，可以避免数据库事实已提交但消息丢失，或消息已发出但事实回滚。
7. 幂等、DLQ、poison message 和 replay 规则必须成为契约基线的一部分；否则事件系统在失败场景下只会把同步耦合换成不可诊断的异步耦合。

## Consequences

1. 每个新增集成事件都需要补 Contracts DTO、事件常量、序列化测试和消费者契约测试，早期实现成本会上升。
2. 服务内部 Domain event 不能直接跨进程发布，需要显式转换为 IntegrationEvent payload。
3. routing key、event type 和 version 一旦公开就不能随意重命名；语义调整需要走新版本和迁移窗口。
4. 消费者必须维护 inbox、processed message 记录或等价幂等机制，并在数据库唯一约束或业务状态机中防止重复副作用。
5. 运维需要为 RabbitMQ DLQ、重试队列、告警、replay 工具和诊断日志建立统一 profile。
6. Replay 能提升恢复能力，但也要求业务处理器真正做到幂等；不能把 replay 当成手工改数据的替代品。

## Implementation Notes

1. `backend/common/Contracts/` 下应补充共享 envelope 类型或基础接口；若多个 Contracts 项目重复 envelope，应提升到窄共享 Contracts 包，而不是放入无边界的 Utils。
2. 每个服务发布集成事件时，应在 Web/Application 层把领域事实转换为公开 IntegrationEvent payload，再交给 CAP outbox 或等价 publisher。
3. 每个消费者应记录 `consumerName`、`eventId`、`eventType`、`eventVersion`、`idempotencyKey`、处理状态、首次处理时间、最后处理时间和失败原因；记录位置归消费者服务所有，不写入发布方 schema。
4. 契约测试至少覆盖 envelope 必填字段、JSON 字段名稳定性、未知可选字段容忍、版本不匹配拒绝和重复投递幂等。
5. 服务日志字段应沿用 ADR 0003 的观测要求，至少在发布和消费日志中保留 `service.name`、`environment`、`traceId`、`correlationId`、`organizationId`、`environmentId`、`actor`、`eventId`、`eventType` 和 `eventVersion`。
6. DLQ 和 replay 工具必须输出 correlationId、消费者名称、失败原因、尝试次数和 replay 批次 ID；不得在日志中打印敏感 payload。
7. Connector Host 与外部应用协议事件若进入平台消息系统，也必须先转换为本 ADR 定义的 IntegrationEvent envelope；外部协议版本不直接等同于平台事件版本。

## Out of Scope

1. 不在本 ADR 中定义具体 RabbitMQ exchange、queue、prefetch、TTL、quorum queue 或镜像策略。
2. 不在本 ADR 中冻结每个业务事件的 payload 字段清单；这些字段由对应 Contracts 项目、服务文档和测试承接。
3. 不在本 ADR 中实现事件存储、replay UI、DLQ 管理页面或具体运维脚本。
4. 不在本 ADR 中要求所有现有同步链路立即改为异步事件；只有跨服务状态传播进入 IntegrationEvent 时才适用本基线。
