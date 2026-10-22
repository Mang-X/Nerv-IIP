# 技术栈资料索引

本页只维护 Nerv-IIP 长期技术栈的**用途、事实生产者和官方资料入口**。精确依赖版本、镜像 tag、运行 profile 和当前是否实际引用，必须从仓库 manifest/lockfile/project/config 读取；本页不复制版本号或阶段完成状态。

## 事实来源

| 范围 | 精确事实来源 |
| --- | --- |
| Node.js | `.node-version` |
| pnpm / 前端依赖 | `frontend/package.json`、各 workspace `package.json`、`frontend/pnpm-lock.yaml`、Vite+ workspace 配置 |
| .NET / NuGet | `backend/Directory.Packages.props`、`connector-hosts/Directory.Packages.props`、各 `.csproj` / tool manifest |
| AppHost / 基础设施 | `infra/aspire/Nerv.IIP.AppHost/**`、当前部署配置与生成产物 |
| 容器镜像 | AppHost、Compose/deploy 配置与受治理安装脚本 |
| 数据库 / 消息 /对象存储 profile | 服务配置、DI/provider 代码、migration、AppHost 与对应 provider tests |
| AI / Knowledge | 实际项目引用、相关 Architecture/ADR 与项目文件 |

技术名称出现在本页不等于“当前所有部署 profile 都启用”。判断是否已引用、可选、候选或尚未实现时，回到上述 producer。

## 后端选型导航

先按任务读取唯一决策，再核对 producer。**已接受**描述选择与责任边界，**实际依赖**由 manifest/项目引用及代码证明，**待评估**不构成选型授权。

| 任务 | 决策 / 责任边界 | 当前实现 producer |
| --- | --- | --- |
| 缓存 | [ADR 0003](../adr/0003-data-and-messaging-baseline.md)：FusionCache L1、Redis L2 与多实例 backplane；当前结构见 [Caching](../architecture/platform/caching.md) | [Caching 模块](../../backend/common/Caching)、[CPM](../../backend/Directory.Packages.props) 和消费项目引用 |
| tus / storage provider | [ADR 0023](../adr/0023-filestorage-tus-proxy-staging-final-complete-invariants.md)、[ADR 0024](../adr/0024-filestorage-storage-provider-and-local-production-semantics.md)；受控业务网关入口修订见 [ADR 0030](../adr/0030-business-gateway-purpose-scoped-file-transfer.md)。当前结构见 [FileStorage](../architecture/platform/file-storage.md) | [FileStorage 模块](../../backend/services/FileStorage) 的项目、传输与 complete 实现；决策不代表 tusdotnet 迁移已完成 |
| CSV | Sep 是实际依赖；CSV 转义交给库，列、金额、编码及导出权限仍由业务拥有 | [CPM](../../backend/Directory.Packages.props)、[Scheduling 项目引用](../../backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj)、[CSV 消费者](../../backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Queries/SchedulePlanCsv.cs)；不把旧候选讨论当作当前未决选型 |
| 通用编码 / 时间 / 数字与字符串 | [ADR 0034](../adr/0034-common-tools-and-time-toolchain.md)；Base64URL 先核对 BCL `System.Buffers.Text.Base64Url`，业务编码合同回到所属模块 | [目标框架](../../backend/Directory.Build.props)、[Base64URL 现有消费者](../../backend/common/Contracts/Nerv.IIP.Contracts.IntegrationEvents/IntegrationEventIdempotencyKey.cs)；其它实际使用回到目标项目 |
| Office/PDF、报表、发票、标签、音视频 | [ADR 0035](../adr/0035-business-documents-reports-and-media-toolchain.md)，保留各子域基线、可选与暂缓项 | [CPM](../../backend/Directory.Packages.props)、目标项目 `.csproj` / worker 配置及代码；不能由 ADR 已接受推导包已引入 |
| Redis 分布式锁 / GS1 | **待评估**；ADR 0003 的 Redis 基线不选定分布式锁产品，ZXing 图像编解码不替代 GS1 Application Identifier 业务语义 | 目标模块实现与项目依赖；不从 Redis / ZXing 名称推导锁或 GS1 方案已经选定 |

### 通用工具与文档媒体资料

下面只提供官方资料，选择理由与完整职责留在上述 ADR；名称出现不证明当前依赖存在。

