# 部署基线

本文档承接 ADR 0008，定义 Nerv-IIP 的部署目标、工程落点与交付边界。它描述后续部署能力应如何演进，不代表当前第一、第二阶段纵切已经具备生产部署能力。脚本可信执行、分类、副作用声明和诊断门禁按 ADR 0010 与 docs/architecture/script-automation-governance.md 执行。

## 目标

1. 面向更多客户环境，而不是只支持单一容器部署。
2. 使用统一部署模型描述服务拓扑，避免 Aspire、Docker Compose、安装包和脚本各自漂移。
3. 保持服务边界和发布边界清晰，尤其是主平台与 Connector Host 的独立性。
4. 让本地开发、联调、PoC、私有化交付和受限环境安装拥有可追踪的入口。

## 部署目标矩阵

| 部署目标 | 主要场景 | 入口 | 边界要求 |
| --- | --- | --- | --- |
| Aspire | 本地开发、联调、Dashboard、服务发现、生成部署产物 | 平台级 AppHost | 只能有一个平台级编排入口，不为每个服务生成局部 AppHost。 |
| Docker Compose | PoC、小规模私有化、容器化单机部署、演示环境 | Aspire 生成的 Compose 或受控 overlay | 不把手写 compose 作为长期拓扑真相源。 |
| 安装包 | 无容器或传统运维环境 | Windows Service、systemd、zip/tar/deb/rpm 等制品 | 主平台服务与 Connector Host 分开分发。 |
| 整合安装脚本 | 实施交付、离线或弱联网部署、环境初始化 | PowerShell、Bash | 脚本负责检查、配置、初始化、注册服务、启动和诊断。 |

## 统一部署模型

1. 平台级 Aspire AppHost 是首选拓扑模型，负责描述服务、基础设施依赖、连接关系和观测资源。
2. netcorepal-web 模板创建的领域服务继续传入 `--UseAspire false`，表示不生成服务级 AppHost。
3. AppHost 不拥有领域规则，不替代 IAM、AppHub、Ops、File Storage 等服务边界。
4. Docker Compose 产物优先从 AppHost 生成；需要客户环境差异时，通过 overlay、参数、环境文件或安装脚本补充。
5. 安装包与整合安装脚本必须消费同一套配置口径，不能发明只在某个脚本中存在的隐式配置。
6. 数据库迁移、初始化、seed 和回滚策略按 docs/adr/0009-database-migration-release-and-seed-strategy.md 与 docs/architecture/database-release-runbook.md 执行；部署入口不得把 `EnsureCreated()` 当作生产建表流程。
7. 部署相关脚本必须通过脚本自动化治理；AppHost、Compose、安装包和脚本可以是不同入口，但不能绕过同一套超时、日志、进程清理和敏感信息处理要求。
8. 生产和 PoC 部署 profile 必须为 PlatformGateway、Connector Host、Ops、FileStorage、Notification 配置同一组 `InternalService:BearerToken`（环境变量形态为 `InternalService__BearerToken`），用于内部服务调用认证；不得把该 token 写入仓库、Compose 明文模板或脚本日志。

## 工程落点

```text
Nerv-IIP/
  infra/
    aspire/
      Nerv.IIP.AppHost/
    docker-compose.dev.yml
    compose/
    observability/
  scripts/
    lib/
      ScriptAutomation.ps1
    check-script-governance.ps1
    install/
      windows/
      linux/
    verify-*.ps1
```

当前 `infra/docker-compose.dev.yml` 是本地依赖服务兜底入口。平台级 AppHost 已新增到 `infra/aspire/Nerv.IIP.AppHost`；开发联调和 Compose 生成应逐步以该 AppHost 为拓扑真相源。

## Aspire AppHost 范围

当前平台级 AppHost 已覆盖 PlatformGateway、AppHub、IAM、Ops、FileStorage、Connector Host、Console、PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector。首批完整 AppHost 后续应继续覆盖：

1. Notification、Knowledge、AI Integration 和 Qdrant。
2. 可选 .NET Aspire Dashboard。
3. Aspire Dashboard 只作为微软官方、自托管、开源免费的短期观测 UI，不作为生产日志持久化后端。

后续 Notification、Knowledge、AI Integration 等能力进入可运行状态后，应纳入同一 AppHost，而不是新增第二套平台编排入口。

## 日志持久化

