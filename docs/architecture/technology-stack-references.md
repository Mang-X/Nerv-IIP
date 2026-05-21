# Technology Stack References

本文档为 Nerv-IIP 当前已落地和已冻结规划技术栈的链接索引。每个条目都尽量给出官方文档与源码仓库，避免同名生态、旧仓库或社区分叉造成歧义。

## Repository

| Item | Link |
|---|---|
| Nerv-IIP repository | [Mang-X/Nerv-IIP](https://github.com/Mang-X/Nerv-IIP) |
| Current baseline | 当前能力基线随主平台实现持续演进；最新实施状态、Phase 8 控制台能力和统一本地开发入口见 [implementation-readiness.md](implementation-readiness.md)。 |

## Frontend

| Technology | Current status | Documentation | Repository |
|---|---|---|---|
| Node.js | Required. `.node-version` pins `22.22.3`; Vite+ requires `>=22.18.0` for TS config loading in lint/fmt paths. | [Node.js docs](https://nodejs.org/api/) | [nodejs/node](https://github.com/nodejs/node) |
| pnpm | Required package manager, pinned by `packageManager` to `pnpm@11.1.2`. | [pnpm docs](https://pnpm.io/) | [pnpm/pnpm](https://github.com/pnpm/pnpm) |
| Vite+ | Required workspace toolchain for check, fmt, lint, test and run tasks. | [Vite+ docs](https://viteplus.dev/) | [voidzero-dev/vite-plus](https://github.com/voidzero-dev/vite-plus) |
| Vite | Used through Vite+ core override; app-level dev/build config remains Vite-compatible. | [Vite docs](https://vite.dev/guide/) | [vitejs/vite](https://github.com/vitejs/vite) |
| Vitest | Used through Vite+ test override. | [Vitest docs](https://vitest.dev/guide/) | [vitest-dev/vitest](https://github.com/vitest-dev/vitest) |
| TypeScript | Required frontend language and type-checking baseline. | [TypeScript docs](https://www.typescriptlang.org/docs/) | [microsoft/TypeScript](https://github.com/microsoft/TypeScript) |
| Vue | Required console runtime, using Vue 3 Composition API. | [Vue docs](https://vuejs.org/guide/introduction.html) | [vuejs/core](https://github.com/vuejs/core) |
| Vue Router | Required router, using official file-routing plugin and typed routes. | [Vue Router docs](https://router.vuejs.org/guide/) | [vuejs/router](https://github.com/vuejs/router) |
| Pinia | Required client-state store. | [Pinia docs](https://pinia.vuejs.org/) | [vuejs/pinia](https://github.com/vuejs/pinia) |
| Pinia Colada | Required server-state/query layer. | [Pinia Colada docs](https://pinia-colada.esm.dev/) | [posva/pinia-colada](https://github.com/posva/pinia-colada) |
| Pinia Colada Auto Refetch | Required for OperationTask polling behavior. | [Pinia Colada docs](https://pinia-colada.esm.dev/) | [posva/pinia-colada](https://github.com/posva/pinia-colada) |
| Hey API OpenAPI TypeScript | Required for generated frontend API client. | [Hey API openapi-ts docs](https://heyapi.dev/openapi-ts/get-started) | [hey-api/openapi-ts](https://github.com/hey-api/openapi-ts) |
| VueUse | Frozen frontend planning baseline; introduce only when a real composable need appears. | [VueUse docs](https://vueuse.org/guide/) | [vueuse/vueuse](https://github.com/vueuse/vueuse) |
| shadcn-vue | Required UI-system baseline for Console Auth and later console UI work; initialized in `frontend/packages/ui` with `reka-nova` style and stable `@nerv-iip/ui` exports. | [shadcn-vue docs](https://www.shadcn-vue.com/docs/) | [unovue/shadcn-vue](https://github.com/unovue/shadcn-vue) |
| es-toolkit | Frozen utility-library planning baseline; introduce only when it replaces real local utility complexity. | [es-toolkit docs](https://es-toolkit.dev/) | [toss/es-toolkit](https://github.com/toss/es-toolkit) |

## Backend And Platform

| Technology | Current status | Documentation | Repository |
|---|---|---|---|
| .NET SDK | Required backend target SDK; projects target `net10.0`. | [.NET docs](https://learn.microsoft.com/dotnet/) | [dotnet/sdk](https://github.com/dotnet/sdk) |
| ASP.NET Core | Required HTTP host/runtime surface for Web projects. | [ASP.NET Core docs](https://learn.microsoft.com/aspnet/core/) | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| ASP.NET Core Authentication/Authorization | Required platform security baseline; IAM persistent auth, Gateway permission enforcement and minimal Console login flow exist, while OAuth/OIDC, SSO, MFA and ABAC remain future work. | [Authentication docs](https://learn.microsoft.com/aspnet/core/security/authentication/) / [Authorization docs](https://learn.microsoft.com/aspnet/core/security/authorization/introduction) | [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| FastEndpoints | Required endpoint framework for platform HTTP APIs. | [FastEndpoints docs](https://fast-endpoints.com/) | [FastEndpoints/FastEndpoints](https://github.com/FastEndpoints/FastEndpoints) |
| FastEndpoints.Swagger | Required Gateway OpenAPI generation path. | [FastEndpoints Swagger docs](https://fast-endpoints.com/docs/swagger-support) | [FastEndpoints/FastEndpoints](https://github.com/FastEndpoints/FastEndpoints) |
| netcorepal-cloud-framework | Required backend architectural baseline for platform domain services; AppHub and Ops have adopted the CleanDDD/netcorepal shape in the fourth slice. | [netcorepal-cloud-framework docs](https://netcorepal.github.io/netcorepal-cloud-framework/) | [netcorepal/netcorepal-cloud-framework](https://github.com/netcorepal/netcorepal-cloud-framework) |
| Aspire AppHost | Required deployment/development orchestration baseline; platform-level AppHost exists at `infra/aspire/Nerv.IIP.AppHost` and currently covers PlatformGateway, AppHub, IAM, Ops, FileStorage, Connector Host, Console, PostgreSQL, Redis, RabbitMQ, MinIO and OpenTelemetry Collector. | [.NET Aspire docs](https://learn.microsoft.com/dotnet/aspire/) | [dotnet/aspire](https://github.com/dotnet/aspire) |
| .NET Aspire Dashboard | Selected Microsoft-official, self-hostable, open-source short-term observability UI for local development, integration and PoC diagnostics; not a production log persistence backend. | [Aspire Dashboard docs](https://aspire.dev/dashboard/standalone/) | [microsoft/aspire](https://github.com/microsoft/aspire) |
| PowerShell | Required verification-script runtime. | [PowerShell docs](https://learn.microsoft.com/powershell/) | [PowerShell/PowerShell](https://github.com/PowerShell/PowerShell) |
| OpenTelemetry | Required observability baseline. | [OpenTelemetry .NET docs](https://opentelemetry.io/docs/languages/dotnet/) | [open-telemetry/opentelemetry-dotnet](https://github.com/open-telemetry/opentelemetry-dotnet) |
| Serilog | Required host-level structured logging provider; business code still uses `Microsoft.Extensions.Logging`. | [Serilog docs](https://serilog.net/) | [serilog/serilog](https://github.com/serilog/serilog) |
| FusionCache | Required cache abstraction baseline. | [FusionCache docs](https://fusioncache.net/) | [ZiggyCreatures/FusionCache](https://github.com/ZiggyCreatures/FusionCache) |
| NetCorePal.Template | Required backend service scaffold reference. Current `--Database` choices include PostgreSQL, GaussDB and DMDB; Nerv-IIP defaults to PostgreSQL profile. | [NuGet package](https://www.nuget.org/packages/NetCorePal.Template) | [netcorepal/netcorepal-cloud-template](https://github.com/netcorepal/netcorepal-cloud-template) |

## Data, Messaging And Storage

| Technology | Current status | Documentation | Repository |
|---|---|---|---|
| PostgreSQL | Required primary persistence baseline; AppHub/Ops PostgreSQL profile is implemented for fourth-stage verification. Production migrations and seed flow are governed by ADR 0009. | [PostgreSQL docs](https://www.postgresql.org/docs/current/) | [postgres/postgres](https://github.com/postgres/postgres) |
| GaussDB / DMDB | Template-supported domestic database profile candidates for 信创 validation; not the default profile and not production-supported in Nerv-IIP until provider, CAP storage, migrations and tests are verified in this repo. | [NetCorePal.Template package](https://www.nuget.org/packages/NetCorePal.Template) | [netcorepal-cloud-template template.json](https://github.com/netcorepal/netcorepal-cloud-template/blob/main/template/.template.config/template.json) |
| Redis | Required cache/backplane baseline; fourth-stage AppHost and local compose include Redis, while individual service cache behavior remains staged by feature need. | [Redis docs](https://redis.io/docs/latest/) | [redis/redis](https://github.com/redis/redis) |
| RabbitMQ | Required messaging baseline; fourth-stage AppHost, compose and AppHub/Ops CAP wiring include RabbitMQ, but business integration-event outbox behavior remains follow-up scope. | [RabbitMQ docs](https://www.rabbitmq.com/docs) | [rabbitmq/rabbitmq-server](https://github.com/rabbitmq/rabbitmq-server) |
| S3-compatible object storage | Frozen object-storage baseline; local development uses the S3-compatible local runtime image `pgsty/minio:RELEASE.2026-04-17T00-00-00Z`, while FileStorage continues to depend on the provider abstraction for MinIO, AIStor or equivalent S3-compatible backends. | [MinIO/AIStor S3 compatibility reference](https://docs.min.io/community/minio-object-store/reference/s3-api-compatibility.html) | [pgsty/minio local runtime image](https://hub.docker.com/r/pgsty/minio) |
| Qdrant | Frozen vector-store baseline for future knowledge/RAG work. | [Qdrant docs](https://qdrant.tech/documentation/) | [qdrant/qdrant](https://github.com/qdrant/qdrant) |

## AI And Knowledge

| Technology | Current status | Documentation | Repository |
|---|---|---|---|
| Microsoft.Extensions.AI | Frozen AI integration baseline. | [Microsoft.Extensions.AI docs](https://learn.microsoft.com/dotnet/ai/ai-extensions) | [dotnet/extensions](https://github.com/dotnet/extensions) |
| Microsoft.Extensions.DataIngestion | Frozen knowledge-ingestion baseline; code not yet implemented in current slices. | [Data ingestion docs](https://learn.microsoft.com/dotnet/ai/conceptual/data-ingestion) | [dotnet/extensions](https://github.com/dotnet/extensions) |
| Microsoft.Extensions.VectorData | Frozen vector abstraction baseline. | [Vector store docs](https://learn.microsoft.com/dotnet/ai/vector-stores/overview) | [dotnet/extensions](https://github.com/dotnet/extensions) |

## Update Rule

When adding a new long-lived framework, runtime, database, broker, SDK, or code-generation tool, update this file in the same change that introduces the dependency. For transient implementation-only packages, prefer package manifests and lockfiles as the source of truth instead of expanding this reference table.
