# ADR 0003: 数据、消息与存储基线

- Status: Accepted
- Date: 2026-05-13

## Context

Nerv-IIP 需要同时支撑事务数据、缓存、异步消息、对象文件、向量检索与全链路观测。项目以私有化与混合部署为主要场景，默认技术选型必须兼顾通用性、可自部署性与演进空间。

0 到 1 阶段不能把基础设施做成每个服务各选一套，否则维护成本会迅速失控。同时，平台必须避免因为早期合库或合实例而退化成跨服务共享表、共享日志语义与共享保留策略的混乱状态。

## Decision

1. 事务数据默认采用 PostgreSQL。
2. 0 到 1 阶段优先使用同一 PostgreSQL 集群下按服务隔离 schema 的方式控制运维成本。
3. 缓存与分布式协作默认采用 Redis。
4. 应用级缓存默认采用 FusionCache，使用进程内 L1 缓存加 Redis L2 缓存；多实例部署需要启用 Redis backplane 以同步本地缓存失效。
5. 异步集成事件与后台异步任务分发默认采用 RabbitMQ。
6. 对象文件、备份包、日志归档附件等二进制内容默认采用 MinIO。
7. File Storage 服务拥有文件元数据、访问授权、上传下载会话、Upload Provider 抽象、对象存储 key 与保留策略；其它服务通过 File Storage API/SDK 引用 `fileId` 或 `FileReference`，不直接暴露对象存储 key。
8. 向量索引与相似检索默认采用 Qdrant。
9. 观测基线统一采用 OpenTelemetry；可视化与观测聚合默认接入 Grafana 体系。
10. 日志、审计、业务事务数据必须分开建模，不允许共用同一语义表或同一 retention 策略。
11. 跨服务状态传播优先通过集成事件完成，禁止通过共享数据库表或直接写入他服务 schema 的方式协作。
12. 若客户环境强依赖 Microsoft 体系，可在实施阶段评估 SQL Server 或 Azure 相关替代，但不改变默认基线。

## Rationale

1. 这组基础设施兼顾私有化可落地性、社区成熟度与后续演进空间，适合作为通用应用管理平台的默认基线。
2. 在同一 PostgreSQL 集群下按服务隔离 schema，能在保持服务级数据边界的同时降低早期运维复杂度。
3. 统一消息、缓存、对象存储与观测方案，可以减少平台服务之间的技术碎片化。
4. 将日志、审计与事务数据拆开，可以避免诊断需求、合规需求与业务一致性需求相互污染。
5. 通过事件而不是共享表传播状态，能从机制上维持服务边界的可持续性。
6. FusionCache 能在统一 Redis 基线之上提供 L1/L2 混合缓存、缓存击穿保护、超时控制、失效同步和 OpenTelemetry 集成，适合 Gateway 聚合查询、权限快照、配置字典和读侧投影等高频读场景。

## Consequences

1. 早期部署会引入较完整的基础设施套件，本地开发与 CI 环境需要提供标准编排。
2. 团队需要显式管理 schema 边界、事件契约与观测规范，而不是依赖数据库便利性进行跨服务联动。
3. 某些 Microsoft-first 客户环境可能会要求 SQL Server 或 Azure 替代，这会形成后续变体适配工作。
4. 统一基线有助于减少认知负担，但也意味着少量特殊服务不能随意自行选型。
5. 团队需要显式管理缓存键、TTL、tag、失效策略和 fail-safe 边界，避免缓存把权限、会话、动作状态或审计事实变成隐形真相源。
6. 团队需要通过 File Storage 统一治理文件访问和保留策略，避免各服务直接散落访问 MinIO 形成隐形权限边界。

## Implementation Notes

1. infra 层需要提供 PostgreSQL、Redis、RabbitMQ、MinIO、Qdrant、OpenTelemetry 的本地开发编排；完整平台拓扑后续通过平台级 Aspire AppHost 承接，Docker Compose 作为生成或兜底目标。
2. backend/services/FileStorage 需要封装文件元数据、上传下载授权、Upload Provider 抽象和 MinIO/object storage 适配；业务服务不直接把对象存储 key、tus endpoint 或 S3 multipart 细节当成长期公开契约。
3. backend/common/Caching 需要封装 FusionCache 的服务注册、Redis L2、Redis backplane、序列化、OpenTelemetry、缓存键命名和默认策略。
4. backend/common/Observability 需要定义 tracing、metrics、structured logging 的统一接入方式。
5. 跨服务 IntegrationEvent 默认采用 CAP outbox 或等价可靠发布机制，outbox 记录必须与领域事实处于同一事务边界。
6. 第一迭代可以只验证注册、心跳、状态同步的最短链路；进入第二迭代低风险动作闭环前，AppHub、Ops、File Storage、Knowledge 等会产生跨服务集成事件的服务必须具备可靠发布与消费者幂等处理。

## Out of Scope

1. 不在本 ADR 中规定数据库表结构、索引细节与迁移执行流程。
2. 不在本 ADR 中决定各环境的备份周期、日志保留时长与告警阈值。
3. 不在本 ADR 中决定 Microsoft-first 环境下的替代产品选型。
4. 不在本 ADR 中冻结每个业务查询的缓存键、TTL 和 tag 明细；这些规则由 docs/architecture/caching-baseline.md 和具体服务实现承接。