1. 服务运行日志默认通过 Console 和 OpenTelemetry/OTLP 输出，由 OpenTelemetry Collector 统一接收。
2. 本地开发和第四阶段验证只要求 Collector 可接收 OTLP；可选启用 .NET Aspire Dashboard 查看短期 telemetry，不强制预置第三方日志检索后端。
3. PoC、私有化和生产部署必须在部署 profile 中选择日志后端、索引策略、保留周期和清理任务；这些选择不影响业务代码。
4. 日志不得写入业务 PostgreSQL schema，也不得复用 Ops `AuditRecord` 表。审计事实、业务事实和诊断日志保持独立存储与独立 retention。
5. 运维日志包、诊断包、导出包和长期归档附件通过 File Storage 保存到 MinIO 或等价对象存储，只在业务表中保留 `fileId`/`FileReference`。
6. 无观测后端时的最低兜底是滚动 JSONL 文件：按大小和日期滚动，限制保留文件数或保留天数，仅用于短期现场诊断。
7. 若需要本地可视化排障，优先提供 `aspire-dashboard` 部署 profile：通过微软官方容器镜像接收 OTLP 并展示结构化日志、trace 和 metric；该 profile 只面向开发、联调、PoC 和短期诊断。
8. OpenTelemetry Collector 可以启用本地 `file_storage` 持久化队列，用于后端短时不可用时的发送缓冲；它不是日志查询库，也不替代日志后端。
9. InfluxDB 不作为默认日志后端；SQLite 只允许作为开发诊断或 agent/collector 内部队列实现细节，不用于多服务日志检索、告警或生产保留。

## 内置日志持久化方案

Nerv-IIP 默认提供一个不依赖第三方日志平台的内置持久化 profile。它不是替代专业日志平台的全文检索引擎，而是保证私有化、Docker 和脚本安装环境具备可保留、可下载、可按上下文定位的日志能力。

这个方案是二层存储，不是单纯文件存储，也不是把每条日志正文写进数据库：

| 层 | 默认存储 | 存什么 | 不存什么 |
| --- | --- | --- | --- |
| 日志正文 | 本机滚动 JSONL、File Storage `.jsonl.gz` chunk | 完整结构化日志行、可下载诊断包 | 不放入业务 PostgreSQL schema |
| 查询索引 | PostgreSQL 独立 `observability` schema，或等价独立元数据存储 | chunk 目录、时间范围、服务、实例、`operationTaskId`、`correlationId`、`traceId`、level、`fileId`、过期时间 | 不保存完整原始日志正文 |
| 页面查询 | PlatformGateway | 受控过滤、分页、脱敏、按索引定位 chunk 并扫描返回 | 不暴露 File Storage object key 或内部表结构 |

1. 热日志：每个服务宿主通过 Serilog File sink 写滚动 JSONL 文件，目录按 `service.name`、部署环境、实例或节点分区；滚动策略按日期和大小双限制，保留天数、单文件大小和总占用上限由部署 profile 配置。
2. 采集可靠性：OpenTelemetry Collector 可启用 `file_storage` 作为发送队列，降低短时后端不可用造成的丢失；它只保障转发，不作为查询存储。
3. 归档 worker：关闭后的滚动日志文件由 Log Archive Worker 压缩为 `.jsonl.gz` chunk，计算 `sha256`、时间范围、行数、level 范围和标签摘要后上传到 File Storage。File Storage 的 provider 可以是 MinIO、客户对象存储或脚本安装下的本地文件目录。
4. 查询索引：平台维护独立 `observability` schema 或等价独立元数据存储，只保存 `LogChunk`、可选 `LogEntryIndex`、`fileId`、时间范围、服务、实例、`operationTaskId`、`correlationId`、`traceId`、level、保留到期时间等索引字段。原始日志正文不写入 AppHub、Ops、Iam、FileStorage 等业务 schema。
5. 控制台查询：PlatformGateway 先查索引定位 chunk，再通过 File Storage 读取压缩日志块，在受限时间窗口内扫描并返回平台中立 DTO。近实时查看可先读取热日志或 Aspire Dashboard 短期 telemetry；历史查看走归档 chunk。
6. 清理策略：日志保留由 profile 定义，清理任务必须同时删除 File Storage 对象和 `observability` 索引记录。审计记录、业务事务数据和诊断日志可以拥有不同 retention。
7. 替换策略：客户已有日志平台时，可以新增 Gateway adapter 直接查询客户平台；内置归档仍可作为兜底或关闭。前端契约不随后端选择变化。

最小索引模型：

