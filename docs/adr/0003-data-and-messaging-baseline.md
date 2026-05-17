# ADR 0003: 数据、消息与存储基线

- Status: Accepted
- Date: 2026-05-13

## Context

Nerv-IIP 需要同时支撑事务数据、缓存、异步消息、对象文件、向量检索与全链路观测。项目以私有化与混合部署为主要场景，默认技术选型必须兼顾通用性、可自部署性与演进空间。

0 到 1 阶段不能把基础设施做成每个服务各选一套，否则维护成本会迅速失控。同时，平台必须避免因为早期合库或合实例而退化成跨服务共享表、共享日志语义与共享保留策略的混乱状态。

## Decision

1. 事务数据默认采用 PostgreSQL。
2. 0 到 1 阶段优先使用同一 PostgreSQL 集群下按服务隔离 schema 的方式控制运维成本；当工具链、AppHost resource 或 `EnsureCreated()` 本地验证要求更强隔离时，可以在同一集群内按服务拆分 database，并继续保留服务自有 schema。
3. 缓存与分布式协作默认采用 Redis。
4. 应用级缓存默认采用 FusionCache，使用进程内 L1 缓存加 Redis L2 缓存；多实例部署需要启用 Redis backplane 以同步本地缓存失效。
5. 异步集成事件与后台异步任务分发默认采用 RabbitMQ。
6. 对象文件、备份包、日志归档附件等二进制内容默认采用 MinIO。
7. File Storage 服务拥有文件元数据、访问授权、上传下载会话、Upload Provider 抽象、对象存储 key 与保留策略；其它服务通过 File Storage API/SDK 引用 `fileId` 或 `FileReference`，不直接暴露对象存储 key。
8. 向量索引与相似检索默认采用 Qdrant。
9. 观测基线统一采用 OpenTelemetry；可视化与观测聚合作为部署期可选适配，不默认绑定特定商业平台或收费体系。
10. 日志、审计、业务事务数据必须分开建模，不允许共用同一语义表或同一 retention 策略。
11. 跨服务状态传播优先通过集成事件完成，禁止通过共享数据库表或直接写入他服务 schema 的方式协作。
12. 数据库替换采用显式 database profile，而不是让业务代码直接感知具体 provider；默认 profile 为 PostgreSQL。
13. 信创环境优先评估 netcorepal 模板已有 profile 覆盖的 GaussDB 与 DMDB；Kingbase、OceanBase 等未出现在当前模板公开参数中的数据库，必须先完成 EF Core provider、CAP storage/outbox、迁移和集成测试评估后再进入支持矩阵。
14. 若客户环境强依赖 Microsoft 体系，可在实施阶段评估 SQL Server 或 Azure 相关替代，但不改变默认基线。
15. 后端应用代码统一依赖 `Microsoft.Extensions.Logging` 抽象；服务宿主默认采用 Serilog 作为结构化日志 provider，并通过 Console 和 OpenTelemetry/OTLP 输出日志事件。
16. 日志持久化不写入业务 PostgreSQL schema。普通运行日志由 OpenTelemetry Collector 转发到部署 profile 选择的观测后端；日志包、诊断包和长期归档附件通过 File Storage/MinIO 管理。
17. 无生产观测后端的本地兜底采用分层策略：首先使用 Console/stdout 与滚动 JSONL 文件保留短期诊断日志；其次允许 OpenTelemetry Collector 使用本地持久化队列或文件导出降低短时离线丢失；需要本地观测 UI 时，优先使用 .NET Aspire Dashboard 的自部署 profile。
18. InfluxDB 默认用于指标/时间序列场景，不作为 Nerv-IIP 的应用日志默认后端。SQLite 只允许作为开发诊断、小型单机临时缓存或 collector/agent 内部队列实现细节，不作为生产日志检索与保留后端。
19. 内置日志持久化 profile 采用滚动 JSONL 热日志、Log Archive Worker、File Storage 压缩 chunk 和独立 `observability` 索引元数据组合；原始日志正文不进入业务 schema，也不复用 Ops `AuditRecord`。
20. `observability` 索引数据库默认使用 PostgreSQL 独立 schema 或独立 database；SQLite 只允许作为单机诊断索引，Elastic/OpenSearch/ClickHouse 等只作为外部增强 adapter。

