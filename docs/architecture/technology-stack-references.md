# 技术栈参考链接

本文档为 Nerv-IIP 当前已落地和已冻结规划技术栈的链接索引。每个条目都尽量给出官方文档与源码仓库，避免同名生态、旧仓库或社区分叉造成歧义。

## 仓库（Repository）

| 项目（Item） | 链接（Link） |
|---|---|
| Nerv-IIP 仓库（repository） | [Mang-X/Nerv-IIP](https://github.com/Mang-X/Nerv-IIP) |
| 当前基线（Current baseline） | 当前能力基线随主平台实现持续演进；最新实施状态、阶段（Phase 8）控制台能力和统一本地开发入口见 [implementation-readiness.md](implementation-readiness.md)。 |

## 前端（Frontend）

| 技术（Technology） | 当前状态（Current status） | 文档（Documentation） | 仓库（Repository） |
|---|---|---|---|
| Node.js | 必需。`.node-version` 固定为 `22.22.3`；Vite+ 在 lint/fmt 路径加载 TypeScript 配置时要求 `>=22.18.0`。 | [Node.js 文档](https://nodejs.org/api/) | [nodejs/node](https://github.com/nodejs/node) |
| pnpm | 必需的包管理器；`packageManager` 将其固定为 `pnpm@11.13.1`。 | [pnpm 文档](https://pnpm.io/) | [pnpm/pnpm](https://github.com/pnpm/pnpm) |
| Vite+ | 必需的工作区工具链，用于 check、fmt、lint、test 和 run 任务。 | [Vite+ 文档](https://viteplus.dev/) | [voidzero-dev/vite-plus](https://github.com/voidzero-dev/vite-plus) |
| Vite | 通过 Vite+ 核心覆盖使用；应用级 dev/build 配置仍与 Vite 兼容。 | [Vite 文档](https://vite.dev/guide/) | [vitejs/vite](https://github.com/vitejs/vite) |
| Vitest | 官方 Vitest 固定为 Vite+ 内置的精确版本，使 `vp test` 与工作区包共用一个 runner 实例。 | [Vitest 文档](https://vitest.dev/guide/) | [vitest-dev/vitest](https://github.com/vitest-dev/vitest) |
| TypeScript | 必需的前端语言和类型检查基线。 | [TypeScript 文档](https://www.typescriptlang.org/docs/) | [microsoft/TypeScript](https://github.com/microsoft/TypeScript) |
| Vue | 必需的控制台运行时，使用 Vue 3 Composition API。 | [Vue 文档](https://vuejs.org/guide/introduction.html) | [vuejs/core](https://github.com/vuejs/core) |
| Vue Router | 必需的路由器，使用官方文件路由插件和类型化路由。 | [Vue Router 文档](https://router.vuejs.org/guide/) | [vuejs/router](https://github.com/vuejs/router) |
| Pinia | 必需的客户端状态存储。 | [Pinia 文档](https://pinia.vuejs.org/) | [vuejs/pinia](https://github.com/vuejs/pinia) |
| Pinia Colada | 必需的服务端状态/查询层。 | [Pinia Colada 文档](https://pinia-colada.esm.dev/) | [posva/pinia-colada](https://github.com/posva/pinia-colada) |
| Pinia Colada Auto Refetch | OperationTask 轮询行为所必需。 | [Pinia Colada 文档](https://pinia-colada.esm.dev/) | [posva/pinia-colada](https://github.com/posva/pinia-colada) |
| Hey API OpenAPI TypeScript | 生成前端 API 客户端所必需。 | [Hey API openapi-ts 文档](https://heyapi.dev/openapi-ts/get-started) | [hey-api/openapi-ts](https://github.com/hey-api/openapi-ts) |
| VueUse | 已冻结的前端规划基线；仅在出现真实的组合式函数需求时引入。 | [VueUse 文档](https://vueuse.org/guide/) | [vueuse/vueuse](https://github.com/vueuse/vueuse) |
| shadcn-vue | Console Auth 和后续控制台 UI 工作所必需的 UI 系统基线；已在 `frontend/packages/ui` 以 `reka-nova` 风格初始化，并提供稳定的 `@nerv-iip/ui` 导出。 | [shadcn-vue 文档](https://www.shadcn-vue.com/docs/) | [unovue/shadcn-vue](https://github.com/unovue/shadcn-vue) |
| es-toolkit | 已冻结的工具库规划基线；仅在其能替代真实的本地工具复杂度时引入。 | [es-toolkit 文档](https://es-toolkit.dev/) | [toss/es-toolkit](https://github.com/toss/es-toolkit) |

## 移动 PDA（Mobile PDA）

| 技术（Technology） | 当前状态（Current status） | 文档（Documentation） | 仓库（Repository） |
|---|---|---|---|
| Capacitor | 面向 Android 优先 PDA 应用开发的已选规划基线；见 [mobile-pda-capacitor-architecture.md](mobile-pda-capacitor-architecture.md)。 | [Capacitor 文档](https://capacitorjs.com/docs) / [Android 指南](https://capacitorjs.com/docs/android) / [支持策略](https://capacitorjs.com/docs/main/reference/support-policy) | [ionic-team/capacitor](https://github.com/ionic-team/capacitor) |
| Capacitor 官方插件 | 常用原生 API 的已选基线，包括 Barcode Scanner、Camera、Device、Filesystem、File Transfer、Network、Preferences 和 Push Notifications。PDA 硬件扫描头仍需要厂商 intent/SDK 适配器。 | [官方插件](https://capacitorjs.com/docs/apis) / [Android 插件指南](https://capacitorjs.com/docs/plugins/android) | [ionic-team/capacitor-plugins](https://github.com/ionic-team/capacitor-plugins) |
| Zebra DataWedge | 面向 Zebra 或 DataWedge 兼容 Android PDA 设备的推荐首选硬件扫码集成路径。 | [Intent Output（意图输出）](https://techdocs.zebra.com/datawedge/latest/guide/output/intent/) / [Barcode Input（条码输入）](https://techdocs.zebra.com/datawedge/latest/guide/input/barcode/) | 厂商运行时和示例；本单体仓库尚无仓库基线。 |
| Android Enterprise | 当客户环境允许时，面向受管 PDA 设备群的推荐生产分发和远程配置路径。 | [私有应用分发](https://support.google.com/work/android/answer/9495634?hl=en) / [托管配置](https://developer.android.com/work/managed-configurations?hl=en) | 平台能力，不是项目依赖。 |

## 后端与平台（Backend And Platform）

| 技术（Technology） | 当前状态（Current status） | 文档（Documentation） | 仓库（Repository） |
|---|---|---|---|
| .NET SDK | 必需的后端目标 SDK；项目目标为 `net10.0`。 | [.NET 文档](https://learn.microsoft.com/dotnet/) | [dotnet/sdk](https://github.com/dotnet/sdk) |
| ASP.NET Core | Web 项目必需的 HTTP 宿主/运行时表面。 | [ASP.NET Core 文档](https://learn.microsoft.com/aspnet/core/) | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| ASP.NET Core Authentication/Authorization | 必需的平台安全基线；IAM 持久化认证、Gateway 权限执行、Console Auth 流程、OIDC 回调/MFA 钩子和资源范围 ABAC 授权已存在，而完整 OAuth/OIDC 授权服务器、WebAuthn 和复杂策略语言仍是后续工作。 | [认证文档](https://learn.microsoft.com/aspnet/core/security/authentication/) / [授权文档](https://learn.microsoft.com/aspnet/core/security/authorization/introduction) | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| FastEndpoints | 平台 HTTP API 所必需的 endpoint 框架。 | [FastEndpoints 文档](https://fast-endpoints.com/) | [FastEndpoints/FastEndpoints](https://github.com/FastEndpoints/FastEndpoints) |
| FastEndpoints.Swagger | Gateway OpenAPI 生成的必需路径。 | [FastEndpoints Swagger 文档](https://fast-endpoints.com/docs/swagger-support) | [FastEndpoints/FastEndpoints](https://github.com/FastEndpoints/FastEndpoints) |
| netcorepal-cloud-framework | 平台领域服务所必需的后端架构基线；AppHub 和 Ops 已在第四切片中采用 CleanDDD/netcorepal 结构。 | [netcorepal-cloud-framework 文档](https://netcorepal.github.io/netcorepal-cloud-framework/) | [netcorepal/netcorepal-cloud-framework](https://github.com/netcorepal/netcorepal-cloud-framework) |
| Aspire AppHost | 必需的部署/开发编排基线；平台级 AppHost 位于 `infra/aspire/Nerv.IIP.AppHost`，当前覆盖 PlatformGateway、BusinessGateway、AppHub、IAM、Ops、FileStorage、Notification、Connector Host、Console、BusinessConsole、已登记的业务服务、PostgreSQL、Redis、可选 RabbitMQ、MinIO、VictoriaLogs 和可选 OpenTelemetry Collector。RabbitMQ 仅在 `Messaging:Provider=RabbitMQ` 配置档创建；当 `Messaging:Provider=Redis` 时，Redis 复用于 CAP。 | [.NET Aspire 文档](https://learn.microsoft.com/dotnet/aspire/) | [dotnet/aspire](https://github.com/dotnet/aspire) |
| .NET Aspire Dashboard | 已选 Microsoft 官方、可自托管、开源的短期可观测性 UI，用于本地开发、集成和 PoC 诊断；不是生产日志持久化后端。 | [Aspire Dashboard 文档](https://aspire.dev/dashboard/standalone/) | [microsoft/aspire](https://github.com/microsoft/aspire) |
| PowerShell | 必需的验证脚本运行时。 | [PowerShell 文档](https://learn.microsoft.com/powershell/) | [PowerShell/PowerShell](https://github.com/PowerShell/PowerShell) |
| OpenTelemetry | 必需的可观测性基线。 | [OpenTelemetry .NET 文档](https://opentelemetry.io/docs/languages/dotnet/) | [open-telemetry/opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet) |
| Serilog | 必需的宿主级结构化日志提供方；业务代码仍使用 `Microsoft.Extensions.Logging`。 | [Serilog 文档](https://serilog.net/) | [serilog/serilog](https://github.com/serilog/serilog) |
| VictoriaLogs | #304 所必需的内置仅日志集中存储和查询后端；固定为 `victoriametrics/victoria-logs:v1.50.0`、Apache License 2.0、OTLP 日志 endpoint `/insert/opentelemetry/v1/logs`、LogsQL 查询 endpoint `/select/logsql/query`。 | [VictoriaLogs 文档](https://docs.victoriametrics.com/victorialogs/) / [OTLP 摄取](https://docs.victoriametrics.com/victorialogs/data-ingestion/opentelemetry/) / [LogsQL 查询](https://docs.victoriametrics.com/victorialogs/querying/) | [VictoriaMetrics/VictoriaLogs](https://github.com/VictoriaMetrics/VictoriaLogs) |
| FusionCache | 必需的缓存抽象基线。 | [FusionCache 文档](https://fusioncache.net/) | [ZiggyCreatures/FusionCache](https://github.com/ZiggyCreatures/FusionCache) |
| NetCorePal.Template | 必需的后端服务脚手架参考。当前 `--Database` 可选 PostgreSQL、GaussDB 和 DMDB；Nerv-IIP 默认使用 PostgreSQL 配置档。 | [NuGet 包](https://www.nuget.org/packages/NetCorePal.Template) | [netcorepal/netcorepal-cloud-template](https://github.com/netcorepal/netcorepal-cloud-template) |

## 数据、消息与存储（Data, Messaging And Storage）

| 技术（Technology） | 当前状态（Current status） | 文档（Documentation） | 仓库（Repository） |
|---|---|---|---|
| PostgreSQL | 必需的主持久化基线；AppHub/Ops/IAM 的 PostgreSQL 配置档已通过基于迁移的验证、仅开发环境自动迁移门禁和适用时的幂等 seed 基线落地。生产迁移和 seed 流程由 ADR 0009 治理。 | [PostgreSQL 文档](https://www.postgresql.org/docs/current/) | [postgres/postgres](https://github.com/postgres/postgres) |
| GaussDB / DMDB | 模板支持的国产数据库配置档候选，用于信创验证；在本仓库验证 provider、CAP 存储、迁移和测试前，不是 Nerv-IIP 的默认配置档，也不受生产支持。 | [NetCorePal.Template 包](https://www.nuget.org/packages/NetCorePal.Template) | [netcorepal-cloud-template template.json](https://github.com/netcorepal/netcorepal-cloud-template/blob/main/template/.template.config/template.json) |
| Redis | 必需的缓存/背板基线；第四阶段 AppHost 和本地 compose 包含 Redis。当 `Messaging:Provider=Redis` 时，Redis 还承载 CAP Redis Streams，且必须使用持久化存储及 RDB/AOF 级耐久性设置。 | [Redis 文档](https://redis.io/docs/latest/) | [redis/redis](https://github.com/redis/redis) |
| RabbitMQ | 用于多实例或更高可靠性跨进程事件传递的可选消息提供方。当 Redis Streams 的耐久性、吞吐量或运维语义不足时，RabbitMQ 仍是首选 broker 配置档。 | [RabbitMQ 文档](https://www.rabbitmq.com/docs) | [rabbitmq/rabbitmq-server](https://github.com/rabbitmq/rabbitmq-server) |
| DotNetCore.CAP.RedisStreams | 用于单机或中等吞吐量部署的可选 CAP transport，此时 Redis 可同时承担 cache/session 和消息总线职责。使用包版本 `10.0.1`，与主 CAP 包对齐。 | [CAP Redis Streams 文档](https://cap.dotnetcore.xyz/user-guide/en/transport/redis-streams/) / [NuGet 包](https://www.nuget.org/packages/DotNetCore.CAP.RedisStreams/10.0.1) | [dotnetcore/CAP](https://github.com/dotnetcore/CAP) |
| Savorboard.CAP.InMemoryMessageQueue | 当 `Messaging:Provider` 省略或设为 `InMemory` 时必需的默认 Development CAP transport；CAP outbox 存储仍使用服务数据库配置档。非 Development 环境不得使用。 | [CAP In-Memory Queue 文档](https://cap.dotnetcore.xyz/user-guide/en/transport/in-memory-queue/) | [dotnetcore/CAP](https://github.com/dotnetcore/CAP) |
| S3-compatible 对象存储 | 已冻结的对象存储基线；本地开发使用 S3 兼容的本地运行时镜像 `pgsty/minio:RELEASE.2026-04-17T00-00-00Z`，而 FileStorage 继续依赖 provider 抽象以支持 MinIO、AIStor 或等效 S3 兼容后端。 | [MinIO/AIStor S3 兼容性参考](https://docs.min.io/community/minio-object-store/reference/s3-api-compatibility.html) | [pgsty/minio 本地运行时镜像](https://hub.docker.com/r/pgsty/minio) |
| Qdrant | 面向未来知识/RAG 工作的已冻结向量存储基线。 | [Qdrant 文档](https://qdrant.tech/documentation/) | [qdrant/qdrant](https://github.com/qdrant/qdrant) |

## AI 与知识（AI And Knowledge）

| 技术（Technology） | 当前状态（Current status） | 文档（Documentation） | 仓库（Repository） |
|---|---|---|---|
| Microsoft.Extensions.AI | 已冻结的 AI 集成基线。 | [Microsoft.Extensions.AI 文档](https://learn.microsoft.com/dotnet/ai/ai-extensions) | [dotnet/extensions](https://github.com/dotnet/extensions) |
| Microsoft.Extensions.DataIngestion | 已冻结的知识摄取基线；当前切片尚未实现代码。 | [数据摄取文档](https://learn.microsoft.com/dotnet/ai/conceptual/data-ingestion) | [dotnet/extensions](https://github.com/dotnet/extensions) |
| Microsoft.Extensions.VectorData | 已冻结的向量抽象基线。 | [向量存储文档](https://learn.microsoft.com/dotnet/ai/vector-stores/overview) | [dotnet/extensions](https://github.com/dotnet/extensions) |

## 更新规则（Update Rule）

新增长期使用的框架、运行时（runtime）、数据库、broker、SDK 或代码生成工具时，必须在引入依赖的同一变更中更新本文件。对于短暂的仅实现期包，应以 package manifest 和 lockfile 为事实来源，而不是扩充本参考表。
