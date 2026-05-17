# Fourth Vertical Slice Real Infrastructure Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将第一、第二、第三阶段已经跑通的 AppHub、Ops、Gateway、Connector Host 和 Console 链路迁移到可验证的 netcorepal-first 真实基础设施底座，优先完成 PostgreSQL profile、结构化日志与 OpenTelemetry 输出、平台级 Aspire AppHost、框架代码分析入口和 database profile 边界。

**Architecture:** 本阶段不扩展业务范围，先把 AppHub 和 Ops 当前内存态事实迁移到符合 netcorepal/CleanDDD 的 Domain aggregate、Application command/query、Infrastructure repository/ApplicationDbContext 形态；PostgreSQL 作为首个 database profile，provider 选择只存在于 Infrastructure DI extension、配置、测试和部署脚本中。验证脚本用本地 `infra/docker-compose.dev.yml` 拉起依赖并证明数据跨 DbContext 生命周期存在。Aspire AppHost 作为统一拓扑入口落到 `infra/aspire/Nerv.IIP.AppHost`，但不替代既有验证脚本；日志采用 Console/OTLP 优先，本地滚动 JSONL 文件是第四阶段必须实现的持久化兜底；内置长期持久化目标是 Log Archive Worker 将关闭后的日志压缩成 File Storage chunk，并在 PostgreSQL 独立 `observability` schema 或 database 中记录可查询元数据索引；可选 .NET Aspire Dashboard 作为短期观测 UI；观测 profile 必须同时覆盖 Aspire AppHost、Docker Compose 和安装包/脚本三类部署入口；控制台日志查看通过 PlatformGateway 后续受控 API 接入，不让前端直连 Aspire Dashboard 或第三方观测后端；Gateway、Connector Host、Contracts/SDK、frontend api-client 和 console 保持轻量契约边界，不强行采用完整 netcorepal 三项目模型。

**Tech Stack:** .NET 10、netcorepal-cloud-framework/NetCorePal 3.3.0、FastEndpoints、MediatR、Entity Framework Core 10.0.8、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1、NetCorePal CAP PostgreSQL storage 3.3.0、Serilog.AspNetCore、Serilog.Sinks.OpenTelemetry、Serilog.Sinks.File、OpenTelemetry Collector、.NET Aspire Dashboard optional short-term observability UI、PostgreSQL 17 primary profile、Redis 7、RabbitMQ 4、PowerShell、Docker Compose、Aspire.Hosting 13.3.3、pnpm 10.13.1、Vue 3、Hey API。GaussDB/DMDB are documented as template-supported future profiles, not implemented in this stage.

---

## Completion Record

2026-05-17 本阶段真实基础设施门禁已通过：

```powershell
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

最终输出：

```text
Fourth vertical slice real infrastructure verified.
```

验证环境使用 Docker Desktop；镜像拉取受限时已通过 Docker Desktop proxy 指向 `http://127.0.0.1:10808` 解决。PostgreSQL 本机端口改为 `15432`，避免与本机已有 `5432` PostgreSQL 冲突。AppHub 与 Ops 在 AppHost 和验证脚本中使用独立 database（默认 `nerv_iip_apphub` / `nerv_iip_ops`，第四阶段脚本使用 `nerv_iip_apphub_verify` / `nerv_iip_ops_verify`），避免共享 database 下 `EnsureCreated()` 因既有 schema/table 漏建服务表。

## Execution Status

2026-05-17 当前执行进度：

1. Tasks 1-5 已完成并通过本地 restore/build/test、AppHub/Ops in-memory regression、PostgreSQL profile smoke（未设置真实 PostgreSQL 时早返回）、code-analysis smoke 和 reviewer 复核。
2. Task 6 脚本已落地并通过：`scripts/verify-second-slice-ops.ps1` 与 `scripts/verify-third-slice-console.ps1` 支持 `-UsePostgres`，`scripts/verify-fourth-slice-real-infra.ps1` 会拉起 PostgreSQL、Redis、RabbitMQ、重建 verify database 并运行真实基础设施门禁，`.codex/environments/environment.toml` 已增加第四阶段 action。
3. Task 7 平台级 AppHost 已落地到 `infra/aspire/Nerv.IIP.AppHost`，AppHub/Ops 使用独立 PostgreSQL database resource，并已通过 `dotnet restore infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj` 与 `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`。
4. `/code-analysis` 已从 `Program.cs` 的 Minimal API 写法收敛到 FastEndpoints endpoint，`dotnet test backend/tests/Nerv.IIP.FastEndpoints.Architecture.Tests/Nerv.IIP.FastEndpoints.Architecture.Tests.csproj --no-build`、AppHub/Ops CodeAnalysis smoke tests 和 `pwsh scripts/verify-second-slice-ops.ps1` 均已通过。
5. `pwsh scripts/verify-fourth-slice-real-infra.ps1` 已完整通过：AppHub/Ops PostgreSQL profile tests、backend/connector-hosts 串行 solution tests、Gateway OpenAPI 导出、frontend api-client 生成、typecheck/test/build 和真实 AppHub/Ops/Gateway/Connector Host 联调均通过。

## Current Gate

当前第四阶段真实基础设施门禁已经通过：