## Rationale

1. 这组基础设施兼顾私有化可落地性、社区成熟度与后续演进空间，适合作为通用应用管理平台的默认基线。
2. 在同一 PostgreSQL 集群下按服务隔离 schema 或服务级 database，能在保持服务级数据边界的同时降低早期运维复杂度。
3. 统一消息、缓存、对象存储与观测方案，可以减少平台服务之间的技术碎片化。
4. 将日志、审计与事务数据拆开，可以避免诊断需求、合规需求与业务一致性需求相互污染。
5. 通过事件而不是共享表传播状态，能从机制上维持服务边界的可持续性。
6. FusionCache 能在统一 Redis 基线之上提供 L1/L2 混合缓存、缓存击穿保护、超时控制、失效同步和 OpenTelemetry 集成，适合 Gateway 聚合查询、权限快照、配置字典和读侧投影等高频读场景。
7. `ILogger` 抽象保证业务代码不绑定日志实现；Serilog 与 OpenTelemetry 组合能在本地、容器和私有化环境中保持结构化字段、trace 关联和后端可替换性。
8. 现代容器和私有化部署通常让应用写 stdout/stderr 或 OTLP，再由节点/Collector/Agent 采集、缓冲、转发到日志后端；这种方式比应用直接写数据库更容易统一限流、重试、保留、归档和替换后端。
9. 内置归档 profile 使用平台已经需要的 File Storage 和 PostgreSQL 元数据能力，可以覆盖 Docker 和无容器脚本安装；同时把日志正文的大容量存储留在对象或文件系统层，避免业务库成为日志库。
10. PostgreSQL 已是平台事务数据默认依赖，作为日志索引元数据存储不会引入新基础设施；它的分区、B-tree 和 BRIN 索引能力足以覆盖首版按时间、服务、实例、操作任务和 correlationId 定位 chunk 的需求。

## Consequences

1. 早期部署会引入较完整的基础设施套件，本地开发与 CI 环境需要提供标准编排。
2. 团队需要显式管理 schema 边界、事件契约与观测规范，而不是依赖数据库便利性进行跨服务联动。
3. 某些 Microsoft-first 或信创客户环境可能会要求 SQL Server、GaussDB、DMDB 或其它数据库替代，这会形成后续 profile 适配工作。
4. 统一基线有助于减少认知负担，但也意味着少量特殊服务不能随意自行选型。
5. 团队需要显式管理缓存键、TTL、tag、失效策略和 fail-safe 边界，避免缓存把权限、会话、动作状态或审计事实变成隐形真相源。
6. 团队需要通过 File Storage 统一治理文件访问和保留策略，避免各服务直接散落访问 MinIO 形成隐形权限边界。
7. 数据库替换目标是低成本、低感知，而不是完全无感；provider 包、连接串、迁移、SQL 方言、JSON/日期时间映射、CAP outbox 存储和测试基础设施都必须按 profile 显式验证。
8. 运维需要为每个部署 profile 明确日志热存储、归档、检索和清理策略；这些策略不能和审计记录、业务事务数据使用同一张表或同一保留周期。
9. 本地文件和 Aspire Dashboard profile 只能作为开发、PoC、弱联网和短期诊断兜底；一旦进入多节点、长期保留或生产可用性要求，必须重新评估持久化后端、副本、磁盘容量、备份、清理和告警。
10. 内置归档 profile 能保证日志可保留和可按上下文定位，但不承诺替代专业日志平台的全文检索、复杂聚合和超大规模分析能力。

## Implementation Notes