1. `LogChunk` 是必需索引，表示一个压缩日志块：`chunkId`、`fileId`、`serviceName`、`environmentName`、`nodeId`、`instanceKey`、`fromUtc`、`toUtc`、`lineCount`、`byteSize`、`sha256`、`levels`、`retentionUntilUtc`、`archivedAtUtc`。
2. `LogEntryIndex` 是可选索引，只在需要更快按上下文定位时启用：`chunkId`、`timestampUtc`、`level`、`operationTaskId`、`correlationId`、`traceId`、`instanceKey`、`byteOffset` 或 `lineNumber`、截断后的 `messagePreview`。
3. 默认先实现 `LogChunk` 粗粒度索引；查询时按时间和上下文缩小 chunk 范围，再扫描 `.jsonl.gz` 内容。只有日志量上来、扫描成本不可接受时，才启用 `LogEntryIndex` 细粒度索引。

## 索引数据库选型

1. 默认索引数据库使用 PostgreSQL，落在独立 `observability` schema；若客户要求隔离，也可以拆成独立 `observability` database。它只保存日志定位元数据，不保存完整日志正文。
2. 选择 PostgreSQL 的原因是：平台默认已经依赖 PostgreSQL，能覆盖 Aspire、Docker Compose 和脚本安装；它支持时间范围查询、组合 B-tree 索引、按时间分区、BRIN 等适合追加型索引数据的能力，且不引入新的运维组件。
3. `LogChunk` 默认按 `fromUtc` 或 `archivedAtUtc` 做月分区或日分区；常用索引包括 `(serviceName, fromUtc, toUtc)`、`(instanceKey, fromUtc)`、`(operationTaskId, fromUtc)`、`(correlationId, fromUtc)`、`(traceId, fromUtc)`。大批量按时间追加时，可补 BRIN 索引降低索引体积。
4. 为兼容后续 GaussDB、DMDB 等 database profile，`LogChunk` 首版只使用普通列、时间范围、字符串和数字字段；JSONB、GIN、trigram、全文检索等 PostgreSQL 专有能力只能作为 PostgreSQL profile 的可选优化，不进入跨数据库最小模型。
5. SQLite 只允许作为无中心数据库的单机诊断索引或临时导入工具，不作为多服务部署、生产保留和控制台共享查询的默认索引数据库。
6. Elastic、OpenSearch、ClickHouse 等可以作为外部日志平台或增强索引后端，通过 PlatformGateway adapter 接入；启用这些后端时，可以关闭或弱化内置 `observability` 索引，但前端契约不变。
7. Redis 不作为日志索引数据库；它可以用于短期缓存查询结果或游标状态，但不能承担持久化索引和 retention 清理事实。

## 观测后端资源分层

1. `collector-only` 是默认第四阶段 profile：只保证日志、trace、metric 能被 OpenTelemetry Collector 接收和转发，资源要求低，主要受采集量、批处理、重试队列和是否启用 `file_storage` 影响；它不提供日志查询 UI。
2. `aspire-dashboard` 是默认推荐的本地观测 UI profile：它同时符合微软官方、自部署、开源免费和社区活跃等优先特征，可通过 Aspire CLI 或 `mcr.microsoft.com/dotnet/aspire-dashboard` 容器运行，接收 OTLP/gRPC 和 OTLP/HTTP。
3. `aspire-dashboard` 只用于开发、联调、PoC 和短期诊断；其 telemetry 存储是内存态，超过限制会丢弃，进程重启后不保留，因此不能承诺生产级日志持久化、长期检索或审计保留。
4. Grafana、Loki、Elasticsearch、Seq、ClickHouse 等第三方观测后端不作为 Nerv-IIP 默认选型；客户已有平台可以通过 Gateway adapter 接入，但不能成为产品前端或第四阶段交付的硬依赖。
5. 微软官方、可自部署、开源免费、稳定活跃是观测后端选型优先级，不是所有候选都必须同时满足的准入门槛。候选方案缺失其中任一特征时，必须在后续 ADR 中说明取舍原因、替代路径和退出成本。
6. 当前冻结内置归档 profile 作为默认持久化方案，不冻结第三方生产级日志后端。若后续需要专业全文检索、复杂聚合或超大规模日志分析，应在选型 ADR 中明确部署方式、授权模型、保留策略和迁移路径。

## 观测部署目标