| 技术 | 资料用途 | 官方资料 |
| --- | --- | --- |
| .NET BCL / TimeProvider | 平台标准类型与可测试时间 | [TimeProvider](https://learn.microsoft.com/dotnet/standard/datetime/timeprovider-overview)、[Base64Url](https://learn.microsoft.com/dotnet/api/system.buffers.text.base64url) |
| tus / tusdotnet | 续传协议与服务端实现资料 | [tus](https://tus.io/protocols/resumable-upload)、[tusdotnet](https://github.com/tusdotnet/tusdotnet) |
| Sep | CSV 读写 | [nietras/Sep](https://github.com/nietras/Sep) |
| Open XML SDK | OOXML 处理 | [Open XML SDK](https://learn.microsoft.com/office/open-xml/open-xml-sdk) |
| ClosedXML | Excel 处理 | [ClosedXML](https://docs.closedxml.io/) |
| QuestPDF | 代码优先 PDF 生成 | [QuestPDF](https://www.questpdf.com/) |
| PDFsharp | PDF 绘制 | [PDFsharp](https://docs.pdfsharp.net/) |
| Tiptap / ProseMirror | 正文编辑与文档结构 | [Tiptap](https://tiptap.dev/docs)、[ProseMirror](https://prosemirror.net/docs/) |
| ZXing.Net | 条码图像编解码 | [ZXing.Net](https://github.com/micjahn/ZXing.Net) |
| FFmpeg / ffprobe | 媒体处理与探测 | [FFmpeg documentation](https://ffmpeg.org/documentation.html) |
| PdfPig / PaddleOCR | 可选 adapter / worker 资料 | [PdfPig](https://github.com/UglyToad/PdfPig)、[PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) |

## 前端

| 技术 | 用途 / 边界 | 官方文档 | 上游仓库 |
| --- | --- | --- | --- |
| Node.js | 前端工具链运行时 | [Node.js](https://nodejs.org/api/) | [nodejs/node](https://github.com/nodejs/node) |
| pnpm | 前端 workspace 包管理 | [pnpm](https://pnpm.io/) | [pnpm/pnpm](https://github.com/pnpm/pnpm) |
| Vite+ | workspace check/fmt/lint/test/run 工具链 | [Vite+](https://viteplus.dev/) | [voidzero-dev/vite-plus](https://github.com/voidzero-dev/vite-plus) |
| Vite | 应用 dev/build 配置兼容层 | [Vite](https://vite.dev/guide/) | [vitejs/vite](https://github.com/vitejs/vite) |
| Vitest | 前端单元/组件测试 runner | [Vitest](https://vitest.dev/guide/) | [vitest-dev/vitest](https://github.com/vitest-dev/vitest) |
| TypeScript | 前端语言与类型检查 | [TypeScript](https://www.typescriptlang.org/docs/) | [microsoft/TypeScript](https://github.com/microsoft/TypeScript) |
| Vue | Console/PDA Web UI 运行时 | [Vue](https://vuejs.org/guide/introduction.html) | [vuejs/core](https://github.com/vuejs/core) |
| Vue Router | 前端路由 | [Vue Router](https://router.vuejs.org/guide/) | [vuejs/router](https://github.com/vuejs/router) |
| Pinia | 客户端状态 | [Pinia](https://pinia.vuejs.org/) | [vuejs/pinia](https://github.com/vuejs/pinia) |
| Pinia Colada | 服务端状态/查询层；是否启用插件能力以当前 package/config 为准 | [Pinia Colada](https://pinia-colada.esm.dev/) | [posva/pinia-colada](https://github.com/posva/pinia-colada) |
| Hey API OpenAPI TypeScript | OpenAPI → 前端客户端生成 | [openapi-ts](https://heyapi.dev/openapi-ts/get-started) | [hey-api/openapi-ts](https://github.com/hey-api/openapi-ts) |
| shadcn-vue | Console UI 组件体系 | [shadcn-vue](https://www.shadcn-vue.com/docs/) | [unovue/shadcn-vue](https://github.com/unovue/shadcn-vue) |
| VueUse | 可复用组合式工具候选/依赖；实际使用看 workspace manifest | [VueUse](https://vueuse.org/guide/) | [vueuse/vueuse](https://github.com/vueuse/vueuse) |
| es-toolkit | 通用数据处理；已接受选择见 [ADR 0034](../adr/0034-common-tools-and-time-toolchain.md)，实际使用看 workspace manifest | [es-toolkit](https://es-toolkit.dev/) | [toss/es-toolkit](https://github.com/toss/es-toolkit) |

## Mobile PDA

PDA 容器/插件边界仍以当前 Capacitor 配置和 [`../architecture/mobile/capacitor.md`](../architecture/mobile/capacitor.md) 为准。

| 技术 | 用途 / 边界 | 官方文档 | 上游仓库 |
| --- | --- | --- | --- |
| Capacitor | Android 优先 PDA WebView/native shell | [Capacitor](https://capacitorjs.com/docs) / [Android](https://capacitorjs.com/docs/android) / [Support policy](https://capacitorjs.com/docs/main/reference/support-policy) | [ionic-team/capacitor](https://github.com/ionic-team/capacitor) |
| Capacitor 官方插件 | Camera、Device、Filesystem、Network 等原生桥；实际依赖看 PDA package | [APIs](https://capacitorjs.com/docs/apis) / [Android plugins](https://capacitorjs.com/docs/plugins/android) | [ionic-team/capacitor-plugins](https://github.com/ionic-team/capacitor-plugins) |
| Zebra DataWedge | Zebra/DataWedge 兼容设备硬件扫码集成路径 | [Intent Output](https://techdocs.zebra.com/datawedge/latest/guide/output/intent/) / [Barcode Input](https://techdocs.zebra.com/datawedge/latest/guide/input/barcode/) | 厂商能力 |
| Android Enterprise | 受管 PDA 私有应用分发/托管配置候选 | [Private apps](https://support.google.com/work/android/answer/9495634?hl=en) / [Managed configurations](https://developer.android.com/work/managed-configurations?hl=en) | 平台能力 |

## 后端与平台

| 技术 | 用途 / 边界 | 官方文档 | 上游仓库 |
| --- | --- | --- | --- |
| .NET SDK | 后端/Connector Host 构建运行时 | [.NET](https://learn.microsoft.com/dotnet/) | [dotnet/sdk](https://github.com/dotnet/sdk) |
| ASP.NET Core | HTTP host 与认证授权基础 | [ASP.NET Core](https://learn.microsoft.com/aspnet/core/) | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| ASP.NET Core Authentication / Authorization | 平台 authn/authz primitives | [Authentication](https://learn.microsoft.com/aspnet/core/security/authentication/) / [Authorization](https://learn.microsoft.com/aspnet/core/security/authorization/introduction) | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| FastEndpoints | HTTP endpoint 框架 | [FastEndpoints](https://fast-endpoints.com/) | [FastEndpoints/FastEndpoints](https://github.com/FastEndpoints/FastEndpoints) |
| FastEndpoints.Swagger | OpenAPI 生成 | [Swagger support](https://fast-endpoints.com/docs/swagger-support) | [FastEndpoints/FastEndpoints](https://github.com/FastEndpoints/FastEndpoints) |
| netcorepal-cloud-framework | CleanDDD / 分布式事务等后端基础 | [netcorepal-cloud-framework](https://netcorepal.github.io/netcorepal-cloud-framework/) | [netcorepal/netcorepal-cloud-framework](https://github.com/netcorepal/netcorepal-cloud-framework) |
| NetCorePal.Template | 服务脚手架参考；可选数据库能力不自动等同于本仓支持矩阵 | [NuGet](https://www.nuget.org/packages/NetCorePal.Template) | [netcorepal-cloud-template](https://github.com/netcorepal/netcorepal-cloud-template) |
| .NET Aspire / AppHost | 本地开发与部署拓扑生产者 | [.NET Aspire](https://learn.microsoft.com/dotnet/aspire/) | [dotnet/aspire](https://github.com/dotnet/aspire) |
| Aspire Dashboard | 本地/集成/PoC 诊断 UI；不作为长期日志存储 | [Standalone Dashboard](https://aspire.dev/dashboard/standalone/) | [microsoft/aspire](https://github.com/microsoft/aspire) |
| PowerShell | 受治理脚本运行时 | [PowerShell](https://learn.microsoft.com/powershell/) | [PowerShell/PowerShell](https://github.com/PowerShell/PowerShell) |
| OpenTelemetry | telemetry 采集与上下文基线 | [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/) | [open-telemetry/opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet) |
| Serilog | 宿主级结构化日志 provider；业务代码依旧面向 logging abstraction | [Serilog](https://serilog.net/) | [serilog/serilog](https://github.com/serilog/serilog) |
| VictoriaLogs | 内置 logs-only 存储/查询后端之一；精确镜像与 endpoint 看部署 producer | [VictoriaLogs](https://docs.victoriametrics.com/victorialogs/) / [OTLP](https://docs.victoriametrics.com/victorialogs/data-ingestion/opentelemetry/) / [LogsQL](https://docs.victoriametrics.com/victorialogs/querying/) | [VictoriaMetrics/VictoriaLogs](https://github.com/VictoriaMetrics/VictoriaLogs) |
| FusionCache | 缓存抽象 | [FusionCache](https://fusioncache.net/) | [ZiggyCreatures/FusionCache](https://github.com/ZiggyCreatures/FusionCache) |

## 数据、消息与存储

| 技术 | 用途 / 边界 | 官方文档 | 上游仓库 |
| --- | --- | --- | --- |
| PostgreSQL | 主要持久化 profile；支持边界以 migrations/provider tests 为准 | [PostgreSQL](https://www.postgresql.org/docs/current/) | [postgres/postgres](https://github.com/postgres/postgres) |
| GaussDB / DMDB | 模板/信创候选；是否支持由本仓 provider、migration、CAP 与集成测试证明 | [NetCorePal.Template](https://www.nuget.org/packages/NetCorePal.Template) | [template.json](https://github.com/netcorepal/netcorepal-cloud-template/blob/main/template/.template.config/template.json) |
| Redis | cache/session；配置为相应 messaging provider 时也可承担 CAP transport | [Redis](https://redis.io/docs/latest/) | [redis/redis](https://github.com/redis/redis) |
| RabbitMQ | 可选消息 broker profile | [RabbitMQ](https://www.rabbitmq.com/docs) | [rabbitmq/rabbitmq-server](https://github.com/rabbitmq/rabbitmq-server) |
| DotNetCore.CAP.RedisStreams | Redis Streams CAP transport；精确包版本看 `Directory.Packages.props` / lock | [CAP Redis Streams](https://cap.dotnetcore.xyz/user-guide/en/transport/redis-streams/) / [NuGet](https://www.nuget.org/packages/DotNetCore.CAP.RedisStreams) | [dotnetcore/CAP](https://github.com/dotnetcore/CAP) |
| Savorboard.CAP.InMemoryMessageQueue | Development-only messaging profile 的实现候选/依赖 | [CAP In-Memory](https://cap.dotnetcore.xyz/user-guide/en/transport/in-memory-queue/) | [dotnetcore/CAP](https://github.com/dotnetcore/CAP) |
| S3-compatible object storage | FileStorage / archive 对象存储相关能力；实际 provider 语义看 FileStorage 代码/ADR | [S3 compatibility](https://docs.min.io/community/minio-object-store/reference/s3-api-compatibility.html) | [pgsty/minio](https://hub.docker.com/r/pgsty/minio) |
| Qdrant | Knowledge/RAG 向量存储候选/依赖；实际启用看项目/AppHost | [Qdrant](https://qdrant.tech/documentation/) | [qdrant/qdrant](https://github.com/qdrant/qdrant) |

## AI 与知识

| 技术 | 用途 / 边界 | 官方文档 | 上游仓库 |
| --- | --- | --- | --- |
| Microsoft.Extensions.AI | .NET AI abstraction | [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/ai-extensions) | [dotnet/extensions](https://github.com/dotnet/extensions) |
| Microsoft.Extensions.DataIngestion | Knowledge ingestion abstraction/candidate | [Data ingestion](https://learn.microsoft.com/dotnet/ai/conceptual/data-ingestion) | [dotnet/extensions](https://github.com/dotnet/extensions) |
| Microsoft.Extensions.VectorData | Vector store abstraction | [Vector stores](https://learn.microsoft.com/dotnet/ai/vector-stores/overview) | [dotnet/extensions](https://github.com/dotnet/extensions) |

## 更新规则

- 引入或移除长期使用的框架、运行时、数据库、broker、SDK 或代码生成工具时，复核本页的用途和官方资料入口。
- 只在实现期短暂使用的 package 不进入本索引，以 manifest/lockfile 为事实来源。
- 不在本页记录“Phase N 已完成”、具体 CI 通过次数、当前 Issue 状态或精确依赖版本；需要历史证明时使用冻结 Report/Git，当前值回到 producer。