1. infra 层需要提供 PostgreSQL、Redis、RabbitMQ、MinIO、Qdrant、OpenTelemetry 的本地开发编排；完整平台拓扑后续通过平台级 Aspire AppHost 承接，Docker Compose 作为生成或兜底目标。
2. backend/services/FileStorage 需要封装文件元数据、上传下载授权、Upload Provider 抽象和 MinIO/object storage 适配；业务服务不直接把对象存储 key、tus endpoint 或 S3 multipart 细节当成长期公开契约。
3. backend/common/Caching 需要封装 FusionCache 的服务注册、Redis L2、Redis backplane、序列化、OpenTelemetry、缓存键命名和默认策略。
4. backend/common/Observability 需要定义 tracing、metrics、structured logging 的统一接入方式。
5. 跨服务 IntegrationEvent 默认采用 CAP outbox 或等价可靠发布机制，outbox 记录必须与领域事实处于同一事务边界。
6. 持久化代码必须把 provider 选择收敛在 Infrastructure/Program 注册层；Domain、Application、Endpoint、SDK 和公开契约不得依赖 PostgreSQL、GaussDB、DMDB 或其它 provider 的专有 API。
7. 第一、第二、第三阶段可以只验证同步 API 与本地纵切链路；一旦某个业务能力以跨服务 IntegrationEvent 驱动状态变化，发布方必须具备可靠发布，消费者必须具备幂等处理。
8. 日志字段至少保留 `service.name`、`environment`、`traceId`、`spanId`、`correlationId`、`organizationId`、`environmentId`、`userId` 或 `actor`、`operationTaskId`、`instanceKey` 等可用字段；不存在的字段不伪造。
9. 日志不得记录 access token、refresh token、密码、密钥、完整连接串、个人敏感信息、文件内容或大体积 payload。需要追溯业务动作时写 Ops/AuditRecord 或对应领域事实，不用日志替代审计。
10. 本地文件兜底必须配置滚动策略、单文件大小上限、保留文件数或保留天数，并把日志目录排除在业务数据迁移和数据库备份流程之外。
11. 若启用 Aspire Dashboard profile，必须明确它是内存态短期 telemetry UI，不用于长期日志检索或审计保留；生产 profile 应另行选择持久化日志后端或通过平台受控索引实现。
12. 观测 profile 必须覆盖 Aspire AppHost、Docker Compose 和安装包/脚本三类部署入口；任何观测 UI 都不得成为无容器安装的硬依赖。
13. Log Archive Worker 只能处理已关闭或稳定 checkpoint 的滚动日志文件，上传前必须压缩、计算 `sha256` 并记录时间范围、服务、实例、level、`operationTaskId`、`correlationId`、`traceId` 和过期时间等索引元数据。
14. `observability` 索引只保存定位与过滤所需元数据以及可选 message preview，不保存完整原始日志正文；完整日志通过 File Storage chunk 读取。
15. `LogChunk` 是默认必需索引；`LogEntryIndex` 是可选加速索引。未启用细粒度索引时，Gateway 通过 `LogChunk` 缩小候选文件块后扫描 `.jsonl.gz` 内容返回结果。
16. `LogChunk` 首版索引模型必须保持跨数据库可迁移，避免把 JSONB、GIN、trigram、全文检索等 PostgreSQL 专有能力写成默认契约；这些能力只允许作为 PostgreSQL profile 的可选优化。

## Out of Scope

1. 不在本 ADR 中规定数据库表结构、索引细节与迁移执行流程。
2. 不在本 ADR 中决定各环境的备份周期、日志保留时长与告警阈值。
3. 不在本 ADR 中决定 Microsoft-first 或信创环境下的具体替代产品选型、验收证书和生产迁移脚本。
4. 不在本 ADR 中冻结每个业务查询的缓存键、TTL 和 tag 明细；这些规则由 docs/architecture/caching-baseline.md 和具体服务实现承接。