| 部署目标 | 默认观测入口 | 日志 UI | 持久化边界 |
| --- | --- | --- | --- |
| Aspire AppHost | AppHost 负责连接服务、基础设施和 OTLP endpoint | 使用 AppHost/Aspire Dashboard 进行开发、联调和 PoC 诊断 | Dashboard 为内存态短期视图；长期日志走滚动 JSONL、Log Archive Worker、File Storage chunk 和 `observability` 索引 |
| Docker Compose | Compose 或 overlay 启动 OpenTelemetry Collector；可选启动 `aspire-dashboard` 与 Log Archive Worker 容器 | `aspire-dashboard` 作为可选 service，不作为 Compose 必选项 | volumes 必须覆盖滚动日志目录和 Collector `file_storage` 目录；归档 chunk 上传 File Storage，Dashboard 不承担长期保留 |
| 安装包/脚本 | Windows Service/systemd 通过环境变量或配置文件声明 OTLP endpoint 和滚动日志目录 | 脚本可选择启动 standalone Aspire Dashboard、连接已有 Collector，或只保留滚动文件诊断 | 无容器环境必须至少具备滚动 JSONL 文件；需要集中保留时安装 Log Archive Worker 或计划任务上传 File Storage |

1. 三种部署目标都必须使用同一套日志字段、OTLP 配置键和滚动文件策略，不能因为入口不同而产生不同日志语义。
2. Docker Compose 和安装脚本可以不启用 Aspire Dashboard，但必须保留 Collector/OTLP 或滚动文件路径，保证现场可诊断。
3. 若客户环境已有日志平台，PlatformGateway 只通过 adapter 接入其查询能力；前端契约和日志 DTO 不随部署目标变化。

## 控制台日志查询

1. 控制台前端不得直接访问 Aspire Dashboard、第三方观测后端或客户侧日志平台，也不得在浏览器中暴露后端地址、凭据、租户 header 或查询语言。
2. PlatformGateway 负责把业务上下文转换为日志查询上下文：用户、组织、环境、应用实例、操作任务、correlationId、traceId、时间窗口、level 和关键字都必须经过鉴权与范围收敛。
3. Gateway 后续提供 `/api/console/v1/logs/query` 作为通用查询入口，并可以提供 `/api/console/v1/instances/{instanceKey}/logs`、`/api/console/v1/operation-tasks/{operationTaskId}/logs` 等页面级便捷入口。
4. 日志查询响应使用平台中立 DTO，不泄漏后端查询语言、内部 API、tenant header 或具体存储字段。建议字段为 `timestamp`、`level`、`service`、`instanceKey`、`operationTaskId`、`correlationId`、`traceId`、`message`、`labels`、`fields`、`nextCursor` 和 `partial`。
5. Gateway 必须限制默认时间窗口、最大时间窗口、返回条数、分页游标、并发和查询频率；对敏感字段做脱敏，不允许把 token、password、secret、connection string 等内容原样返回给前端。
6. 控制台第一版日志查看以历史查询为主：实例详情页和操作任务详情页显示日志面板，支持时间范围、level、服务、关键字、correlationId/traceId 过滤。默认查询内置归档索引和 File Storage chunk；实时 tail 可以作为后续 SSE 或 WebSocket 能力，不进入第四阶段默认验收。

## Docker Compose 规则

1. `infra/docker-compose.dev.yml` 只作为早期本地依赖编排和兜底入口。
2. 面向完整平台的 Compose 文件应从 Aspire AppHost 生成。
3. 生成的 Compose 产物进入交付前必须校验 volumes、restart policy、资源限制、健康检查、镜像 tag、secret 注入和端口暴露。
4. 不在 Compose 文件中写入真实密钥、客户域名或不可公开的环境信息。
5. 若需要按客户环境定制，优先使用 overlay、`.env`、参数文件或安装脚本生成，不复制出长期维护的平行 Compose。
6. Compose profile 至少应区分 `collector-only`、`aspire-dashboard` 和 `log-archive`：前者只启动 Collector 和滚动文件目录，`aspire-dashboard` 额外启动 Dashboard 容器并配置 OTLP 端口映射，`log-archive` 启动归档 worker 并挂载日志目录与 File Storage 配置。

## 安装包规则

1. 主平台服务和 Connector Host 是不同分发单元。
2. Windows 包默认支持注册为 Windows Service。
3. Linux 包默认支持注册为 systemd service。
4. 安装包应包含可校验版本、配置样例、健康检查说明和卸载入口。
5. Connector Host 安装包不得引用主平台源码目录；只能依赖 Platform SDK、版本化契约包、公开 API 或等价发布制品。
6. 安装包不得强依赖容器运行时。无容器环境下，观测能力至少通过滚动 JSONL 文件和可选 OTLP endpoint 配置提供；Aspire Dashboard 只能作为可选诊断组件安装或连接。需要长期保留时，安装包应包含 Log Archive Worker 或等价计划任务。

## 整合安装脚本规则

Windows 脚本默认使用 PowerShell，Linux 脚本默认使用 Bash。脚本至少需要覆盖：