```powershell
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

当前限制：

1. 第四阶段仍使用 `EnsureCreated()` 做本地纵切验证；生产级迁移发布、回滚和种子数据初始化流程仍在后续持久化硬化阶段。
2. IAM 完整授权、FileStorage 真实上传下载、CAP outbox、审批、通知和高风险动作不进入本阶段。
3. GaussDB、DMDB、Kingbase、OceanBase 等信创数据库 profile 只冻结边界与替换约束，尚未实现生产 adapter。
4. AppHost 当前覆盖 AppHub、Ops、Gateway、Connector Host、PostgreSQL、Redis 和 RabbitMQ；OpenTelemetry Collector、Log Archive Worker、长期日志索引和控制台日志查询仍按部署基线进入后续阶段。

## Scope

### In This Plan

1. 引入 NetCorePal、EF Core、Npgsql、CAP RabbitMQ/CAP PostgreSQL storage、Serilog/OpenTelemetry sink 包版本，并保持 Central Package Management；provider 包只作为 PostgreSQL profile 的实现依赖。
2. 将 AppHub 当前事实迁移为 netcorepal/CleanDDD 形态：Domain aggregate、Infrastructure repository/ApplicationDbContext、Web Application command/query、Endpoint 通过 mediator 调用。
3. 将 Ops 当前事实迁移为 netcorepal/CleanDDD 形态：Domain aggregate、Infrastructure repository/ApplicationDbContext、Web Application command/query、Endpoint 通过 mediator 调用。
4. 保留现有内存验证链路作为回归门禁，但新 PostgreSQL 路径必须基于 `ApplicationDbContext : AppDbContextBase`、`AddUnitOfWork<ApplicationDbContext>()`、NetCorePal repository 和 CAP storage。
5. 增加 PostgreSQL profile 集成测试，验证事实跨 DbContext 生命周期存在，并证明命令处理器不手写 `SaveChanges`。
6. 为 AppHub/Ops 增加 netcorepal code-analysis endpoint，用于生成命令、聚合、事件、处理器流向图。
7. 固化后端日志口径：业务代码使用 `ILogger<T>`，宿主层使用 Serilog provider，日志通过 Console 与 OpenTelemetry/OTLP 输出到 Collector；无生产观测后端时使用滚动 JSONL 文件兜底，需要本地观测 UI 时使用可选 .NET Aspire Dashboard profile；日志不写业务 PostgreSQL。
8. 增加 `scripts/verify-fourth-slice-real-infra.ps1`，拉起本地依赖并运行真实基础设施门禁。
9. 创建平台级 Aspire AppHost，覆盖 PostgreSQL、Redis、RabbitMQ、AppHub、Ops、Gateway 和 Connector Host；OpenTelemetry Collector 作为后续观测 profile 资源继续由部署基线承接。
10. 在文档和代码注册层固化 `Persistence:Provider` 边界，默认值为 `InMemory`，第四阶段脚本显式切到 `PostgreSQL`。
11. 固化日志查看的后续契约边界：控制台只消费 PlatformGateway `/api/console/**`，Gateway 代理日志后端并负责鉴权、租户隔离、限流、脱敏、分页和时间窗口限制。
12. 固化内置日志持久化目标：滚动 JSONL 热日志、Log Archive Worker、File Storage `.jsonl.gz` chunk、PostgreSQL 独立 `observability` 索引元数据和 Gateway 查询代理；第四阶段只要求落地滚动 JSONL，归档 worker 和查询页作为后续实现。
13. 固化观测部署入口：Aspire AppHost 使用 AppHost/Dashboard，Docker Compose 通过 `collector-only`、`aspire-dashboard` 与 `log-archive` profile/overlay，安装包和脚本通过 OTLP endpoint、滚动日志目录、File Storage 归档目标和可选 standalone Dashboard 配置实现。
14. 更新 README、实施状态、部署基线、API 契约规范和 `.codex/environments/environment.toml` 的第四阶段入口。

### Outside This Plan

1. 不把 IAM 登录、JWT、refresh token 和权限 guard 全量迁移到 PostgreSQL。
2. 不完成 FileStorage 的真实上传下载闭环和 MinIO provider。
3. 不实现 CAP outbox、RabbitMQ 消费者、通知服务或审批 UI。
4. 不引入生产级 EF migrations 发布流程；第四阶段使用 `EnsureCreated()` 做本地纵切验证，正式迁移流程在后续持久化硬化阶段补齐。
5. 不生成生产 Compose、安装包、Windows Service 或 systemd unit。
6. 不实现 GaussDB、DMDB、Kingbase、OceanBase 等信创数据库的生产 adapter；本阶段只保证 PostgreSQL 实现不把 provider 细节泄漏到业务层，并把后续 profile 适配点留清楚。
7. 不把 PlatformGateway、Connector Host、Contracts/SDK、frontend console 强行改造成完整 netcorepal 三项目服务；这些项目只按职责消费 Web/API、观测、契约或 SDK 约定。
8. 不整包重跑 `dotnet new netcorepal-web` 覆盖已有 AppHub/Ops 代码；本阶段按模板形态迁移现有纵切，避免丢失已经验证过的契约和脚本。
9. 不在第四阶段实现产品控制台日志查询页、日志 tail、生产级 Grafana/Loki/Elastic/Seq/ClickHouse 部署、Log Archive Worker 或长期日志保留任务；本阶段只冻结 Gateway 接入边界、Aspire Dashboard 短期观测 UI、滚动 JSONL 热日志和内置归档目标设计。

## File Structure Map

```text
backend/
  Directory.Packages.props
  common/
    Observability/
      Nerv.IIP.Observability/
        NervIipObservability.cs
  services/
    AppHub/
      src/
        Nerv.IIP.AppHub.Domain/
          AggregatesModel/
            ApplicationAggregate/
            ManagedNodeAggregate/
            ApplicationInstanceAggregate/
          DomainEvents/
        Nerv.IIP.AppHub.Infrastructure/
          ApplicationDbContext.cs
          EntityConfigurations/
          Repositories/
          AppHubPersistenceServiceCollectionExtensions.cs
        Nerv.IIP.AppHub.Web/
          Application/
            Commands/
            Queries/
            DomainEventHandlers/
            IntegrationEvents/
            IntegrationEventConverters/
          Program.cs
          Endpoints/Connectors/ConnectorIngestionEndpoints.cs
          Endpoints/Instances/InstanceQueryEndpoints.cs
      tests/
        Nerv.IIP.AppHub.Domain.Tests/
          AppHubStateStoreTests.cs
        Nerv.IIP.AppHub.Web.Tests/
          AppHubPostgresProfileTests.cs
    Ops/
      src/
        Nerv.IIP.Ops.Domain/
          AggregatesModel/
            OperationTaskAggregate/
          DomainEvents/
        Nerv.IIP.Ops.Infrastructure/
          ApplicationDbContext.cs
          EntityConfigurations/
          Repositories/
          OpsPersistenceServiceCollectionExtensions.cs
        Nerv.IIP.Ops.Web/
          Application/
            Commands/
            Queries/
            DomainEventHandlers/
            IntegrationEvents/
            IntegrationEventConverters/
          Program.cs
          Endpoints/OperationTasks/OperationTaskEndpoints.cs
      tests/
        Nerv.IIP.Ops.Web.Tests/
          OpsPostgresProfileTests.cs

infra/
  aspire/
    Nerv.IIP.AppHost/
      Nerv.IIP.AppHost.csproj
      Program.cs
  docker-compose.dev.yml

scripts/
  verify-second-slice-ops.ps1
  verify-third-slice-console.ps1
  verify-fourth-slice-real-infra.ps1

.codex/environments/environment.toml
README.md
docs/architecture/deployment-baseline.md
docs/architecture/implementation-readiness.md
```

## Boundary Rules

1. AppHub and Ops Web endpoints depend on MediatR commands/queries, not concrete in-memory stores or DbContext.
2. Domain projects own aggregate roots, entities, value objects, strong typed IDs and domain events; they do not reference EF provider packages, CAP packages or infrastructure stores.
3. Infrastructure projects own `ApplicationDbContext`, entity configurations, repository interfaces/implementations and database profile registration.
4. Web projects own Endpoint、Application Commands、Queries、DomainEventHandlers、IntegrationEvents and framework registration.
5. Gateway still calls AppHub and Ops through HTTP clients; it must not reference AppHub/Ops Domain or Infrastructure.
6. Connector Host still uses Platform SDK clients; it must not reference backend service implementation projects.
7. Database provider selection is isolated to Infrastructure DI extensions, profile tests, scripts and deployment configuration; Domain/Application/Endpoint/SDK code must not reference provider-specific packages, SQL dialects or PostgreSQL-only types.
8. PostgreSQL schemas are service-owned: AppHub uses schema `apphub`, Ops uses schema `ops`; services must not read or write each other's schema.
9. Redis and RabbitMQ are introduced into AppHost topology in this phase, and CAP storage is wired through netcorepal; actual cross-service messaging behavior remains follow-up scope unless needed for framework smoke tests.
10. Business code uses `ILogger<T>` only; Serilog, Console sink, OpenTelemetry sink and deployment log backend choices stay in Host/Observability/deployment configuration.
11. Runtime logs, Ops audit facts and business transaction data use separate storage and retention strategies; logs do not write to service PostgreSQL schemas.
12. Local logging fallback is bounded: rolling JSONL files for minimum diagnostics, OpenTelemetry Collector `file_storage` queue for short-term export resilience, and optional .NET Aspire Dashboard only for short-term local telemetry viewing.
13. Built-in persistent logs use Log Archive Worker, File Storage compressed chunks and a PostgreSQL independent `observability` metadata index; raw log bodies do not enter business schemas.
14. Observability profiles must support Aspire AppHost, Docker Compose and package/script installs. Docker can run Dashboard and Log Archive Worker as optional services; direct installs must not require a container runtime.
15. Console log viewing is a Gateway concern: frontend code must use generated `/api/console/**` clients, and Gateway must proxy any Aspire Dashboard, rolling-file, built-in archive, production-backend or customer-platform query through IAM authorization, organization/environment filters, bounded time windows, paging, rate limits and redaction.
16. Log query DTOs must stay backend neutral; no LogQL, backend URL, credential, tenant header, File Storage object key or storage-specific field may leak into frontend contracts.
17. Verification must keep the existing in-memory scripts usable. PostgreSQL mode is opt-in through a fourth-stage script and explicit environment variables.

## Architecture Inputs

Read these documents before executing the tasks:

1. `docs/adr/0003-data-and-messaging-baseline.md` for PostgreSQL, Redis, RabbitMQ, MinIO, outbox and service schema boundaries.
2. `docs/architecture/backend-cleanddd-netcorepal-guidelines.md` for database profile and 信创 compatibility rules.
3. `docs/adr/0008-multi-target-deployment-and-aspire-apphost.md` for the single AppHost strategy.
4. `docs/architecture/deployment-baseline.md` for AppHost and Compose responsibility.
5. `docs/architecture/implementation-readiness.md` for current stage status and verification commands.
6. `docs/architecture/api-contract-and-codegen.md` for Gateway OpenAPI, generated frontend client and console log query API boundaries.
7. `docs/architecture/third-vertical-slice-console.md` for Gateway OpenAPI and console codegen boundaries.

## NetCorePal Adoption Decision

第四阶段从现在开始把 `netcorepal-cloud-framework` 明确为后端平台领域服务的默认框架。执行时按下表处理，不再把“只有 Web 项目使用框架”作为目标形态：

| Project kind | Fourth-stage decision | Reason |
| --- | --- | --- |
| AppHub | Adopt full netcorepal/CleanDDD shape | 已经拥有注册、心跳、状态查询和幂等事实，适合作为第一个真实持久化和 code-analysis 试点。 |
| Ops | Adopt full netcorepal/CleanDDD shape | 已经拥有任务、尝试、审计和幂等事实，和 AppHub 一起验证命令、查询、仓储、事务、测试和 database profile。 |
| Iam | Keep current slice in this plan; migrate later | IAM 认证、JWT、refresh token、权限 guard 风险面更大，第四阶段只记录为后续迁移对象。 |
| FileStorage | Keep current slice in this plan; migrate later | 需要同时处理 MinIO/provider、对象元数据和下载授权，放到真实文件闭环阶段。 |
| PlatformGateway | Do not force full netcorepal三项目结构 | Gateway 是 BFF/路由聚合层，默认只使用 ASP.NET/FastEndpoints、观测和契约消费约定；只有拥有自身持久化模型时再补 Infrastructure。 |
| Connector Host | Do not adopt full netcorepal service model | Connector Host 是可独立安装升级的 worker，通过 Platform SDK/HTTP 与平台交互，不拥有平台领域数据库。 |
| Contracts/SDK | Keep lightweight | 这些项目是跨进程契约和客户端封装，不能反向依赖服务端框架。 |
| Frontend console/api-client | Not applicable | Vue、Hey API 和 pnpm 侧只消费 OpenAPI，不引入 .NET server framework。 |

执行本计划时，如果后续任务中的旧 store 代码片段与本节冲突，以本节为准：最终态应是 Endpoint -> MediatR command/query -> repository/query handler -> `ApplicationDbContext`，而不是 Endpoint 直接注入 concrete store。内存 store 只允许作为回归测试基线或临时 adapter 存在，不能成为新功能扩展方向。

---

## Task 1: Add NetCorePal, Persistence And Analysis Package Baseline

**Files:**

- Modify: `backend/Directory.Packages.props`
- Modify: `backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj`
- Modify: `backend/common/Observability/Nerv.IIP.Observability/NervIipObservability.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/Nerv.IIP.AppHub.Domain.csproj`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Domain/Nerv.IIP.Ops.Domain.csproj`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj`

- [ ] **Step 1: Add central package versions**

Modify `backend/Directory.Packages.props` and add these entries in the existing `ItemGroup`:

```xml
<PackageVersion Include="DotNetCore.CAP.Dashboard" Version="8.4.0" />
<PackageVersion Include="DotNetCore.CAP.RabbitMQ" Version="8.4.0" />
<PackageVersion Include="FluentValidation.AspNetCore" Version="11.3.1" />
<PackageVersion Include="MediatR" Version="14.0.0" />
<PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.8" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8" />
<PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.1" />
<PackageVersion Include="NetCorePal.Context.AspNetCore" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Context.CAP" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Context.Shared" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Extensions.AspNetCore" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Extensions.CodeAnalysis" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Extensions.DistributedTransactions.CAP.PostgreSQL" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Extensions.Domain.Abstractions" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Extensions.Primitives" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Extensions.Repository.EntityFrameworkCore" Version="3.3.0" />
<PackageVersion Include="NetCorePal.Extensions.Repository.EntityFrameworkCore.Snowflake" Version="3.3.0" />
<PackageVersion Include="Serilog.AspNetCore" Version="9.0.0" />
<PackageVersion Include="Serilog.Enrichers.ClientInfo" Version="2.1.2" />
<PackageVersion Include="Serilog.Sinks.File" Version="7.0.0" />
<PackageVersion Include="Serilog.Sinks.OpenTelemetry" Version="4.1.0" />
```

The package line follows the current `NetCorePal.Template` generated package shape and the `dotnet package search` result checked on 2026-05-17. If restore reports a newer compatible template baseline during execution, update all NetCorePal packages together; do not mix minor versions.

- [ ] **Step 2: Reference netcorepal Domain packages from AppHub.Domain and Ops.Domain**

Before editing domain projects, update `backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj` with host-level logging packages:

```xml
<PackageReference Include="Serilog.AspNetCore" />
<PackageReference Include="Serilog.Enrichers.ClientInfo" />
<PackageReference Include="Serilog.Sinks.File" />
<PackageReference Include="Serilog.Sinks.OpenTelemetry" />
```

Extend `AddNervIipObservability(...)` so each host can configure Serilog from configuration, enrich logs with service name and correlation scope, write JSON to Console, write bounded rolling JSONL files when `Logging:LocalFile:Enabled=true`, and optionally write OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` or `OpenTelemetry:Endpoint` is configured. Keep `ILogger<T>` as the only API used by application code.

Modify both Domain csproj files:

- `backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/Nerv.IIP.AppHub.Domain.csproj`
- `backend/services/Ops/src/Nerv.IIP.Ops.Domain/Nerv.IIP.Ops.Domain.csproj`

Add these package references:

```xml
<PackageReference Include="NetCorePal.Extensions.CodeAnalysis" />
<PackageReference Include="NetCorePal.Extensions.Domain.Abstractions" />
<PackageReference Include="NetCorePal.Extensions.Primitives" />
```

- [ ] **Step 3: Reference netcorepal Web packages from AppHub.Web and Ops.Web**

Modify both Web csproj files:

- `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj`
- `backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj`

Add these package references alongside existing FastEndpoints references:

```xml
<PackageReference Include="DotNetCore.CAP.Dashboard" />
<PackageReference Include="DotNetCore.CAP.RabbitMQ" />
<PackageReference Include="FluentValidation.AspNetCore" />
<PackageReference Include="MediatR" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
<PackageReference Include="NetCorePal.Context.AspNetCore" />
<PackageReference Include="NetCorePal.Context.CAP" />
<PackageReference Include="NetCorePal.Context.Shared" />
<PackageReference Include="NetCorePal.Extensions.AspNetCore" />
<PackageReference Include="NetCorePal.Extensions.CodeAnalysis" />
<PackageReference Include="NetCorePal.Extensions.Primitives" />
```

- [ ] **Step 4: Reference PostgreSQL profile packages from AppHub.Infrastructure**

Modify `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Nerv.IIP.AppHub.Domain\Nerv.IIP.AppHub.Domain.csproj" />
  <ProjectReference Include="..\..\..\..\common\Contracts\Nerv.IIP.Contracts.AppHubQueries\Nerv.IIP.Contracts.AppHubQueries.csproj" />
  <ProjectReference Include="..\..\..\..\common\Contracts\Nerv.IIP.Contracts.ConnectorProtocol\Nerv.IIP.Contracts.ConnectorProtocol.csproj" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  <PackageReference Include="NetCorePal.Extensions.DistributedTransactions.CAP.PostgreSQL" />
  <PackageReference Include="NetCorePal.Extensions.Repository.EntityFrameworkCore" />
  <PackageReference Include="NetCorePal.Extensions.Repository.EntityFrameworkCore.Snowflake" />
</ItemGroup>
```

- [ ] **Step 5: Reference PostgreSQL profile packages from Ops.Infrastructure**

Modify `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Nerv.IIP.Ops.Domain\Nerv.IIP.Ops.Domain.csproj" />
  <ProjectReference Include="..\..\..\..\common\Contracts\Nerv.IIP.Contracts.ConnectorProtocol\Nerv.IIP.Contracts.ConnectorProtocol.csproj" />
  <ProjectReference Include="..\..\..\..\common\Contracts\Nerv.IIP.Contracts.Ops\Nerv.IIP.Contracts.Ops.csproj" />
</ItemGroup>

<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
  <PackageReference Include="NetCorePal.Extensions.DistributedTransactions.CAP.PostgreSQL" />
  <PackageReference Include="NetCorePal.Extensions.Repository.EntityFrameworkCore" />
  <PackageReference Include="NetCorePal.Extensions.Repository.EntityFrameworkCore.Snowflake" />
</ItemGroup>
```

- [ ] **Step 6: Restore and build backend**

Run:

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln --no-restore
```

Expected: both commands exit `0`.

- [ ] **Step 7: Commit**

```powershell
git add backend/Directory.Packages.props backend/common/Observability/Nerv.IIP.Observability backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/Nerv.IIP.AppHub.Domain.csproj backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj backend/services/Ops/src/Nerv.IIP.Ops.Domain/Nerv.IIP.Ops.Domain.csproj backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj
git commit -m "chore: add netcorepal persistence package baseline"
```

## Task 2: Map Existing AppHub Behavior Before CleanDDD Migration

> Revised netcorepal execution note: this task is no longer a final "Endpoint -> store" refactor. Use the existing `InMemoryAppHubStateStore` API as the behavior map for registration, heartbeat, state snapshot, instance list and instance detail. The implementation completed by Task 3 must end as "Endpoint -> MediatR command/query -> repository/query handler -> `ApplicationDbContext`"; any `IAppHubStateStore` introduced here is a temporary adapter for preserving tests during migration.

**Files:**

- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/AppHubFacts.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Endpoints/Connectors/ConnectorIngestionEndpoints.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Endpoints/Instances/InstanceQueryEndpoints.cs`
- Modify: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/AppHubStateStoreTests.cs`

- [ ] **Step 1: Add the AppHub store interface**

Add the interface above `InMemoryAppHubStateStore` in `AppHubFacts.cs`:

```csharp
public interface IAppHubStateStore
{
    RegistrationResult Register(ApplicationRegistration registration);
    void RecordHeartbeat(ApplicationHeartbeat heartbeat);
    void RecordStateSnapshot(InstanceStateSnapshot snapshot);
    InstanceListResponse QueryInstances(InstanceListQuery query);
    InstanceDetailResponse GetInstanceDetail(string organizationId, string environmentId, string instanceKey);
}
```

Then change the class declaration:

```csharp
public sealed class InMemoryAppHubStateStore : IAppHubStateStore
```

- [ ] **Step 2: Register the interface in AppHub.Web**

Modify `Program.cs`:

```csharp
builder.Services.AddSingleton<IAppHubStateStore, InMemoryAppHubStateStore>();
```

Remove the previous direct singleton registration.

- [ ] **Step 3: Update endpoint constructors**

In `ConnectorIngestionEndpoints.cs` and `InstanceQueryEndpoints.cs`, replace every constructor parameter of type `InMemoryAppHubStateStore` with `IAppHubStateStore`.

Example:

```csharp
public sealed class RegisterApplicationEndpoint(IAppHubStateStore store) : Endpoint<ApplicationRegistration>
```

- [ ] **Step 4: Keep domain tests on the in-memory implementation**

In `AppHubStateStoreTests.cs`, keep the concrete construction:

```csharp
var store = new InMemoryAppHubStateStore();
```

The test remains a fast behavioral baseline for the interface.

- [ ] **Step 5: Run AppHub tests**

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/Nerv.IIP.AppHub.Domain.Tests.csproj
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj
```

Expected: both commands exit `0`.

- [ ] **Step 6: Commit**

```powershell
git add backend/services/AppHub
git commit -m "refactor: introduce apphub state store interface"
```

## Task 3: Implement AppHub NetCorePal Aggregate, Repository, Commands And PostgreSQL Profile

> Revised netcorepal execution note: replace the earlier raw `PostgresAppHubStateStore` sketch with netcorepal/CleanDDD files. Keep the behavior in the old sketch as a mapping reference, but the final code should use `ApplicationDbContext : AppDbContextBase`, entity configurations, repositories based on `RepositoryBase`, command/query handlers and mediator-driven endpoints.

Target CleanDDD shape for AppHub:

1. Domain aggregates live under `Nerv.IIP.AppHub.Domain/AggregatesModel`: `Application`, `ManagedNode`, `ApplicationInstance`, `InstanceHeartbeat`, `InstanceStateHistory` and idempotency facts as aggregate-owned entities or value objects.
2. Strong typed IDs use `IGuidStronglyTypedId` unless an existing external key must stay string-based; public protocol keys remain contract strings at API boundaries.
3. Commands live under `Nerv.IIP.AppHub.Web/Application/Commands`: `RegisterApplicationCommand`, `RecordApplicationHeartbeatCommand`, `RecordInstanceStateSnapshotCommand`.
4. Queries live under `Nerv.IIP.AppHub.Web/Application/Queries`: `ListApplicationInstancesQuery`, `GetApplicationInstanceDetailQuery`.
5. Endpoints call `IMediator.Send(...)` and return the existing contract response shapes.
6. Infrastructure owns `ApplicationDbContext`, entity configurations and repositories; it implements PostgreSQL profile registration without leaking Npgsql types to Domain/Web Application code.

**Files:**

- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Repositories/*.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/AppHubPersistenceServiceCollectionExtensions.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Commands/*.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Queries/*.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Create: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubPostgresProfileTests.cs`

- [ ] **Step 1: Create the AppHub `ApplicationDbContext` and entity configurations**

Legacy mapping reference below uses a single-file row sketch. Implement the final version as `ApplicationDbContext.cs` plus `EntityConfigurations/*.cs`; keep table/schema/column intent equivalent.

```csharp
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.AppHub.Infrastructure;

public sealed class AppHubDbContext(DbContextOptions<AppHubDbContext> options) : DbContext(options)
{
    public DbSet<AppHubApplicationRow> Applications => Set<AppHubApplicationRow>();
    public DbSet<AppHubNodeRow> Nodes => Set<AppHubNodeRow>();
    public DbSet<AppHubInstanceRow> Instances => Set<AppHubInstanceRow>();
    public DbSet<AppHubCapabilityManifestRow> CapabilityManifests => Set<AppHubCapabilityManifestRow>();
    public DbSet<AppHubLivenessRow> Liveness => Set<AppHubLivenessRow>();
    public DbSet<AppHubStateHistoryRow> StateHistory => Set<AppHubStateHistoryRow>();
    public DbSet<AppHubIdempotencyRow> Idempotency => Set<AppHubIdempotencyRow>();
    public DbSet<AppHubStatusChangeRow> StatusChanges => Set<AppHubStatusChangeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("apphub");
        modelBuilder.Entity<AppHubApplicationRow>().HasKey(x => new { x.OrganizationId, x.EnvironmentId, x.ApplicationKey });
        modelBuilder.Entity<AppHubNodeRow>().HasKey(x => new { x.OrganizationId, x.EnvironmentId, x.NodeKey });
        modelBuilder.Entity<AppHubInstanceRow>().HasKey(x => x.InstanceKey);
        modelBuilder.Entity<AppHubCapabilityManifestRow>().HasKey(x => x.InstanceKey);
        modelBuilder.Entity<AppHubLivenessRow>().HasKey(x => x.InstanceKey);
        modelBuilder.Entity<AppHubStateHistoryRow>().HasKey(x => x.StateHistoryId);
        modelBuilder.Entity<AppHubIdempotencyRow>().HasKey(x => x.IdempotencyKey);
        modelBuilder.Entity<AppHubStatusChangeRow>().HasKey(x => x.StatusChangeId);
        modelBuilder.Entity<AppHubInstanceRow>().HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ApplicationKey });
        modelBuilder.Entity<AppHubStateHistoryRow>().HasIndex(x => new { x.InstanceKey, x.ObservedAtUtc });
    }
}

public sealed class AppHubApplicationRow
{
    public required string OrganizationId { get; set; }
    public required string EnvironmentId { get; set; }
    public required string ApplicationKey { get; set; }
    public required string ApplicationName { get; set; }
    public required string VersionsJson { get; set; }
}

public sealed class AppHubNodeRow
{
    public required string OrganizationId { get; set; }
    public required string EnvironmentId { get; set; }
    public required string NodeKey { get; set; }
    public required string NodeName { get; set; }
    public required string DeploymentKind { get; set; }
}

public sealed class AppHubInstanceRow
{
    public required string OrganizationId { get; set; }
    public required string EnvironmentId { get; set; }
    public required string ApplicationKey { get; set; }
    public required string Version { get; set; }
    public required string NodeKey { get; set; }
    public required string InstanceKey { get; set; }
    public required string InstanceName { get; set; }
    public required string ReportedStatus { get; set; }
    public required string HealthStatus { get; set; }
    public required string MetadataJson { get; set; }
}

public sealed class AppHubCapabilityManifestRow
{
    public required string InstanceKey { get; set; }
    public required string CapabilitiesJson { get; set; }
}

public sealed class AppHubLivenessRow
{
    public required string InstanceKey { get; set; }
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    public bool Reachable { get; set; }
    public int LatencyMs { get; set; }
}

public sealed class AppHubStateHistoryRow
{
    public required string StateHistoryId { get; set; }
    public required string InstanceKey { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; }
    public required string ReportedStatus { get; set; }
    public required string HealthStatus { get; set; }
    public required string Summary { get; set; }
}

public sealed class AppHubIdempotencyRow
{
    public required string IdempotencyKey { get; set; }
    public required string RegistrationId { get; set; }
    public required string InstanceKey { get; set; }
}

public sealed class AppHubStatusChangeRow
{
    public required string StatusChangeId { get; set; }
    public required string InstanceKey { get; set; }
    public required string PreviousStatus { get; set; }
    public required string CurrentStatus { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
}
```

- [ ] **Step 2: Create AppHub repositories and command/query handlers**

Legacy mapping reference below shows the old store-shaped algorithm. Implement the final version through repositories and handlers:

1. `RegisterApplicationCommandHandler` performs idempotency lookup, creates/updates application, node, instance and capability facts, and returns `RegistrationResult`.
2. `RecordApplicationHeartbeatCommandHandler` updates heartbeat/liveness facts.
3. `RecordInstanceStateSnapshotCommandHandler` records status history.
4. `ListApplicationInstancesQueryHandler` and `GetApplicationInstanceDetailQueryHandler` return the existing contract response types.

Do not inject this store into endpoints in the final implementation:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Domain;
using Nerv.IIP.Contracts.AppHubQueries;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Infrastructure;

public sealed class PostgresAppHubStateStore(AppHubDbContext db) : IAppHubStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public RegistrationResult Register(ApplicationRegistration registration)
    {
        var existing = db.Idempotency.SingleOrDefault(x => x.IdempotencyKey == registration.IdempotencyKey);
        if (existing is not null)
        {
            return new RegistrationResult(existing.RegistrationId, existing.InstanceKey);
        }

        var registrationId = $"reg-{db.Idempotency.Count() + 1:000000}";
        var app = db.Applications.SingleOrDefault(x =>
            x.OrganizationId == registration.Context.OrganizationId
            && x.EnvironmentId == registration.Context.EnvironmentId
            && x.ApplicationKey == registration.ApplicationKey);

        if (app is null)
        {
            db.Applications.Add(new AppHubApplicationRow
            {
                OrganizationId = registration.Context.OrganizationId,
                EnvironmentId = registration.Context.EnvironmentId,
                ApplicationKey = registration.ApplicationKey,
                ApplicationName = registration.ApplicationName,
                VersionsJson = JsonSerializer.Serialize(new[] { registration.Version }, JsonOptions)
            });
        }
        else
        {
            var versions = JsonSerializer.Deserialize<HashSet<string>>(app.VersionsJson, JsonOptions) ?? [];
            versions.Add(registration.Version);
            app.ApplicationName = registration.ApplicationName;
            app.VersionsJson = JsonSerializer.Serialize(versions.Order(StringComparer.Ordinal), JsonOptions);
        }

        UpsertNode(registration);
        UpsertInstance(registration);
        UpsertCapabilities(registration);
        db.Idempotency.Add(new AppHubIdempotencyRow { IdempotencyKey = registration.IdempotencyKey, RegistrationId = registrationId, InstanceKey = registration.InstanceKey });
        db.SaveChanges();
        return new RegistrationResult(registrationId, registration.InstanceKey);
    }

    public void RecordHeartbeat(ApplicationHeartbeat heartbeat)
    {
        EnsureInstance(heartbeat.Context.OrganizationId, heartbeat.Context.EnvironmentId, heartbeat.InstanceKey);
        var row = db.Liveness.SingleOrDefault(x => x.InstanceKey == heartbeat.InstanceKey);
        if (row is null)
        {
            db.Liveness.Add(new AppHubLivenessRow { InstanceKey = heartbeat.InstanceKey, LastHeartbeatAtUtc = heartbeat.HeartbeatAtUtc, Reachable = heartbeat.Reachable, LatencyMs = heartbeat.LatencyMs });
        }
        else
        {
            row.LastHeartbeatAtUtc = heartbeat.HeartbeatAtUtc;
            row.Reachable = heartbeat.Reachable;
            row.LatencyMs = heartbeat.LatencyMs;
        }
        db.SaveChanges();
    }

    public void RecordStateSnapshot(InstanceStateSnapshot snapshot)
    {
        var instance = EnsureInstance(snapshot.Context.OrganizationId, snapshot.Context.EnvironmentId, snapshot.InstanceKey);
        db.StateHistory.Add(new AppHubStateHistoryRow
        {
            StateHistoryId = Guid.NewGuid().ToString("n"),
            InstanceKey = snapshot.InstanceKey,
            ObservedAtUtc = snapshot.ObservedAtUtc,
            ReportedStatus = snapshot.ReportedStatus,
            HealthStatus = snapshot.HealthStatus,
            Summary = snapshot.Summary
        });
        if (instance.ReportedStatus != "unknown" && instance.ReportedStatus != snapshot.ReportedStatus)
        {
            db.StatusChanges.Add(new AppHubStatusChangeRow
            {
                StatusChangeId = Guid.NewGuid().ToString("n"),
                InstanceKey = snapshot.InstanceKey,
                PreviousStatus = instance.ReportedStatus,
                CurrentStatus = snapshot.ReportedStatus,
                ChangedAtUtc = snapshot.ObservedAtUtc
            });
        }
        instance.ReportedStatus = snapshot.ReportedStatus;
        instance.HealthStatus = snapshot.HealthStatus;
        instance.MetadataJson = JsonSerializer.Serialize(snapshot.Metadata, JsonOptions);
        db.SaveChanges();
    }

    public InstanceListResponse QueryInstances(InstanceListQuery query)
    {
        var rows = db.Instances.AsNoTracking()
            .Where(x => x.OrganizationId == query.OrganizationId && x.EnvironmentId == query.EnvironmentId)
            .ToList()
            .Where(x =>
            {
                var app = db.Applications.AsNoTracking().Single(a => a.OrganizationId == x.OrganizationId && a.EnvironmentId == x.EnvironmentId && a.ApplicationKey == x.ApplicationKey);
                return string.IsNullOrWhiteSpace(query.Search) || app.ApplicationName.Contains(query.Search, StringComparison.OrdinalIgnoreCase) || x.InstanceName.Contains(query.Search, StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(x => db.Applications.AsNoTracking().Single(a => a.OrganizationId == x.OrganizationId && a.EnvironmentId == x.EnvironmentId && a.ApplicationKey == x.ApplicationKey).ApplicationName)
            .ThenBy(x => x.InstanceName)
            .ToList();

        var items = rows
            .Skip((Math.Max(query.PageNumber, 1) - 1) * Math.Max(query.PageSize, 1))
            .Take(Math.Max(query.PageSize, 1))
            .Select(ToListItem)
            .ToList();
        return new InstanceListResponse(query.PageNumber, query.PageSize, rows.Count, items);
    }

    public InstanceDetailResponse GetInstanceDetail(string organizationId, string environmentId, string instanceKey)
    {
        var instance = db.Instances.AsNoTracking().Single(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.InstanceKey == instanceKey);
        var app = db.Applications.AsNoTracking().Single(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.ApplicationKey == instance.ApplicationKey);
        var node = db.Nodes.AsNoTracking().Single(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.NodeKey == instance.NodeKey);
        var live = db.Liveness.AsNoTracking().SingleOrDefault(x => x.InstanceKey == instance.InstanceKey);
        var state = db.StateHistory.AsNoTracking().Where(x => x.InstanceKey == instance.InstanceKey).OrderBy(x => x.ObservedAtUtc).LastOrDefault();
        var capabilities = db.CapabilityManifests.AsNoTracking().SingleOrDefault(x => x.InstanceKey == instance.InstanceKey);
        var summaries = capabilities is null
            ? []
            : JsonSerializer.Deserialize<List<CapabilityDescriptor>>(capabilities.CapabilitiesJson, JsonOptions)!
                .Select(x => new CapabilitySummary(x.CapabilityCode, x.CapabilityVersion, x.Category, x.SupportedOperations))
                .ToList();
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(instance.MetadataJson, JsonOptions) ?? [];
        return new InstanceDetailResponse(app.ApplicationKey, app.ApplicationName, instance.Version, node.NodeKey, node.NodeName, instance.InstanceKey, instance.InstanceName, instance.ReportedStatus, instance.HealthStatus, live?.LastHeartbeatAtUtc, state?.ObservedAtUtc, summaries, metadata);
    }

    private void UpsertNode(ApplicationRegistration registration)
    {
        var row = db.Nodes.SingleOrDefault(x =>
            x.OrganizationId == registration.Context.OrganizationId
            && x.EnvironmentId == registration.Context.EnvironmentId
            && x.NodeKey == registration.NodeKey);
        if (row is null)
        {
            db.Nodes.Add(new AppHubNodeRow
            {
                OrganizationId = registration.Context.OrganizationId,
                EnvironmentId = registration.Context.EnvironmentId,
                NodeKey = registration.NodeKey,
                NodeName = registration.NodeName,
                DeploymentKind = registration.DeploymentKind
            });
            return;
        }

        row.NodeName = registration.NodeName;
        row.DeploymentKind = registration.DeploymentKind;
    }

    private void UpsertInstance(ApplicationRegistration registration)
    {
        var row = db.Instances.SingleOrDefault(x => x.InstanceKey == registration.InstanceKey);
        if (row is null)
        {
            db.Instances.Add(new AppHubInstanceRow
            {
                OrganizationId = registration.Context.OrganizationId,
                EnvironmentId = registration.Context.EnvironmentId,
                ApplicationKey = registration.ApplicationKey,
                Version = registration.Version,
                NodeKey = registration.NodeKey,
                InstanceKey = registration.InstanceKey,
                InstanceName = registration.InstanceName,
                ReportedStatus = "unknown",
                HealthStatus = "unknown",
                MetadataJson = JsonSerializer.Serialize(registration.Metadata, JsonOptions)
            });
            return;
        }

        row.OrganizationId = registration.Context.OrganizationId;
        row.EnvironmentId = registration.Context.EnvironmentId;
        row.ApplicationKey = registration.ApplicationKey;
        row.Version = registration.Version;
        row.NodeKey = registration.NodeKey;
        row.InstanceName = registration.InstanceName;
        row.MetadataJson = JsonSerializer.Serialize(registration.Metadata, JsonOptions);
    }

    private void UpsertCapabilities(ApplicationRegistration registration)
    {
        var row = db.CapabilityManifests.SingleOrDefault(x => x.InstanceKey == registration.InstanceKey);
        var capabilitiesJson = JsonSerializer.Serialize(registration.Capabilities, JsonOptions);
        if (row is null)
        {
            db.CapabilityManifests.Add(new AppHubCapabilityManifestRow { InstanceKey = registration.InstanceKey, CapabilitiesJson = capabilitiesJson });
            return;
        }

        row.CapabilitiesJson = capabilitiesJson;
    }

    private AppHubInstanceRow EnsureInstance(string organizationId, string environmentId, string instanceKey) =>
        db.Instances.SingleOrDefault(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.InstanceKey == instanceKey)
        ?? throw new InvalidOperationException($"Instance context is invalid: {instanceKey}");

    private InstanceListItem ToListItem(AppHubInstanceRow instance)
    {
        var app = db.Applications.AsNoTracking().Single(x => x.OrganizationId == instance.OrganizationId && x.EnvironmentId == instance.EnvironmentId && x.ApplicationKey == instance.ApplicationKey);
        var node = db.Nodes.AsNoTracking().Single(x => x.OrganizationId == instance.OrganizationId && x.EnvironmentId == instance.EnvironmentId && x.NodeKey == instance.NodeKey);
        var live = db.Liveness.AsNoTracking().SingleOrDefault(x => x.InstanceKey == instance.InstanceKey);
        var state = db.StateHistory.AsNoTracking().Where(x => x.InstanceKey == instance.InstanceKey).OrderBy(x => x.ObservedAtUtc).LastOrDefault();
        return new InstanceListItem(app.ApplicationKey, app.ApplicationName, instance.Version, node.NodeKey, node.NodeName, instance.InstanceKey, instance.InstanceName, instance.ReportedStatus, instance.HealthStatus, live?.LastHeartbeatAtUtc, state?.ObservedAtUtc);
    }
}
```

- [ ] **Step 3: Add AppHub netcorepal persistence DI**

Final DI must include `AddRepositories(typeof(ApplicationDbContext).Assembly)`, `AddUnitOfWork<ApplicationDbContext>()`, `AddContext().AddEnvContext().AddCapContextProcessor()`, `AddIntegrationEvents(typeof(Program)).UseCap<ApplicationDbContext>(...)`, and `AddCap(...UseNetCorePalStorage<ApplicationDbContext>()...)` when PostgreSQL mode is enabled. The legacy extension below is a provider switch sketch; update type names from `AppHubDbContext` to `ApplicationDbContext` and register repositories/handlers instead of a concrete `PostgresAppHubStateStore`.

Create `AppHubPersistenceServiceCollectionExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.AppHub.Domain;

namespace Nerv.IIP.AppHub.Infrastructure;

public static class AppHubPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddAppHubPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";
        if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<AppHubDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("AppHubDb")));
            services.AddScoped<IAppHubStateStore, PostgresAppHubStateStore>();
            return services;
        }

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAppHubStateStore, InMemoryAppHubStateStore>();
            return services;
        }

        throw new NotSupportedException($"Persistence provider '{provider}' is not supported by AppHub yet.");
    }
}
```

- [ ] **Step 4: Wire AppHub.Web to Infrastructure**

Modify `Nerv.IIP.AppHub.Web.csproj`:

```xml
<ProjectReference Include="..\Nerv.IIP.AppHub.Infrastructure\Nerv.IIP.AppHub.Infrastructure.csproj" />
```

Modify `Program.cs`:

```csharp
using Nerv.IIP.AppHub.Infrastructure;
```

Replace the store registration with:

```csharp
builder.Services.AddAppHubPersistence(builder.Configuration);
```

After `var app = builder.Build();`, add this development bootstrap for PostgreSQL mode:

```csharp
if (string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppHubDbContext>().Database.EnsureCreated();
}
```

- [ ] **Step 5: Add AppHub PostgreSQL integration test**

Create `AppHubPostgresProfileTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.AppHub.Infrastructure;
using Nerv.IIP.Contracts.ConnectorProtocol;

namespace Nerv.IIP.AppHub.Web.Tests;

public sealed class AppHubPostgresProfileTests
{
    [Fact]
    public void Postgres_store_persists_registration_heartbeat_and_state()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<AppHubDbContext>().UseNpgsql(connectionString).Options;
        using (var db = new AppHubDbContext(options))
        {
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            var store = new PostgresAppHubStateStore(db);
            var registration = AppHubPostgresSamples.Registration("pg-apphub-001");
            store.Register(registration);
            store.RecordHeartbeat(AppHubPostgresSamples.Heartbeat());
            store.RecordStateSnapshot(AppHubPostgresSamples.State("running", "healthy"));
        }

        using (var db = new AppHubDbContext(options))
        {
            var store = new PostgresAppHubStateStore(db);
            var detail = store.GetInstanceDetail("org-001", "env-dev", "demo-api-001");
            Assert.Equal("running", detail.ReportedStatus);
            Assert.Equal("healthy", detail.HealthStatus);
            Assert.NotNull(detail.LastHeartbeatAtUtc);
        }
    }

    private static class AppHubPostgresSamples
    {
        private static readonly ConnectorRequestContext Context = new("1.0", "1.0", "corr-pg-apphub", DateTimeOffset.Parse("2026-05-17T00:00:00Z"), "org-001", "env-dev", "connector-host-001");

        public static ApplicationRegistration Registration(string idempotencyKey) => new(
            Context,
            idempotencyKey,
            "node-001",
            "local-docker",
            "docker",
            "demo-api",
            "Demo API",
            "1.0.0",
            "demo-api-001",
            "demo-api",
            [new CapabilityDescriptor("lifecycle.restart", "1.0", "lifecycle", ["restart"], new Dictionary<string, string>())],
            new Dictionary<string, string> { ["containerId"] = "abc123" });

        public static ApplicationHeartbeat Heartbeat() => new(
            Context,
            "demo-api-001",
            DateTimeOffset.Parse("2026-05-17T00:00:05Z"),
            true,
            DateTimeOffset.Parse("2026-05-17T00:00:00Z"),
            12,
            new Dictionary<string, string>());

        public static InstanceStateSnapshot State(string reportedStatus, string healthStatus) => new(
            Context,
            "demo-api-001",
            DateTimeOffset.Parse("2026-05-17T00:00:10Z"),
            reportedStatus,
            healthStatus,
            "summary",
            new Dictionary<string, string>(),
            new Dictionary<string, decimal>(),
            new Dictionary<string, string>());
    }
}
```

- [ ] **Step 6: Run AppHub tests**

First run fast tests:

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj
```

Then with PostgreSQL available:

```powershell
$env:NERV_IIP_TEST_POSTGRES="Host=localhost;Port=5432;Database=nerv_iip_apphub_test;Username=nerv;Password=nerv"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter Postgres_store_persists_registration_heartbeat_and_state
```

Expected: both commands exit `0`.

- [ ] **Step 7: Commit**

```powershell
git add backend/services/AppHub
git commit -m "feat: persist apphub facts in postgres"
```

## Task 4: Implement Ops NetCorePal Aggregate, Repository, Commands And PostgreSQL Profile

> Revised netcorepal execution note: replace the earlier raw `PostgresOpsStateStore` sketch with netcorepal/CleanDDD files. Keep the old store behavior as a mapping reference for task creation, dispatch, result recording, audit and idempotency, but the final endpoint path must go through MediatR command/query handlers.

Target CleanDDD shape for Ops:

1. Domain aggregate lives under `Nerv.IIP.Ops.Domain/AggregatesModel/OperationTaskAggregate`: `OperationTask` is the aggregate root, with `OperationAttempt`, `AuditRecord`, failure reason and idempotency facts as owned entities/value objects.
2. Strong typed IDs use `IGuidStronglyTypedId` unless existing contract IDs must remain stable at the API boundary.
3. Commands live under `Nerv.IIP.Ops.Web/Application/Commands`: `CreateOperationTaskCommand`, `DispatchPendingOperationsCommand`, `RecordOperationResultCommand`.
4. Queries live under `Nerv.IIP.Ops.Web/Application/Queries`: `GetOperationTaskQuery` and any existing pending-task response projection.
5. Endpoints call `IMediator.Send(...)` and keep existing route/request/response contracts stable.
6. Infrastructure owns `ApplicationDbContext`, entity configurations and repositories; it implements PostgreSQL profile registration without leaking Npgsql types to Domain/Web Application code.

**Files:**

- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Domain/InMemoryOpsStateStore.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Repositories/*.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/OpsPersistenceServiceCollectionExtensions.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Commands/*.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/*.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/OperationTasks/OperationTaskEndpoints.cs`
- Create: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsPostgresProfileTests.cs`

- [ ] **Step 1: Add the Ops store interface**

Add this interface above `InMemoryOpsStateStore`:

```csharp
public interface IOpsStateStore
{
    OperationTaskResponse Create(CreateOperationTaskRequest request, DateTimeOffset now);
    OperationTaskResponse Get(string operationTaskId);
    PendingOperationTasksResponse DispatchPending(string organizationId, string environmentId, string connectorHostId, int take, DateTimeOffset now);
    OperationTaskResponse RecordResult(OperationResult result);
}
```

Then change:

```csharp
public sealed class InMemoryOpsStateStore : IOpsStateStore
```

- [ ] **Step 2: Update Ops endpoint constructors**

In `OperationTaskEndpoints.cs`, replace every constructor parameter of type `InMemoryOpsStateStore` with `IOpsStateStore`.

- [ ] **Step 3: Create Ops `ApplicationDbContext` and entity configurations**

Legacy mapping reference below uses a single-file row sketch. Implement the final version as `ApplicationDbContext.cs` plus `EntityConfigurations/*.cs`; keep table/schema/column intent equivalent.

```csharp
using Microsoft.EntityFrameworkCore;

namespace Nerv.IIP.Ops.Infrastructure;

public sealed class OpsDbContext(DbContextOptions<OpsDbContext> options) : DbContext(options)
{
    public DbSet<OpsOperationTaskRow> Tasks => Set<OpsOperationTaskRow>();
    public DbSet<OpsOperationAttemptRow> Attempts => Set<OpsOperationAttemptRow>();
    public DbSet<OpsAuditRecordRow> AuditRecords => Set<OpsAuditRecordRow>();
    public DbSet<OpsIdempotencyRow> Idempotency => Set<OpsIdempotencyRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ops");
        modelBuilder.Entity<OpsOperationTaskRow>().HasKey(x => x.OperationTaskId);
        modelBuilder.Entity<OpsOperationAttemptRow>().HasKey(x => x.AttemptId);
        modelBuilder.Entity<OpsAuditRecordRow>().HasKey(x => x.AuditRecordId);
        modelBuilder.Entity<OpsIdempotencyRow>().HasKey(x => x.IdempotencyScope);
        modelBuilder.Entity<OpsOperationTaskRow>().HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.Status, x.RequestedAtUtc });
        modelBuilder.Entity<OpsOperationAttemptRow>().HasIndex(x => x.OperationTaskId);
        modelBuilder.Entity<OpsAuditRecordRow>().HasIndex(x => new { x.OperationTaskId, x.OccurredAtUtc });
    }
}

public sealed class OpsOperationTaskRow
{
    public required string OperationTaskId { get; set; }
    public required string OrganizationId { get; set; }
    public required string EnvironmentId { get; set; }
    public required string InstanceKey { get; set; }
    public required string OperationCode { get; set; }
    public required string Status { get; set; }
    public required string RequestedBy { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string CorrelationId { get; set; }
    public required string ParametersJson { get; set; }
}

public sealed class OpsOperationAttemptRow
{
    public required string AttemptId { get; set; }
    public required string OperationTaskId { get; set; }
    public required string ConnectorHostId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public string? FailureJson { get; set; }
}

public sealed class OpsAuditRecordRow
{
    public required string AuditRecordId { get; set; }
    public required string OperationTaskId { get; set; }
    public required string Action { get; set; }
    public required string Actor { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public required string CorrelationId { get; set; }
}

public sealed class OpsIdempotencyRow
{
    public required string IdempotencyScope { get; set; }
    public required string OperationTaskId { get; set; }
}
```

- [ ] **Step 4: Create Ops repositories and command/query handlers**

Legacy mapping reference below shows the old store-shaped algorithm. Implement the final version through repositories and handlers:

1. `CreateOperationTaskCommandHandler` handles idempotent task creation and audit record creation.
2. `DispatchPendingOperationsCommandHandler` leases pending work to a connector host and creates attempt facts.
3. `RecordOperationResultCommandHandler` updates task/attempt status and audit facts.
4. `GetOperationTaskQueryHandler` returns the existing contract response type.

Do not inject this store into endpoints in the final implementation:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Domain;

namespace Nerv.IIP.Ops.Infrastructure;

public sealed class PostgresOpsStateStore(OpsDbContext db) : IOpsStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OperationTaskResponse Create(CreateOperationTaskRequest request, DateTimeOffset now)
    {
        var idempotencyScope = GetIdempotencyScope(request.OrganizationId, request.EnvironmentId, request.IdempotencyKey);
        var existing = db.Idempotency.AsNoTracking().SingleOrDefault(x => x.IdempotencyScope == idempotencyScope);
        if (existing is not null)
        {
            return Get(existing.OperationTaskId);
        }

        if (!string.Equals(request.OperationCode, "lifecycle.restart", StringComparison.Ordinal))
        {
            throw new InvalidOperationTaskRequestException($"Unsupported operation code: {request.OperationCode}");
        }

        var taskId = $"op-{db.Tasks.Count() + 1:000000}";
        db.Tasks.Add(new OpsOperationTaskRow
        {
            OperationTaskId = taskId,
            OrganizationId = request.OrganizationId,
            EnvironmentId = request.EnvironmentId,
            InstanceKey = request.InstanceKey,
            OperationCode = request.OperationCode,
            Status = "queued",
            RequestedBy = request.RequestedBy,
            RequestedAtUtc = now,
            IdempotencyKey = request.IdempotencyKey,
            CorrelationId = request.CorrelationId,
            ParametersJson = JsonSerializer.Serialize(request.Parameters, JsonOptions)
        });
        db.Idempotency.Add(new OpsIdempotencyRow { IdempotencyScope = idempotencyScope, OperationTaskId = taskId });
        AddAudit(taskId, "operation.requested", request.RequestedBy, now, request.CorrelationId);
        db.SaveChanges();
        return Get(taskId);
    }

    public OperationTaskResponse Get(string operationTaskId)
    {
        var task = db.Tasks.AsNoTracking().SingleOrDefault(x => x.OperationTaskId == operationTaskId)
            ?? throw new OperationTaskNotFoundException(operationTaskId);
        var attempts = db.Attempts.AsNoTracking().Where(x => x.OperationTaskId == operationTaskId).OrderBy(x => x.StartedAtUtc).Select(ToFact);
        var auditRecords = db.AuditRecords.AsNoTracking().Where(x => x.OperationTaskId == operationTaskId).OrderBy(x => x.OccurredAtUtc).Select(ToFact);
        return OperationTaskMapper.ToResponse(ToFact(task), attempts, auditRecords);
    }

    public PendingOperationTasksResponse DispatchPending(string organizationId, string environmentId, string connectorHostId, int take, DateTimeOffset now)
    {
        var cappedTake = Math.Clamp(take, 1, 50);
        var pendingTasks = db.Tasks
            .Where(x => x.OrganizationId == organizationId && x.EnvironmentId == environmentId && x.Status == "queued")
            .OrderBy(x => x.RequestedAtUtc)
            .ThenBy(x => x.OperationTaskId)
            .Take(cappedTake)
            .ToList();

        var items = new List<OperationTaskDispatchItem>();
        foreach (var task in pendingTasks)
        {
            var attemptId = $"attempt-{db.Attempts.Count() + 1:000000}";
            db.Attempts.Add(new OpsOperationAttemptRow { AttemptId = attemptId, OperationTaskId = task.OperationTaskId, ConnectorHostId = connectorHostId, Status = "started", StartedAtUtc = now });
            task.Status = "dispatched";
            AddAudit(task.OperationTaskId, "operation.dispatched", connectorHostId, now, task.CorrelationId);
            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(task.ParametersJson, JsonOptions) ?? [];
            items.Add(new OperationTaskDispatchItem(task.OperationTaskId, attemptId, task.OrganizationId, task.EnvironmentId, connectorHostId, task.InstanceKey, task.OperationCode, task.CorrelationId, parameters));
        }

        db.SaveChanges();
        return new PendingOperationTasksResponse(items);
    }

    public OperationTaskResponse RecordResult(OperationResult result)
    {
        var task = db.Tasks.SingleOrDefault(x => x.OperationTaskId == result.OperationTaskId)
            ?? throw new OperationTaskNotFoundException(result.OperationTaskId);
        var attempt = db.Attempts.SingleOrDefault(x => x.OperationTaskId == result.OperationTaskId && x.AttemptId == result.AttemptId)
            ?? throw new InvalidOperationResultException("Operation result does not match an existing attempt.");
        if (attempt.Status != "started")
        {
            throw new InvalidOperationResultException("Operation result has already been recorded for this attempt.");
        }
        if (attempt.ConnectorHostId != result.Context.ConnectorHostId || task.OrganizationId != result.Context.OrganizationId || task.EnvironmentId != result.Context.EnvironmentId || task.InstanceKey != result.InstanceKey || task.OperationCode != result.OperationCode)
        {
            throw new InvalidOperationResultException("Operation result context does not match the operation task attempt.");
        }

        var completed = string.Equals(result.ExecutionStatus, "succeeded", StringComparison.OrdinalIgnoreCase);
        task.Status = completed ? "completed" : "failed";
        attempt.Status = task.Status;
        attempt.FinishedAtUtc = result.FinishedAtUtc;
        attempt.FailureJson = result.Failure is null ? null : JsonSerializer.Serialize(result.Failure, JsonOptions);
        AddAudit(task.OperationTaskId, completed ? "operation.completed" : "operation.failed", result.Context.ConnectorHostId, result.FinishedAtUtc, result.Context.CorrelationId);
        db.SaveChanges();
        return Get(task.OperationTaskId);
    }

    private void AddAudit(string operationTaskId, string action, string actor, DateTimeOffset occurredAtUtc, string correlationId) =>
        db.AuditRecords.Add(new OpsAuditRecordRow { AuditRecordId = $"audit-{db.AuditRecords.Count() + 1:000000}", OperationTaskId = operationTaskId, Action = action, Actor = actor, OccurredAtUtc = occurredAtUtc, CorrelationId = correlationId });

    private static string GetIdempotencyScope(string organizationId, string environmentId, string idempotencyKey) => $"{organizationId}\u001f{environmentId}\u001f{idempotencyKey}";
    private static OperationTaskFact ToFact(OpsOperationTaskRow row) => new(row.OperationTaskId, row.OrganizationId, row.EnvironmentId, row.InstanceKey, row.OperationCode, row.Status, row.RequestedBy, row.RequestedAtUtc, row.IdempotencyKey, row.CorrelationId, JsonSerializer.Deserialize<Dictionary<string, string>>(row.ParametersJson, JsonOptions) ?? []);
    private static OperationAttemptFact ToFact(OpsOperationAttemptRow row) => new(row.AttemptId, row.OperationTaskId, row.ConnectorHostId, row.Status, row.StartedAtUtc, row.FinishedAtUtc, row.FailureJson is null ? null : JsonSerializer.Deserialize<FailureReason>(row.FailureJson, JsonOptions));
    private static AuditRecordFact ToFact(OpsAuditRecordRow row) => new(row.AuditRecordId, row.OperationTaskId, row.Action, row.Actor, row.OccurredAtUtc, row.CorrelationId);
}
```

- [ ] **Step 5: Add Ops netcorepal persistence DI and wire Web**

Final DI must include `AddRepositories(typeof(ApplicationDbContext).Assembly)`, `AddUnitOfWork<ApplicationDbContext>()`, `AddContext().AddEnvContext().AddCapContextProcessor()`, `AddIntegrationEvents(typeof(Program)).UseCap<ApplicationDbContext>(...)`, and `AddCap(...UseNetCorePalStorage<ApplicationDbContext>()...)` when PostgreSQL mode is enabled. The legacy extension below is a provider switch sketch; update type names from `OpsDbContext` to `ApplicationDbContext` and register repositories/handlers instead of a concrete `PostgresOpsStateStore`.

Create `OpsPersistenceServiceCollectionExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nerv.IIP.Ops.Domain;

namespace Nerv.IIP.Ops.Infrastructure;

public static class OpsPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddOpsPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Persistence:Provider"] ?? "InMemory";
        if (string.Equals(provider, "PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            services.AddDbContext<OpsDbContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("OpsDb")));
            services.AddScoped<IOpsStateStore, PostgresOpsStateStore>();
            return services;
        }

        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IOpsStateStore, InMemoryOpsStateStore>();
            return services;
        }

        throw new NotSupportedException($"Persistence provider '{provider}' is not supported by Ops yet.");
    }
}
```

Modify `Nerv.IIP.Ops.Web.csproj` to reference Infrastructure:

```xml
<ProjectReference Include="..\Nerv.IIP.Ops.Infrastructure\Nerv.IIP.Ops.Infrastructure.csproj" />
```

Modify `Program.cs`:

```csharp
using Nerv.IIP.Ops.Infrastructure;
```

Replace the store registration:

```csharp
builder.Services.AddOpsPersistence(builder.Configuration);
```

After `var app = builder.Build();`, add:

```csharp
if (string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<OpsDbContext>().Database.EnsureCreated();
}
```

- [ ] **Step 6: Add Ops PostgreSQL integration test**

Create `OpsPostgresProfileTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Nerv.IIP.Contracts.ConnectorProtocol;
using Nerv.IIP.Contracts.Ops;
using Nerv.IIP.Ops.Infrastructure;

namespace Nerv.IIP.Ops.Web.Tests;

public sealed class OpsPostgresProfileTests
{
    [Fact]
    public void Postgres_store_persists_task_attempt_and_audit_records()
    {
        var connectionString = Environment.GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<OpsDbContext>().UseNpgsql(connectionString).Options;
        using (var db = new OpsDbContext(options))
        {
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
            var store = new PostgresOpsStateStore(db);
            var task = store.Create(OpsPostgresSamples.CreateTask("pg-ops-001"), DateTimeOffset.Parse("2026-05-17T00:00:00Z"));
            var pending = store.DispatchPending("org-001", "env-dev", "connector-host-001", 10, DateTimeOffset.Parse("2026-05-17T00:00:01Z"));
            store.RecordResult(OpsPostgresSamples.Succeeded(task.OperationTaskId, pending.Items.Single().AttemptId));
        }

        using (var db = new OpsDbContext(options))
        {
            var store = new PostgresOpsStateStore(db);
            var task = store.Get("op-000001");
            Assert.Equal("completed", task.Status);
            Assert.Contains(task.AuditRecords, x => x.Action == "operation.requested");
            Assert.Contains(task.AuditRecords, x => x.Action == "operation.completed");
        }
    }

    private static class OpsPostgresSamples
    {
        private static readonly ConnectorRequestContext Context = new("1.0", "1.0", "corr-pg-ops", DateTimeOffset.Parse("2026-05-17T00:00:00Z"), "org-001", "env-dev", "connector-host-001");

        public static CreateOperationTaskRequest CreateTask(string idempotencyKey) => new(
            "org-001",
            "env-dev",
            "demo-api-001",
            "lifecycle.restart",
            idempotencyKey,
            "user-admin",
            "verify postgres ops",
            "corr-pg-ops",
            new Dictionary<string, string>());

        public static OperationResult Succeeded(string operationTaskId, string attemptId) => new(
            Context,
            operationTaskId,
            attemptId,
            "demo-api-001",
            "lifecycle.restart",
            DateTimeOffset.Parse("2026-05-17T00:00:01Z"),
            DateTimeOffset.Parse("2026-05-17T00:00:02Z"),
            "succeeded",
            null,
            new Dictionary<string, string> { ["exitCode"] = "0" });
    }
}
```

- [ ] **Step 7: Run Ops tests**

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj
$env:NERV_IIP_TEST_POSTGRES="Host=localhost;Port=5432;Database=nerv_iip_ops_test;Username=nerv;Password=nerv"
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter Postgres_store_persists_task_attempt_and_audit_records
```

Expected: both commands exit `0`.

- [ ] **Step 8: Commit**

```powershell
git add backend/services/Ops
git commit -m "feat: persist ops task facts in postgres"
```

## Task 5: Add NetCorePal Code Analysis Endpoints

**Files:**

- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- Modify: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests`
- Modify: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests`

- [ ] **Step 1: Add AppHub code-analysis endpoint**

Add the endpoint after normal route registration in AppHub `Program.cs`:

```csharp
app.MapGet("/code-analysis", () =>
{
    var assemblies = new[]
    {
        typeof(Program).Assembly,
        typeof(Nerv.IIP.AppHub.Infrastructure.ApplicationDbContext).Assembly,
        typeof(Nerv.IIP.AppHub.Domain.AggregatesModel.ApplicationAggregate.Application).Assembly
    };

    var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(
        CodeFlowAnalysisHelper.GetResultFromAssemblies(assemblies));
    return Results.Content(html, "text/html; charset=utf-8");
});
```

Add `using NetCorePal.Extensions.CodeAnalysis;` and adjust aggregate type names to the exact files created in Task 3.

- [ ] **Step 2: Add Ops code-analysis endpoint**

Add the endpoint after normal route registration in Ops `Program.cs`:

```csharp
app.MapGet("/code-analysis", () =>
{
    var assemblies = new[]
    {
        typeof(Program).Assembly,
        typeof(Nerv.IIP.Ops.Infrastructure.ApplicationDbContext).Assembly,
        typeof(Nerv.IIP.Ops.Domain.AggregatesModel.OperationTaskAggregate.OperationTask).Assembly
    };

    var html = VisualizationHtmlBuilder.GenerateVisualizationHtml(
        CodeFlowAnalysisHelper.GetResultFromAssemblies(assemblies));
    return Results.Content(html, "text/html; charset=utf-8");
});
```

Add `using NetCorePal.Extensions.CodeAnalysis;` and adjust aggregate type names to the exact files created in Task 4.

- [ ] **Step 3: Add smoke tests**

Add one web test per service that starts the web application and checks `/code-analysis` returns `text/html` and a non-empty body containing at least one command or aggregate type name.

- [ ] **Step 4: Run code-analysis tests**

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter CodeAnalysis
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter CodeAnalysis
```

Expected: both commands exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests
git commit -m "feat: expose netcorepal code analysis"
```

## Task 6: Add Real Infrastructure Verification Script

**Files:**

- Modify: `scripts/verify-second-slice-ops.ps1`
- Modify: `scripts/verify-third-slice-console.ps1`
- Create: `scripts/verify-fourth-slice-real-infra.ps1`
- Modify: `.codex/environments/environment.toml`

- [ ] **Step 1: Add PostgreSQL switch to second-slice script**

At the top of `scripts/verify-second-slice-ops.ps1`, after strict mode, add:

```powershell
param(
  [switch]$UsePostgres
)
```

Before starting jobs, define:

```powershell
$appHubDb = "Host=localhost;Port=5432;Database=nerv_iip;Username=nerv;Password=nerv"
$opsDb = "Host=localhost;Port=5432;Database=nerv_iip;Username=nerv;Password=nerv"
```

Inside the AppHub Start-Job script block, accept `$usePostgres` and `$connectionString`; when enabled set:

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__AppHubDb = $connectionString
```

Inside the Ops Start-Job script block, accept `$usePostgres` and `$connectionString`; when enabled set:

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__OpsDb = $connectionString
```

Keep the default in-memory path unchanged when `$UsePostgres` is not supplied.

- [ ] **Step 2: Add PostgreSQL switch to third-slice script**

At the top of `scripts/verify-third-slice-console.ps1`, add:

```powershell
param(
  [switch]$UsePostgres
)
```

Replace the second-slice invocation:

```powershell
if ($UsePostgres) {
  pwsh scripts/verify-second-slice-ops.ps1 -UsePostgres
}
else {
  pwsh scripts/verify-second-slice-ops.ps1
}
```

- [ ] **Step 3: Create fourth-slice verification script**

Create `scripts/verify-fourth-slice-real-infra.ps1`:

```powershell
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
  $PSNativeCommandUseErrorActionPreference = $true
}

function Wait-TcpPort {
  param(
    [string]$HostName,
    [int]$Port
  )

  $deadline = (Get-Date).AddSeconds(60)
  do {
    try {
      $client = [System.Net.Sockets.TcpClient]::new()
      $connect = $client.BeginConnect($HostName, $Port, $null, $null)
      if ($connect.AsyncWaitHandle.WaitOne(1000)) {
        $client.EndConnect($connect)
        $client.Dispose()
        return
      }
      $client.Dispose()
    }
    catch {
      Start-Sleep -Milliseconds 500
    }
  } while ((Get-Date) -lt $deadline)

  throw "TCP port $HostName:$Port did not open within 60 seconds."
}

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root

docker compose -f infra/docker-compose.dev.yml up -d postgres redis rabbitmq
Wait-TcpPort localhost 5432
Wait-TcpPort localhost 6379
Wait-TcpPort localhost 5672

$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=5432;Database=nerv_iip_apphub_test;Username=nerv;Password=nerv"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter Postgres_store_persists_registration_heartbeat_and_state

$env:NERV_IIP_TEST_POSTGRES = "Host=localhost;Port=5432;Database=nerv_iip_ops_test;Username=nerv;Password=nerv"
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter Postgres_store_persists_task_attempt_and_audit_records

Remove-Item Env:\NERV_IIP_TEST_POSTGRES -ErrorAction SilentlyContinue
pwsh scripts/verify-third-slice-console.ps1 -UsePostgres

Write-Host "Fourth vertical slice real infrastructure verified."
```

- [ ] **Step 4: Add Codex environment action**

Append this action to `.codex/environments/environment.toml`:

```toml
[[actions]]
name = "验证第四阶段真实基础设施"
icon = "tool"
command = "pwsh scripts/verify-fourth-slice-real-infra.ps1"
```

- [ ] **Step 5: Run the fourth-slice verification**

```powershell
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

Expected final line:

```text
Fourth vertical slice real infrastructure verified.
```

- [ ] **Step 6: Commit**

```powershell
git add scripts/verify-second-slice-ops.ps1 scripts/verify-third-slice-console.ps1 scripts/verify-fourth-slice-real-infra.ps1 .codex/environments/environment.toml
git commit -m "test: verify vertical slice on real infrastructure"
```

## Task 7: Add Platform-Level Aspire AppHost

**Files:**

- Create: `infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`
- Create: `infra/aspire/Nerv.IIP.AppHost/Program.cs`

- [ ] **Step 1: Create AppHost project file**

Create `Nerv.IIP.AppHost.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.AppHost" Version="13.3.3" />
    <PackageReference Include="Aspire.Hosting.PostgreSQL" Version="13.3.3" />
    <PackageReference Include="Aspire.Hosting.Redis" Version="13.3.3" />
    <PackageReference Include="Aspire.Hosting.RabbitMQ" Version="13.3.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\backend\services\AppHub\src\Nerv.IIP.AppHub.Web\Nerv.IIP.AppHub.Web.csproj" />
    <ProjectReference Include="..\..\..\backend\services\Ops\src\Nerv.IIP.Ops.Web\Nerv.IIP.Ops.Web.csproj" />
    <ProjectReference Include="..\..\..\backend\gateway\PlatformGateway\src\Nerv.IIP.PlatformGateway.Web\Nerv.IIP.PlatformGateway.Web.csproj" />
    <ProjectReference Include="..\..\..\connector-hosts\src\Nerv.IIP.ConnectorHost.Host\Nerv.IIP.ConnectorHost.Host.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create AppHost Program**

Create `Program.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("nerv-iip-postgres");
var appHubDatabase = postgres.AddDatabase("apphub-db", "nerv_iip_apphub");
var opsDatabase = postgres.AddDatabase("ops-db", "nerv_iip_ops");

var redis = builder.AddRedis("redis")
    .WithDataVolume("nerv-iip-redis");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin();

var appHub = builder.AddProject<Projects.Nerv_IIP_AppHub_Web>("apphub")
    .WithReference(appHubDatabase, "AppHubDb")
    .WaitFor(appHubDatabase)
    .WithEnvironment("Persistence__Provider", "PostgreSQL");

var ops = builder.AddProject<Projects.Nerv_IIP_Ops_Web>("ops")
    .WithReference(opsDatabase, "OpsDb")
    .WaitFor(opsDatabase)
    .WithEnvironment("Persistence__Provider", "PostgreSQL");

var gateway = builder.AddProject<Projects.Nerv_IIP_PlatformGateway_Web>("gateway")
    .WithReference(appHub)
    .WithReference(ops)
    .WaitFor(appHub)
    .WaitFor(ops);

builder.AddProject<Projects.Nerv_IIP_ConnectorHost_Host>("connector-host")
    .WithReference(appHub)
    .WithReference(ops)
    .WaitFor(appHub)
    .WaitFor(ops)
    .WithEnvironment("ConnectorHost__CycleSeconds", "1");

_ = redis;
_ = rabbitmq;
_ = gateway;

builder.Build().Run();
```

If the generated project type names differ, build once and adjust only the `Projects.*` identifiers to the generated names; keep project references and resource names unchanged.

- [ ] **Step 3: Build AppHost**

```powershell
dotnet restore infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: both commands exit `0`.

- [ ] **Step 4: Smoke run AppHost**

```powershell
dotnet run --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

Expected: AppHost starts and lists `postgres`, `redis`, `rabbitmq`, `apphub`, `ops`, `gateway`, and `connector-host` resources in the Aspire dashboard output. Stop it with Ctrl+C after confirming startup.

- [ ] **Step 5: Commit**

```powershell
git add infra/aspire/Nerv.IIP.AppHost
git commit -m "feat: add platform aspire apphost"
```

## Task 8: Update Documentation For Fourth Stage

**Files:**

- Modify: `README.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/deployment-baseline.md`
- Modify: `docs/architecture/api-contract-and-codegen.md`
- Modify: `docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md`

- [x] **Step 1: Update README status and plan index**

Add this plan to the "实施计划" list:

```markdown
4. docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md
```

After the third-stage paragraph in "当前状态", add:

```markdown
第四阶段真实基础设施底座纵切可以用 `scripts/verify-fourth-slice-real-infra.ps1` 验证：脚本会拉起 PostgreSQL、Redis 和 RabbitMQ，本地验证 AppHub/Ops 的 netcorepal/CleanDDD PostgreSQL profile、code-analysis endpoint，并在 PostgreSQL 模式下复跑第三阶段控制台纵切。
```

- [x] **Step 2: Update implementation readiness**

Add a "第四迭代计划范围" section under the third iteration section:

```markdown
### 第四迭代计划范围

1. AppHub 和 Ops 作为 netcorepal/CleanDDD 迁移试点，落 Domain aggregate、Application command/query、Infrastructure repository/ApplicationDbContext 和 mediator-driven endpoint。
2. PostgreSQL 使用服务级 schema：AppHub 使用 `apphub`，Ops 使用 `ops`；provider 选择只留在 Infrastructure/profile/test/deployment 层。
3. AppHub/Ops 暴露 `/code-analysis`，用于查看 netcorepal 识别的命令、查询、聚合、事件和处理器流向。
4. `scripts/verify-fourth-slice-real-infra.ps1` 作为第四阶段验收入口，默认通过 `infra/docker-compose.dev.yml` 拉起依赖。
5. 平台级 AppHost 落到 `infra/aspire/Nerv.IIP.AppHost`，覆盖 AppHub、Ops、Gateway、Connector Host、PostgreSQL、Redis 和 RabbitMQ。
6. PlatformGateway、Connector Host、Contracts/SDK 和 frontend console 不强行套完整 netcorepal 三项目模型；IAM 完整授权、FileStorage 上传下载、CAP outbox、通知和审批不进入本阶段实现范围。
```

- [x] **Step 3: Update deployment baseline**

In "当前阶段", replace the statement that AppHost has not landed with:

```markdown
第四阶段已落地平台级 AppHost 到 `infra/aspire/Nerv.IIP.AppHost`，用于表达 AppHub、Ops、Gateway、Connector Host 与 PostgreSQL、Redis、RabbitMQ 的首批真实基础设施拓扑。`infra/docker-compose.dev.yml` 继续作为验证脚本拉起本地依赖的稳定入口。
```

In the logging section, keep these deployment decisions explicit:

1. Fourth stage defaults to `collector-only`; it must not require Grafana, Loki, Elasticsearch, Seq or ClickHouse.
2. Microsoft-official, self-hostable, open-source/free and active community are selection preferences, not all-or-nothing gates.
3. `aspire-dashboard` is the selected short-term observability UI profile for Aspire and Docker-compatible environments.
4. Aspire Dashboard must be documented as short-term and in-memory; it is not a production log persistence backend.
5. Docker Compose must support both `collector-only` and optional `aspire-dashboard` profile/overlay.
6. Package/script installs must not require containers; they must at least configure rolling JSONL files and may configure OTLP endpoint or standalone Aspire Dashboard when available.
7. Built-in log persistence uses rolling JSONL hot files, Log Archive Worker, File Storage `.jsonl.gz` chunks and independent `observability` metadata index.
8. Product console log viewing goes through PlatformGateway; frontend must not directly query Aspire Dashboard, archive storage or any observability backend.
9. Gateway must enforce IAM, organization/environment scope, time window limits, paging, rate limits and redaction before returning log entries.
10. The default index database is PostgreSQL `observability` schema or database; SQLite is diagnostic-only and external search engines are adapters.

- [x] **Step 4: Update API contract log query rules**

Update `docs/architecture/api-contract-and-codegen.md` with a `Console Log Query API` section. It must define these future Gateway operations without implementing them in fourth-stage code:

1. `queryConsoleLogs` for `/api/console/v1/logs/query`.
2. `getConsoleInstanceLogs` for `/api/console/v1/instances/{instanceKey}/logs`.
3. `getConsoleOperationLogs` for `/api/console/v1/operation-tasks/{operationTaskId}/logs`.
4. A backend-neutral response DTO with `timestamp`、`level`、`service`、`message`、`instanceKey`、`operationTaskId`、`correlationId`、`traceId`、`labels`、`fields`、`source`、`nextCursor` and `partial`.

- [x] **Step 5: Add completion record after verification**

After `pwsh scripts/verify-fourth-slice-real-infra.ps1` passes, add a `Completion Record` section near the top of this plan with the exact command and final output line.

- [ ] **Step 6: Commit**

```powershell
git add README.md docs/architecture/implementation-readiness.md docs/architecture/deployment-baseline.md docs/architecture/api-contract-and-codegen.md docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md
git commit -m "docs: document fourth real infrastructure slice"
```

## Execution Order

1. Task 1 must run first because AppHub and Ops Domain/Web/Infrastructure need the netcorepal, EF, CAP and PostgreSQL profile package baseline.
2. Task 2 must run before Task 3 because it freezes the AppHub behavior currently hidden in the in-memory store.
3. Task 3 and Task 4 can run in parallel after Task 1; they touch different service folders and both follow the same netcorepal/CleanDDD target shape.
4. Task 5 depends on Tasks 3 and 4 because code-analysis must include the migrated command/query/aggregate/repository flow.
5. Task 6 depends on Tasks 3, 4 and 5 because the real-infra script must verify PostgreSQL mode and code-analysis smoke tests.
6. Task 7 can run after Tasks 3 and 4 because AppHost should start services in PostgreSQL mode.
7. Task 8 is last because it records verified behavior.

Recommended parallelization after Task 2:

1. One worker implements Task 3 AppHub netcorepal/CleanDDD migration and PostgreSQL profile.
2. One worker implements Task 4 Ops netcorepal/CleanDDD migration and PostgreSQL profile.
3. One worker prepares Task 7 AppHost project after service DI shapes are known.

## Fourth Iteration Completion Definition

The fourth iteration is complete when all statements are true:

1. AppHub Web endpoints call MediatR commands/queries instead of concrete stores or DbContext.
2. Ops Web endpoints call MediatR commands/queries instead of concrete stores or DbContext.
3. AppHub and Ops Domain projects contain netcorepal aggregate roots, strong typed IDs and domain events, with provider-specific code absent.
4. AppHub and Ops Infrastructure projects contain `ApplicationDbContext : AppDbContextBase`, entity configurations and repositories based on netcorepal repository patterns.
5. In-memory AppHub and Ops behavior tests still pass as regression baselines.
6. AppHub PostgreSQL integration test proves registration, heartbeat and state facts survive a new DbContext.
7. Ops PostgreSQL integration test proves task, attempt and audit facts survive a new DbContext.
8. AppHub and Ops expose `/code-analysis` endpoints returning non-empty netcorepal code-flow HTML.
9. Backend services use `ILogger<T>` in application code, Serilog in Host/Observability registration, and OpenTelemetry/OTLP for log export.
10. Local logging fallback is implemented as bounded rolling JSONL files; optional .NET Aspire Dashboard profile is documented for short-term local telemetry viewing.
11. No runtime log table is added to AppHub/Ops/IAM/FileStorage PostgreSQL schemas; Ops `AuditRecord` remains audit-only, not a general log store.
12. Deployment docs define the built-in log persistence target: Log Archive Worker, File Storage compressed chunks and PostgreSQL independent `observability` metadata index.
13. Deployment docs define observability resource profiles across Aspire AppHost, Docker Compose and package/script installs.
14. Deployment docs define `collector-only` default, optional `aspire-dashboard` short-term UI, optional `log-archive` persistence profile and no default Grafana/Loki/Elastic/Seq/ClickHouse dependency.
15. API contract docs define console log query as a future Gateway OpenAPI capability, with frontend forbidden from direct observability-backend access.
16. `scripts/verify-second-slice-ops.ps1` remains usable without PostgreSQL.
17. `scripts/verify-fourth-slice-real-infra.ps1` starts local PostgreSQL, Redis and RabbitMQ and exits `0`.
18. `scripts/verify-third-slice-console.ps1 -UsePostgres` exits `0`.
19. Platform AppHost builds resources for AppHub, Ops, Gateway, Connector Host, PostgreSQL, Redis and RabbitMQ; OpenTelemetry Collector remains a documented follow-up observability resource profile.
20. Provider-specific database code is isolated to Infrastructure DI extension, profile tests, scripts and AppHost/deployment configuration; Domain/Application/Endpoint/SDK code does not reference Npgsql or PostgreSQL-only SQL.
21. Gateway, Connector Host, Contracts/SDK and frontend console remain outside the full netcorepal service model for the reasons documented in this plan.
22. Documentation names the fourth-stage verification command and keeps IAM/FileStorage/approval/notification/GaussDB-DMDB production profiles as follow-up scope.

## Self Review

Spec coverage:

1. README next-stage item "PostgreSQL/RabbitMQ/Redis real infrastructure and database profile shape": covered by Tasks 1, 3, 4, 6 and 7.
2. NetCorePal adoption decision: covered by the "NetCorePal Adoption Decision" section and Tasks 1, 3, 4 and 5.
3. Logging library, fields and persistence boundary: covered by Task 1 and ADR/deployment documentation.
4. Observability backend resource profile and console log query boundary: covered by deployment baseline and API contract documentation.
5. Deployment baseline AppHost direction: covered by Task 7.
6. Current third-stage console chain: preserved by Task 6 with `-UsePostgres`.
7. IAM/FileStorage/Ops approval/notification follow-ups: explicitly outside this focused plan and recorded in docs.

Placeholder scan:

1. No unresolved placeholder markers are present.
2. File paths, commands and expected outputs are explicit.
3. Code snippets define concrete method names, rows, interfaces and service registration shapes; revised netcorepal notes supersede legacy store snippets where the two differ.

Type consistency:

1. AppHub commands/queries preserve the current `InMemoryAppHubStateStore` public behavior.
2. Ops commands/queries preserve the current `InMemoryOpsStateStore` public behavior.
3. PostgreSQL connection strings use the same local credentials as `infra/docker-compose.dev.yml`.
4. GaussDB/DMDB are not implemented in this stage; the code shape should make them future profile additions instead of business-layer rewrites.
5. AppHost resource names match the service names used by existing verification scripts.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

Which approach?