1. 运行时和依赖检查。
2. 端口、目录和权限检查。
3. 配置文件或环境文件生成。
4. 数据库初始化和迁移执行入口。
5. 服务注册、启动、停止和卸载。
6. 健康检查和诊断信息输出。
7. 观测配置生成：包括 `OTEL_EXPORTER_OTLP_ENDPOINT`、日志目录、滚动策略、Collector 地址、File Storage 归档目标、归档保留天数，以及是否启用 standalone Aspire Dashboard 或 Log Archive Worker。
8. 数据库迁移和 seed 执行入口：调用服务自有 migrator 或 migration bundle，按 docs/architecture/database-release-runbook.md 输出 releaseId、service、dbProfile、targetDatabase、migration from/to、seed step、duration、correlationId、log path 和 exit code。
9. 内部服务 token 配置检查：非 Development profile 必须要求 `InternalService__BearerToken`，并在诊断输出中只显示是否已配置，不打印 token 值。

脚本必须设计为可重复执行；重复执行不应破坏已有配置或误删数据。所有安装、发布和验证脚本还必须满足 docs/architecture/script-automation-governance.md：声明 `check`、`verify`、`generate` 或 `release-install` 分类；通过共享 helper 执行高风险命令；输出诊断日志；使用作用域环境变量；清理自有进程树；禁止打印密钥、token、完整连接串或客户私有配置。

## Connector Host 部署

1. Connector Host 可以在主平台同机运行，也可以部署到受管节点。
2. Connector Host 必须支持独立安装、独立升级和独立回滚。
3. Docker Connector、Windows Service Connector、HTTP Connector 等适配能力只属于 Connector Host 侧，不进入主平台服务实现。
4. Connector Host 与主平台通过 Platform SDK、Connector Protocol、Ops Protocol、公开 HTTP API 和 IAM 授权关系协作。

## 当前阶段

1. 当前仓库已具备第一、第二、第三阶段纵切验证脚本，并新增第四阶段真实基础设施验证入口，但尚不是面向真实用户的可部署产品。
2. 当前 Compose 文件只覆盖本地依赖服务，还没有承载完整平台服务拓扑。
3. 平台级 AppHost 位于 `infra/aspire/Nerv.IIP.AppHost`，当前用于表达 PlatformGateway、AppHub、IAM、Ops、FileStorage、Connector Host、Console、PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector 的本地开发拓扑；AppHub/IAM/Ops 在 AppHost 中使用独立 PostgreSQL database resource，避免共享 database 下 `EnsureCreated()` 对既有表误判。
4. `infra/docker-compose.dev.yml` 继续作为验证脚本拉起本地依赖的稳定入口；PostgreSQL 默认映射到本机 `15432`，完整 Compose、安装包和整合安装脚本在 AppHost 拓扑稳定后补齐。
5. 第五阶段已新增 migration-based AppHub/Ops 本地验证，但客户 PoC/私有化发布前仍需按 docs/architecture/database-release-runbook.md 补齐发布脚本、备份恢复演练、seed 清单和诊断输出。
6. `scripts/verify-fourth-slice-real-infra.ps1` 是第四阶段本地验收入口，已通过 Docker Desktop 环境验证，最终输出 `Fourth vertical slice real infrastructure verified.`；若镜像拉取受限，可以先按现场网络要求配置 Docker Desktop proxy。
7. 脚本自动化治理已冻结为 ADR 0010；现有验证脚本按 docs/architecture/script-automation-governance.md 迁移到共享 helper 和静态门禁，迁移完成前不得把 legacy verify 脚本解释为 release-install 入口。
8. 本地开发统一入口收敛为根目录 `.\nerv.ps1 dev`，该命令只作为薄 CLI 包装，真实启动逻辑仍位于受脚本治理约束的 `scripts/dev.ps1`。完整平台启动走 Aspire AppHost；`.\nerv.ps1 dev -InfraOnly` 只启动 `infra/docker-compose.dev.yml` 中的依赖服务。
9. 本地 MinIO 容器镜像使用 `pgsty/minio:RELEASE.2026-04-17T00-00-00Z`，避免继续依赖停止更新的 Docker image line；FileStorage 仍通过对象存储 provider 抽象与 MinIO 或等价 S3-compatible backend 交互。

## 非目标

1. 不在本文档中冻结 Kubernetes、云厂商托管服务或 Helm Chart。
2. 不在本文档中定义客户现场的最终机器规格、网络 ACL、备份策略和密钥轮换。
3. 不要求当前阶段立即提供生产级安装包。
4. 不允许部署脚本绕过服务 API 直接写业务库来完成平台初始化。
