# 第四阶段纵切真实基础设施基础实施计划

> **面向智能体执行者：** 必须使用子技能：使用 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans 逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 将第一、第二、第三阶段已经跑通的 AppHub、Ops、Gateway、Connector Host 和 Console 链路迁移到可验证的 netcorepal-first 真实基础设施底座，优先完成 PostgreSQL profile、结构化日志与 OpenTelemetry 输出、平台级 Aspire AppHost、框架代码分析入口和 database profile 边界。

**架构：** 本阶段不扩展业务范围，先把 AppHub 和 Ops 当前内存态事实迁移到符合 netcorepal/CleanDDD 的 Domain 聚合、Application 命令/查询、Infrastructure 仓库/ApplicationDbContext 形态；PostgreSQL 作为首个数据库配置档（profile），数据库提供程序选择只存在于 Infrastructure DI 扩展、配置、测试和部署脚本中。验证脚本用本地 `infra/docker-compose.dev.yml` 拉起依赖并证明数据跨 DbContext 生命周期存在。Aspire AppHost 作为统一拓扑入口落到 `infra/aspire/Nerv.IIP.AppHost`，但不替代既有验证脚本；日志采用 Console/OTLP 优先，本地滚动 JSONL 文件是第四阶段必须实现的持久化兜底；内置长期持久化目标是 Log Archive Worker 将关闭后的日志压缩成 File Storage 分块，并在 PostgreSQL 独立 `observability` schema 或数据库中记录可查询元数据索引；可选 .NET Aspire Dashboard 作为短期观测 UI；观测配置档必须同时覆盖 Aspire AppHost、Docker Compose 和安装包/脚本三类部署入口；控制台日志查看通过 PlatformGateway 后续受控 API 接入，不让前端直连 Aspire Dashboard 或第三方观测后端；Gateway、Connector Host、Contracts/SDK、前端 API 客户端和控制台保持轻量契约边界，不强行采用完整 netcorepal 三项目模型。

**技术栈：** .NET 10、netcorepal-cloud-framework/NetCorePal 3.3.0、FastEndpoints、MediatR、Entity Framework Core 10.0.8、Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1、NetCorePal CAP PostgreSQL storage 3.3.0、Serilog.AspNetCore、Serilog.Sinks.OpenTelemetry、Serilog.Sinks.File、OpenTelemetry Collector、可选的短期可观测性 UI .NET Aspire Dashboard、PostgreSQL 17 主配置档、Redis 7、RabbitMQ 4、PowerShell、Docker Compose、Aspire.Hosting 13.3.3、pnpm 10.13.1、Vue 3、Hey API。GaussDB/DMDB 记录为模板支持的未来配置档，本阶段不实施。

---

## 完成记录

2026-05-17 本阶段真实基础设施门禁已通过：

```powershell
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

最终输出：

```text
Fourth vertical slice real infrastructure verified.
```

验证环境使用 Docker Desktop；镜像拉取受限时已通过 Docker Desktop proxy 指向 `http://127.0.0.1:10808` 解决。PostgreSQL 本机端口改为 `15432`，避免与本机已有 `5432` PostgreSQL 冲突。AppHub 与 Ops 在 AppHost 和验证脚本中使用独立 database（默认 `nerv_iip_apphub` / `nerv_iip_ops`，第四阶段脚本使用 `nerv_iip_apphub_verify` / `nerv_iip_ops_verify`），避免共享 database 下 `EnsureCreated()` 因既有 schema/table 漏建服务表。

## 执行状态

2026-05-17 当前执行进度：

1. 任务 1-5 已完成并通过本地还原/构建/测试、AppHub/Ops 内存模式回归、PostgreSQL profile 冒烟测试（未设置真实 PostgreSQL 时早返回）、code-analysis 冒烟测试和审核者复核。
2. 任务 6 脚本已落地并通过：`scripts/verify-second-slice-ops.ps1` 与 `scripts/verify-third-slice-console.ps1` 支持 `-UsePostgres`，`scripts/verify-fourth-slice-real-infra.ps1` 会拉起 PostgreSQL、Redis、RabbitMQ、重建验证数据库并运行真实基础设施门禁，`.codex/environments/environment.toml` 已增加第四阶段操作。
3. 任务 7 平台级 AppHost 已落地到 `infra/aspire/Nerv.IIP.AppHost`，AppHub/Ops 使用独立 PostgreSQL 数据库资源，并已通过 `dotnet restore infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj` 与 `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`。
4. `/code-analysis` 已从 `Program.cs` 的 Minimal API 写法收敛到 FastEndpoints endpoint，`dotnet test backend/tests/Nerv.IIP.FastEndpoints.Architecture.Tests/Nerv.IIP.FastEndpoints.Architecture.Tests.csproj --no-build`、AppHub/Ops CodeAnalysis 冒烟测试和 `pwsh scripts/verify-second-slice-ops.ps1` 均已通过。
5. `pwsh scripts/verify-fourth-slice-real-infra.ps1` 已完整通过：AppHub/Ops PostgreSQL profile 测试、后端/Connector Host 解决方案串行测试、Gateway OpenAPI 导出、前端 API 客户端生成、类型检查/测试/构建和真实 AppHub/Ops/Gateway/Connector Host 联调均通过。

## 当前门禁

当前第四阶段真实基础设施门禁已经通过：

```powershell
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

当前限制：

1. 第四阶段仍使用 `EnsureCreated()` 做本地纵切验证；生产级迁移发布、回滚和种子数据初始化流程仍在后续持久化硬化阶段。
2. IAM 完整授权、FileStorage 真实上传下载、CAP outbox、审批、通知和高风险动作不进入本阶段。
3. GaussDB、DMDB、Kingbase、OceanBase 等信创数据库 profile 只冻结边界与替换约束，尚未实现生产 adapter。
4. AppHost 当前覆盖 AppHub、Ops、Gateway、Connector Host、PostgreSQL、Redis 和 RabbitMQ；OpenTelemetry Collector、Log Archive Worker、长期日志索引和控制台日志查询仍按部署基线进入后续阶段。

## 范围

### 本计划范围内

1. 引入 NetCorePal、EF Core、Npgsql、CAP RabbitMQ/CAP PostgreSQL storage、Serilog/OpenTelemetry sink 包版本，并保持 Central Package Management；provider 包只作为 PostgreSQL profile 的实现依赖。
2. 将 AppHub 当前事实迁移为 netcorepal/CleanDDD 形态：Domain 聚合、Infrastructure 仓库/ApplicationDbContext、Web Application 命令/查询，Endpoint 通过 mediator 调用。
3. 将 Ops 当前事实迁移为 netcorepal/CleanDDD 形态：Domain 聚合、Infrastructure 仓库/ApplicationDbContext、Web Application 命令/查询，Endpoint 通过 mediator 调用。
4. 保留现有内存验证链路作为回归门禁，但新 PostgreSQL 路径必须基于 `ApplicationDbContext : AppDbContextBase`、`AddUnitOfWork<ApplicationDbContext>()`、NetCorePal 仓库和 CAP 存储。
5. 增加 PostgreSQL profile 集成测试，验证事实跨 DbContext 生命周期存在，并证明命令处理器不手写 `SaveChanges`。
6. 为 AppHub/Ops 增加 netcorepal code-analysis endpoint，用于生成命令、聚合、事件、处理器流向图。
7. 固化后端日志口径：业务代码使用 `ILogger<T>`，宿主层使用 Serilog provider，日志通过 Console 与 OpenTelemetry/OTLP 输出到 Collector；无生产观测后端时使用滚动 JSONL 文件兜底，需要本地观测 UI 时使用可选 .NET Aspire Dashboard profile；日志不写业务 PostgreSQL。
8. 增加 `scripts/verify-fourth-slice-real-infra.ps1`，拉起本地依赖并运行真实基础设施门禁。
9. 创建平台级 Aspire AppHost，覆盖 PostgreSQL、Redis、RabbitMQ、AppHub、Ops、Gateway 和 Connector Host；OpenTelemetry Collector 作为后续观测 profile 资源继续由部署基线承接。
10. 在文档和代码注册层固化 `Persistence:Provider` 边界，默认值为 `InMemory`，第四阶段脚本显式切到 `PostgreSQL`。
11. 固化日志查看的后续契约边界：控制台只消费 PlatformGateway `/api/console/**`，Gateway 代理日志后端并负责鉴权、租户隔离、限流、脱敏、分页和时间窗口限制。
12. 固化内置日志持久化目标：滚动 JSONL 热日志、Log Archive Worker、File Storage `.jsonl.gz` 分块、PostgreSQL 独立 `observability` 索引元数据和 Gateway 查询代理；第四阶段只要求落地滚动 JSONL，归档工作程序和查询页作为后续实现。
13. 固化观测部署入口：Aspire AppHost 使用 AppHost/Dashboard，Docker Compose 通过 `collector-only`、`aspire-dashboard` 与 `log-archive` 配置档/叠加层，安装包和脚本通过 OTLP 端点、滚动日志目录、File Storage 归档目标和可选独立 Dashboard 配置实现。
14. 更新 README、实施状态、部署基线、API 契约规范和 `.codex/environments/environment.toml` 的第四阶段入口。

### 本计划范围外

1. 不把 IAM 登录、JWT、refresh token 和权限 guard 全量迁移到 PostgreSQL。
2. 不完成 FileStorage 的真实上传下载闭环和 MinIO provider。
3. 不实现 CAP outbox、RabbitMQ 消费者、通知服务或审批 UI。
4. 不引入生产级 EF migrations 发布流程；第四阶段使用 `EnsureCreated()` 做本地纵切验证，正式迁移流程在后续持久化硬化阶段补齐。
5. 不生成生产 Compose、安装包、Windows Service 或 systemd unit。
6. 不实现 GaussDB、DMDB、Kingbase、OceanBase 等信创数据库的生产 adapter；本阶段只保证 PostgreSQL 实现不把 provider 细节泄漏到业务层，并把后续 profile 适配点留清楚。
7. 不把 PlatformGateway、Connector Host、Contracts/SDK、frontend console 强行改造成完整 netcorepal 三项目服务；这些项目只按职责消费 Web/API、观测、契约或 SDK 约定。
8. 不整包重跑 `dotnet new netcorepal-web` 覆盖已有 AppHub/Ops 代码；本阶段按模板形态迁移现有纵切，避免丢失已经验证过的契约和脚本。
9. 不在第四阶段实现产品控制台日志查询页、日志 tail、生产级 Grafana/Loki/Elastic/Seq/ClickHouse 部署、Log Archive Worker 或长期日志保留任务；本阶段只冻结 Gateway 接入边界、Aspire Dashboard 短期观测 UI、滚动 JSONL 热日志和内置归档目标设计。

## 文件结构图

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

## 边界规则

1. AppHub 和 Ops Web 端点依赖 MediatR 命令/查询，而不是具体内存存储或 DbContext。
2. Domain 项目拥有聚合根、实体、值对象、强类型 ID 和领域事件；不得引用 EF provider 包、CAP 包或基础设施存储。
3. Infrastructure 项目拥有 `ApplicationDbContext`、实体配置、仓库接口/实现和数据库配置档注册。
4. Web 项目拥有 Endpoint、Application 命令、查询、DomainEventHandler、IntegrationEvent 和框架注册。
5. Gateway 继续通过 HTTP 客户端调用 AppHub 和 Ops；不得引用 AppHub/Ops Domain 或 Infrastructure。
6. Connector Host 继续使用 Platform SDK 客户端；不得引用后端服务实现项目。
7. Database provider 选择隔离在 Infrastructure DI 扩展、profile 测试、脚本和部署配置中；Domain/Application/Endpoint/SDK 代码不得引用 provider 特定包、SQL 方言或 PostgreSQL 专有类型。
8. PostgreSQL schema 归服务所有：AppHub 使用 schema `apphub`，Ops 使用 schema `ops`；服务不得读写彼此的 schema。
9. 本阶段将 Redis 和 RabbitMQ 引入 AppHost 拓扑，并通过 netcorepal 接入 CAP 存储；除非框架冒烟测试需要，实际跨服务消息行为仍属于后续范围。
10. 业务代码仅使用 `ILogger<T>`；Serilog、Console sink、OpenTelemetry sink 和部署日志后端选择保留在 Host/Observability/部署配置中。
11. 运行时日志、Ops 审计事实和业务事务数据使用独立的存储与保留策略；日志不写入服务 PostgreSQL schema。
12. 本地日志回退有界：滚动 JSONL 文件用于最低诊断，OpenTelemetry Collector `file_storage` 队列用于短期导出韧性，可选 .NET Aspire Dashboard 仅用于短期本地遥测查看。
13. 内置持久化日志使用 Log Archive Worker、File Storage 压缩分块和独立 PostgreSQL `observability` 元数据索引；原始日志正文不进入业务 schema。
14. 可观测性 profile 必须支持 Aspire AppHost、Docker Compose 和包/脚本安装。Docker 可以将 Dashboard 和 Log Archive Worker 作为可选服务运行；直接安装不得要求容器运行时。
15. 控制台日志查看属于 Gateway 职责：前端代码必须使用生成的 `/api/console/**` 客户端，Gateway 必须通过 IAM 授权、组织/环境过滤、有界时间窗口、分页、限流和脱敏代理任何 Aspire Dashboard、滚动文件、内置归档、生产后端或客户平台查询。
16. 日志查询 DTO 必须保持后端中立；LogQL、后端 URL、凭证、租户标头、File Storage 对象键或存储特定字段均不得泄漏到前端契约。
17. 验证必须保持现有内存态脚本可用。PostgreSQL 模式通过第四阶段脚本和显式环境变量选择启用。

## 架构输入

执行任务前阅读以下文档：

1. `docs/adr/0003-data-and-messaging-baseline.md`：PostgreSQL、Redis、RabbitMQ、MinIO、outbox 和服务 schema 边界。
2. `docs/architecture/backend-cleanddd-netcorepal-guidelines.md`：database profile 和信创兼容性规则。
3. `docs/adr/0008-multi-target-deployment-and-aspire-apphost.md`：单一 AppHost 策略。
4. `docs/architecture/deployment-baseline.md`：AppHost 和 Compose 职责。
5. `docs/architecture/implementation-readiness.md`：当前阶段状态和验证命令。
6. `docs/architecture/api-contract-and-codegen.md`：Gateway OpenAPI、生成前端客户端和控制台日志查询 API 边界。
7. `docs/architecture/third-vertical-slice-console.md`：Gateway OpenAPI 和控制台 codegen 边界。

## NetCorePal 采用决策

第四阶段从现在开始把 `netcorepal-cloud-framework` 明确为后端平台领域服务的默认框架。执行时按下表处理，不再把“只有 Web 项目使用框架”作为目标形态：

| 项目类型 | 第四阶段决策 | 原因 |
| --- | --- | --- |
| AppHub | 采用完整 netcorepal/CleanDDD 形态 | 已经拥有注册、心跳、状态查询和幂等事实，适合作为第一个真实持久化和 code-analysis 试点。 |
| Ops | 采用完整 netcorepal/CleanDDD 形态 | 已经拥有任务、尝试、审计和幂等事实，和 AppHub 一起验证命令、查询、仓储、事务、测试和 database profile。 |
| Iam | 本计划保留当前纵切；稍后迁移 | IAM 认证、JWT、刷新令牌、权限防护的风险面更大，第四阶段只记录为后续迁移对象。 |
| FileStorage | 本计划保留当前纵切；稍后迁移 | 需要同时处理 MinIO/provider、对象元数据和下载授权，放到真实文件闭环阶段。 |
| PlatformGateway | 不强制完整 netcorepal 三项目结构 | Gateway 是 BFF/路由聚合层，默认只使用 ASP.NET/FastEndpoints、观测和契约消费约定；只有拥有自身持久化模型时再补 Infrastructure。 |
| Connector Host | 不采用完整 netcorepal 服务模型 | Connector Host 是可独立安装升级的 worker，通过 Platform SDK/HTTP 与平台交互，不拥有平台领域数据库。 |
| Contracts/SDK | 保持轻量 | 这些项目是跨进程契约和客户端封装，不能反向依赖服务端框架。 |
| Frontend console/api-client | 不适用 | Vue、Hey API 和 pnpm 侧只消费 OpenAPI，不引入 .NET server framework。 |

执行本计划时，如果后续任务中的旧存储代码片段与本节冲突，以本节为准：最终态应是端点 -> MediatR 命令/查询 -> 仓库/查询处理器 -> `ApplicationDbContext`，而不是端点直接注入具体存储。内存存储只允许作为回归测试基线或临时适配器存在，不能成为新功能扩展方向。

---

## 任务 1：添加 NetCorePal、持久化和分析包基线

**文件：**

- 修改：`backend/Directory.Packages.props`
- 修改：`backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj`
- 修改：`backend/common/Observability/Nerv.IIP.Observability/NervIipObservability.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/Nerv.IIP.AppHub.Domain.csproj`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Domain/Nerv.IIP.Ops.Domain.csproj`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj`

- [ ] **步骤 1：添加集中式包版本**

修改 `backend/Directory.Packages.props`，并在现有 `ItemGroup` 中添加以下条目：

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

包行遵循当前 `NetCorePal.Template` 生成的包形态以及 2026-05-17 检查的 `dotnet package search` 结果。如果执行期间还原报告了更新且兼容的模板基线，应一起更新所有 NetCorePal 包；不得混用次版本。

- [ ] **步骤 2：从 AppHub.Domain 和 Ops.Domain 引用 netcorepal Domain 包**

编辑 Domain 项目前，使用宿主级日志包更新 `backend/common/Observability/Nerv.IIP.Observability/Nerv.IIP.Observability.csproj`：

```xml
<PackageReference Include="Serilog.AspNetCore" />
<PackageReference Include="Serilog.Enrichers.ClientInfo" />
<PackageReference Include="Serilog.Sinks.File" />
<PackageReference Include="Serilog.Sinks.OpenTelemetry" />
```

扩展 `AddNervIipObservability(...)`，使每个宿主都能从配置中配置 Serilog，以服务名称和关联作用域丰富日志，将 JSON 写入 Console，在 `Logging:LocalFile:Enabled=true` 时写入有界滚动 JSONL 文件，并在配置了 `OTEL_EXPORTER_OTLP_ENDPOINT` 或 `OpenTelemetry:Endpoint` 时选择写入 OTLP。保持 `ILogger<T>` 为应用代码使用的唯一 API。

修改两个 Domain csproj 文件：

- `backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/Nerv.IIP.AppHub.Domain.csproj`
- `backend/services/Ops/src/Nerv.IIP.Ops.Domain/Nerv.IIP.Ops.Domain.csproj`

添加以下包引用：

```xml
<PackageReference Include="NetCorePal.Extensions.CodeAnalysis" />
<PackageReference Include="NetCorePal.Extensions.Domain.Abstractions" />
<PackageReference Include="NetCorePal.Extensions.Primitives" />
```

- [ ] **步骤 3：从 AppHub.Web 和 Ops.Web 引用 netcorepal Web 包**

修改两个 Web csproj 文件：

- `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj`
- `backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj`

在现有 FastEndpoints 引用旁添加以下包引用：

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

- [ ] **步骤 4：从 AppHub.Infrastructure 引用 PostgreSQL profile 包**

修改 `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj`：

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

- [ ] **步骤 5：从 Ops.Infrastructure 引用 PostgreSQL profile 包**

修改 `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj`：

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

- [ ] **步骤 6：还原并构建后端**

运行：

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln --no-restore
```

预期结果：两条命令都以 `0` 退出。

- [ ] **步骤 7：提交**

```powershell
git add backend/Directory.Packages.props backend/common/Observability/Nerv.IIP.Observability backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/Nerv.IIP.AppHub.Domain.csproj backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Nerv.IIP.AppHub.Infrastructure.csproj backend/services/Ops/src/Nerv.IIP.Ops.Domain/Nerv.IIP.Ops.Domain.csproj backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Nerv.IIP.Ops.Infrastructure.csproj
git commit -m "chore: add netcorepal persistence package baseline"
```

## 任务 2：在 CleanDDD 迁移前映射现有 AppHub 行为

> 修订后的 netcorepal 执行说明：此任务不再是最终的“端点 -> 存储”重构。使用现有 `InMemoryAppHubStateStore` API 作为注册、心跳、状态快照、实例列表和实例详情的行为映射。任务 3 完成的实现最终必须是“端点 -> MediatR 命令/查询 -> 仓库/查询处理器 -> `ApplicationDbContext`”；此处引入的任何 `IAppHubStateStore` 都只是迁移期间保留测试的临时适配器。

**文件：**

- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Domain/AppHubFacts.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Endpoints/Connectors/ConnectorIngestionEndpoints.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Endpoints/Instances/InstanceQueryEndpoints.cs`
- 修改：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/AppHubStateStoreTests.cs`

- [ ] **步骤 1：添加 AppHub 存储接口**

在 `InMemoryAppHubStateStore` 上方添加接口，位置在 `AppHubFacts.cs` 中：

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

然后更改类声明：

```csharp
public sealed class InMemoryAppHubStateStore : IAppHubStateStore
```

- [ ] **步骤 2：在 AppHub.Web 中注册接口**

修改 `Program.cs`：

```csharp
builder.Services.AddSingleton<IAppHubStateStore, InMemoryAppHubStateStore>();
```

移除先前直接的 singleton 注册。

- [ ] **步骤 3：更新 endpoint 构造函数**

在 `ConnectorIngestionEndpoints.cs` 和 `InstanceQueryEndpoints.cs` 中，将每个 `InMemoryAppHubStateStore` 类型的构造函数参数替换为 `IAppHubStateStore`。

示例：

```csharp
public sealed class RegisterApplicationEndpoint(IAppHubStateStore store) : Endpoint<ApplicationRegistration>
```

- [ ] **步骤 4：保持领域测试使用内存态实现**

在 `AppHubStateStoreTests.cs` 中保留具体构造：

```csharp
var store = new InMemoryAppHubStateStore();
```

该测试继续作为接口的快速行为基线。

- [ ] **步骤 5：运行 AppHub 测试**

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Domain.Tests/Nerv.IIP.AppHub.Domain.Tests.csproj
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj
```

预期结果：两条命令都以 `0` 退出。

- [ ] **步骤 6：提交**

```powershell
git add backend/services/AppHub
git commit -m "refactor: introduce apphub state store interface"
```

## 任务 3：实施 AppHub NetCorePal 聚合、Repository、Command 和 PostgreSQL Profile

> 修订后的 netcorepal 执行说明：使用 netcorepal/CleanDDD 文件替换早期的原始 `PostgresAppHubStateStore` 草图。保留旧草图中的行为作为映射参考，但最终代码应使用 `ApplicationDbContext : AppDbContextBase`、实体配置、基于 `RepositoryBase` 的 repository、command/query handler 和 mediator 驱动的 endpoint。

AppHub 的目标 CleanDDD 形态：

1. Domain 聚合位于 `Nerv.IIP.AppHub.Domain/AggregatesModel` 下：`Application`、`ManagedNode`、`ApplicationInstance`、`InstanceHeartbeat`、`InstanceStateHistory`，以及作为聚合拥有实体或值对象的幂等事实。
2. 强类型 ID 使用 `IGuidStronglyTypedId`，除非现有外部 key 必须保持字符串类型；公开协议 key 在 API 边界继续作为契约字符串。
3. Command 位于 `Nerv.IIP.AppHub.Web/Application/Commands` 下：`RegisterApplicationCommand`、`RecordApplicationHeartbeatCommand`、`RecordInstanceStateSnapshotCommand`。
4. Query 位于 `Nerv.IIP.AppHub.Web/Application/Queries` 下：`ListApplicationInstancesQuery`、`GetApplicationInstanceDetailQuery`。
5. Endpoint 调用 `IMediator.Send(...)` 并返回现有契约响应形态。
6. Infrastructure 拥有 `ApplicationDbContext`、实体配置和 repository；它实施 PostgreSQL profile 注册，且不向 Domain/Web Application 代码泄漏 Npgsql 类型。

**文件：**

- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Repositories/*.cs`
- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/AppHubPersistenceServiceCollectionExtensions.cs`
- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Commands/*.cs`
- 创建：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/Queries/*.cs`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Nerv.IIP.AppHub.Web.csproj`
- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- 创建：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubPostgresProfileTests.cs`

- [ ] **步骤 1：创建 AppHub `ApplicationDbContext` 和实体配置**

下面的遗留映射参考使用单文件行模型草图。最终版本实施为 `ApplicationDbContext.cs` 加 `EntityConfigurations/*.cs`；保持表/schema/列意图等价。

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

- [ ] **步骤 2：创建 AppHub repository 和 command/query handler**

下面的遗留映射参考展示旧的存储形态算法。最终版本通过 repository 和 handler 实施：

1. `RegisterApplicationCommandHandler` 执行幂等查找，创建/更新应用、节点、实例和能力事实，并返回 `RegistrationResult`。
2. `RecordApplicationHeartbeatCommandHandler` 更新心跳/存活性事实。
3. `RecordInstanceStateSnapshotCommandHandler` 记录状态历史。
4. `ListApplicationInstancesQueryHandler` 和 `GetApplicationInstanceDetailQueryHandler` 返回现有契约响应类型。

最终实现中不得将此存储注入 endpoint：

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

- [ ] **步骤 3：添加 AppHub netcorepal 持久化 DI**

启用 PostgreSQL 模式时，最终 DI 必须包含 `AddRepositories(typeof(ApplicationDbContext).Assembly)`、`AddUnitOfWork<ApplicationDbContext>()`、`AddContext().AddEnvContext().AddCapContextProcessor()`、`AddIntegrationEvents(typeof(Program)).UseCap<ApplicationDbContext>(...)` 和 `AddCap(...UseNetCorePalStorage<ApplicationDbContext>()...)`。下面的遗留扩展是 provider 切换草图；将类型名称从 `AppHubDbContext` 更新为 `ApplicationDbContext`，并注册 repository/handler，而不是具体 `PostgresAppHubStateStore`。

创建 `AppHubPersistenceServiceCollectionExtensions.cs`：

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

- [ ] **步骤 4：将 AppHub.Web 接入 Infrastructure**

修改 `Nerv.IIP.AppHub.Web.csproj`：

```xml
<ProjectReference Include="..\Nerv.IIP.AppHub.Infrastructure\Nerv.IIP.AppHub.Infrastructure.csproj" />
```

修改 `Program.cs`：

```csharp
using Nerv.IIP.AppHub.Infrastructure;
```

将存储注册替换为：

```csharp
builder.Services.AddAppHubPersistence(builder.Configuration);
```

在 `var app = builder.Build();` 之后，为 PostgreSQL 模式添加以下开发引导代码：

```csharp
if (string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppHubDbContext>().Database.EnsureCreated();
}
```

- [ ] **步骤 5：添加 AppHub PostgreSQL 集成测试**

创建 `AppHubPostgresProfileTests.cs`：

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

- [ ] **步骤 6：运行 AppHub 测试**

先运行快速测试：

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj
```

然后在 PostgreSQL 可用时运行：

```powershell
$env:NERV_IIP_TEST_POSTGRES="Host=localhost;Port=5432;Database=nerv_iip_apphub_test;Username=nerv;Password=nerv"
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter Postgres_store_persists_registration_heartbeat_and_state
```

预期结果：两条命令都以 `0` 退出。

- [ ] **步骤 7：提交**

```powershell
git add backend/services/AppHub
git commit -m "feat: persist apphub facts in postgres"
```

## 任务 4：实施 Ops NetCorePal 聚合、Repository、Command 和 PostgreSQL Profile

> 修订后的 netcorepal 执行说明：使用 netcorepal/CleanDDD 文件替换早期的原始 `PostgresOpsStateStore` 草图。保留旧存储行为作为任务创建、调度、结果记录、审计和幂等性的映射参考，但最终 endpoint 路径必须经过 MediatR command/query handler。

Ops 的目标 CleanDDD 形态：

1. Domain 聚合位于 `Nerv.IIP.Ops.Domain/AggregatesModel/OperationTaskAggregate` 下：`OperationTask` 是聚合根，并以拥有实体/值对象形式包含 `OperationAttempt`、`AuditRecord`、失败原因和幂等事实。
2. 强类型 ID 使用 `IGuidStronglyTypedId`，除非现有契约 ID 必须在 API 边界保持稳定。
3. Command 位于 `Nerv.IIP.Ops.Web/Application/Commands` 下：`CreateOperationTaskCommand`、`DispatchPendingOperationsCommand`、`RecordOperationResultCommand`。
4. Query 位于 `Nerv.IIP.Ops.Web/Application/Queries` 下：`GetOperationTaskQuery` 和任何现有待处理任务响应投影。
5. Endpoint 调用 `IMediator.Send(...)`，并保持现有路由/请求/响应契约稳定。
6. Infrastructure 拥有 `ApplicationDbContext`、实体配置和 repository；它实施 PostgreSQL profile 注册，且不向 Domain/Web Application 代码泄漏 Npgsql 类型。

**文件：**

- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Domain/InMemoryOpsStateStore.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Repositories/*.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/OpsPersistenceServiceCollectionExtensions.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Commands/*.cs`
- 创建：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/*.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Nerv.IIP.Ops.Web.csproj`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/OperationTasks/OperationTaskEndpoints.cs`
- 创建：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsPostgresProfileTests.cs`

- [ ] **步骤 1：添加 Ops 存储接口**

在 `InMemoryOpsStateStore` 上方添加此接口：

```csharp
public interface IOpsStateStore
{
    OperationTaskResponse Create(CreateOperationTaskRequest request, DateTimeOffset now);
    OperationTaskResponse Get(string operationTaskId);
    PendingOperationTasksResponse DispatchPending(string organizationId, string environmentId, string connectorHostId, int take, DateTimeOffset now);
    OperationTaskResponse RecordResult(OperationResult result);
}
```

然后更改：

```csharp
public sealed class InMemoryOpsStateStore : IOpsStateStore
```

- [ ] **步骤 2：更新 Ops endpoint 构造函数**

在 `OperationTaskEndpoints.cs` 中，将每个 `InMemoryOpsStateStore` 类型的构造函数参数替换为 `IOpsStateStore`。

- [ ] **步骤 3：创建 Ops `ApplicationDbContext` 和实体配置**

下面的遗留映射参考使用单文件行模型草图。最终版本实施为 `ApplicationDbContext.cs` 加 `EntityConfigurations/*.cs`；保持表/schema/列意图等价。

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

- [ ] **步骤 4：创建 Ops repository 和 command/query handler**

下面的遗留映射参考展示旧的存储形态算法。最终版本通过 repository 和 handler 实施：

1. `CreateOperationTaskCommandHandler` 处理幂等任务创建和审计记录创建。
2. `DispatchPendingOperationsCommandHandler` 以租约方式把待处理工作分派给 Connector Host，并创建尝试事实。
3. `RecordOperationResultCommandHandler` 更新任务/尝试状态和审计事实。
4. `GetOperationTaskQueryHandler` 返回现有契约响应类型。

最终实现中不得将此存储注入 endpoint：

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

- [ ] **步骤 5：添加 Ops netcorepal 持久化 DI 并接入 Web**

启用 PostgreSQL 模式时，最终 DI 必须包含 `AddRepositories(typeof(ApplicationDbContext).Assembly)`、`AddUnitOfWork<ApplicationDbContext>()`、`AddContext().AddEnvContext().AddCapContextProcessor()`、`AddIntegrationEvents(typeof(Program)).UseCap<ApplicationDbContext>(...)` 和 `AddCap(...UseNetCorePalStorage<ApplicationDbContext>()...)`。下面的遗留扩展是 provider 切换草图；将类型名称从 `OpsDbContext` 更新为 `ApplicationDbContext`，并注册 repository/handler，而不是具体 `PostgresOpsStateStore`。

创建 `OpsPersistenceServiceCollectionExtensions.cs`：

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

修改 `Nerv.IIP.Ops.Web.csproj` 以引用 Infrastructure：

```xml
<ProjectReference Include="..\Nerv.IIP.Ops.Infrastructure\Nerv.IIP.Ops.Infrastructure.csproj" />
```

修改 `Program.cs`：

```csharp
using Nerv.IIP.Ops.Infrastructure;
```

替换存储注册：

```csharp
builder.Services.AddOpsPersistence(builder.Configuration);
```

在 `var app = builder.Build();` 之后添加：

```csharp
if (string.Equals(builder.Configuration["Persistence:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<OpsDbContext>().Database.EnsureCreated();
}
```

- [ ] **步骤 6：添加 Ops PostgreSQL 集成测试**

创建 `OpsPostgresProfileTests.cs`：

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

- [ ] **步骤 7：运行 Ops 测试**

```powershell
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj
$env:NERV_IIP_TEST_POSTGRES="Host=localhost;Port=5432;Database=nerv_iip_ops_test;Username=nerv;Password=nerv"
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter Postgres_store_persists_task_attempt_and_audit_records
```

预期结果：两条命令都以 `0` 退出。

- [ ] **步骤 8：提交**

```powershell
git add backend/services/Ops
git commit -m "feat: persist ops task facts in postgres"
```

## 任务 5：添加 NetCorePal 代码分析 Endpoint

**文件：**

- 修改：`backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- 修改：`backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- 修改：`backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests`
- 修改：`backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests`

- [ ] **步骤 1：添加 AppHub code-analysis endpoint**

在 AppHub `Program.cs` 的常规路由注册后添加 endpoint：

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

添加 `using NetCorePal.Extensions.CodeAnalysis;`，并将聚合类型名称调整为任务 3 创建的精确文件。

- [ ] **步骤 2：添加 Ops code-analysis endpoint**

在 Ops `Program.cs` 的常规路由注册后添加 endpoint：

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

添加 `using NetCorePal.Extensions.CodeAnalysis;`，并将聚合类型名称调整为任务 4 创建的精确文件。

- [ ] **步骤 3：添加冒烟测试**

为每个服务添加一个 Web 测试，启动 Web 应用并检查 `/code-analysis` 返回 `text/html`，且非空正文至少包含一个 command 或聚合类型名称。

- [ ] **步骤 4：运行 code-analysis 测试**

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --filter CodeAnalysis
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --filter CodeAnalysis
```

预期结果：两条命令都以 `0` 退出。

- [ ] **步骤 5：提交**

```powershell
git add backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests
git commit -m "feat: expose netcorepal code analysis"
```

## 任务 6：添加真实基础设施验证脚本

**文件：**

- 修改：`scripts/verify-second-slice-ops.ps1`
- 修改：`scripts/verify-third-slice-console.ps1`
- 创建：`scripts/verify-fourth-slice-real-infra.ps1`
- 修改：`.codex/environments/environment.toml`

- [ ] **步骤 1：为第二阶段脚本添加 PostgreSQL 开关**

在 `scripts/verify-second-slice-ops.ps1` 顶部的 strict mode 之后添加：

```powershell
param(
  [switch]$UsePostgres
)
```

启动 job 前定义：

```powershell
$appHubDb = "Host=localhost;Port=5432;Database=nerv_iip;Username=nerv;Password=nerv"
$opsDb = "Host=localhost;Port=5432;Database=nerv_iip;Username=nerv;Password=nerv"
```

在 AppHub Start-Job 脚本块中接收 `$usePostgres` 和 `$connectionString`；启用时设置：

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__AppHubDb = $connectionString
```

在 Ops Start-Job 脚本块中接收 `$usePostgres` 和 `$connectionString`；启用时设置：

```powershell
$env:Persistence__Provider = "PostgreSQL"
$env:ConnectionStrings__OpsDb = $connectionString
```

未提供 `$UsePostgres` 时，保持默认内存态路径不变。

- [ ] **步骤 2：为第三阶段脚本添加 PostgreSQL 开关**

在 `scripts/verify-third-slice-console.ps1` 顶部添加：

```powershell
param(
  [switch]$UsePostgres
)
```

替换第二阶段调用：

```powershell
if ($UsePostgres) {
  pwsh scripts/verify-second-slice-ops.ps1 -UsePostgres
}
else {
  pwsh scripts/verify-second-slice-ops.ps1
}
```

- [ ] **步骤 3：创建第四阶段验证脚本**

创建 `scripts/verify-fourth-slice-real-infra.ps1`：

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

- [ ] **步骤 4：添加 Codex 环境操作**

将此 action 追加到 `.codex/environments/environment.toml`：

```toml
[[actions]]
name = "验证第四阶段真实基础设施"
icon = "tool"
command = "pwsh scripts/verify-fourth-slice-real-infra.ps1"
```

- [ ] **步骤 5：运行第四阶段验证**

```powershell
pwsh scripts/verify-fourth-slice-real-infra.ps1
```

预期最后一行：

```text
Fourth vertical slice real infrastructure verified.
```

- [ ] **步骤 6：提交**

```powershell
git add scripts/verify-second-slice-ops.ps1 scripts/verify-third-slice-console.ps1 scripts/verify-fourth-slice-real-infra.ps1 .codex/environments/environment.toml
git commit -m "test: verify vertical slice on real infrastructure"
```

## 任务 7：添加平台级 Aspire AppHost

**文件：**

- 创建：`infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`
- 创建：`infra/aspire/Nerv.IIP.AppHost/Program.cs`

- [ ] **步骤 1：创建 AppHost 项目文件**

创建 `Nerv.IIP.AppHost.csproj`：

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

- [ ] **步骤 2：创建 AppHost Program**

创建 `Program.cs`：

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

如果生成的项目类型名称不同，先构建一次，只将 `Projects.*` 标识符调整为生成的名称；保持项目引用和资源名称不变。

- [ ] **步骤 3：构建 AppHost**

```powershell
dotnet restore infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

预期结果：两条命令都以 `0` 退出。

- [ ] **步骤 4：冒烟运行 AppHost**

```powershell
dotnet run --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

预期结果：AppHost 启动，并在 Aspire dashboard 输出中列出 `postgres`、`redis`、`rabbitmq`、`apphub`、`ops`、`gateway` 和 `connector-host` 资源。确认启动后使用 Ctrl+C 停止。

- [ ] **步骤 5：提交**

```powershell
git add infra/aspire/Nerv.IIP.AppHost
git commit -m "feat: add platform aspire apphost"
```

## 任务 8：更新第四阶段文档

**文件：**

- 修改：`README.md`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`docs/architecture/deployment-baseline.md`
- 修改：`docs/architecture/api-contract-and-codegen.md`
- 修改：`docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md`

- [x] **步骤 1：更新 README 状态和计划索引**

将本计划添加到“实施计划”清单：

```markdown
4. docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md
```

在“当前状态”的第三阶段段落后添加：

```markdown
第四阶段真实基础设施底座纵切可以用 `scripts/verify-fourth-slice-real-infra.ps1` 验证：脚本会拉起 PostgreSQL、Redis 和 RabbitMQ，本地验证 AppHub/Ops 的 netcorepal/CleanDDD PostgreSQL profile、code-analysis endpoint，并在 PostgreSQL 模式下复跑第三阶段控制台纵切。
```

- [x] **步骤 2：更新实施就绪状态**

在第三次迭代章节下添加“第四迭代计划范围”章节：

```markdown
### 第四迭代计划范围

1. AppHub 和 Ops 作为 netcorepal/CleanDDD 迁移试点，落 Domain aggregate、Application command/query、Infrastructure repository/ApplicationDbContext 和 mediator-driven endpoint。
2. PostgreSQL 使用服务级 schema：AppHub 使用 `apphub`，Ops 使用 `ops`；provider 选择只留在 Infrastructure/profile/test/deployment 层。
3. AppHub/Ops 暴露 `/code-analysis`，用于查看 netcorepal 识别的命令、查询、聚合、事件和处理器流向。
4. `scripts/verify-fourth-slice-real-infra.ps1` 作为第四阶段验收入口，默认通过 `infra/docker-compose.dev.yml` 拉起依赖。
5. 平台级 AppHost 落到 `infra/aspire/Nerv.IIP.AppHost`，覆盖 AppHub、Ops、Gateway、Connector Host、PostgreSQL、Redis 和 RabbitMQ。
6. PlatformGateway、Connector Host、Contracts/SDK 和 frontend console 不强行套完整 netcorepal 三项目模型；IAM 完整授权、FileStorage 上传下载、CAP outbox、通知和审批不进入本阶段实现范围。
```

- [x] **步骤 3：更新部署基线**

在“当前阶段”中，将 AppHost 尚未落地的说明替换为：

```markdown
第四阶段已落地平台级 AppHost 到 `infra/aspire/Nerv.IIP.AppHost`，用于表达 AppHub、Ops、Gateway、Connector Host 与 PostgreSQL、Redis、RabbitMQ 的首批真实基础设施拓扑。`infra/docker-compose.dev.yml` 继续作为验证脚本拉起本地依赖的稳定入口。
```

在日志章节中，保持以下部署决策明确：

1. 第四阶段默认为 `collector-only`；不得要求 Grafana、Loki、Elasticsearch、Seq 或 ClickHouse。
2. Microsoft 官方、可自托管、开源/免费和社区活跃是选择偏好，而不是全有或全无的门禁。
3. `aspire-dashboard` 是为 Aspire 和 Docker 兼容环境选定的短期可观测性 UI profile。
4. Aspire Dashboard 必须记录为短期且内存态；它不是生产日志持久化后端。
5. Docker Compose 必须同时支持 `collector-only` 和可选 `aspire-dashboard` profile/overlay。
6. 包/脚本安装不得要求容器；它们至少必须配置滚动 JSONL 文件，并且在可用时可以配置 OTLP endpoint 或独立 Aspire Dashboard。
7. 内置日志持久化使用滚动 JSONL 热文件、Log Archive Worker、File Storage `.jsonl.gz` 分块和独立 `observability` 元数据索引。
8. 产品控制台日志查看通过 PlatformGateway；前端不得直接查询 Aspire Dashboard、归档存储或任何可观测性后端。
9. Gateway 在返回日志条目前必须强制执行 IAM、组织/环境范围、时间窗口限制、分页、限流和脱敏。
10. 默认索引数据库为 PostgreSQL `observability` schema 或 database；SQLite 仅用于诊断，外部搜索引擎作为 adapter。

- [x] **步骤 4：更新 API 契约日志查询规则**

在 `docs/architecture/api-contract-and-codegen.md` 中添加 `Console Log Query API` 章节。它必须定义以下未来 Gateway 操作，但不在第四阶段代码中实施：

1. `queryConsoleLogs` 对应 `/api/console/v1/logs/query`。
2. `getConsoleInstanceLogs` 对应 `/api/console/v1/instances/{instanceKey}/logs`。
3. `getConsoleOperationLogs` 对应 `/api/console/v1/operation-tasks/{operationTaskId}/logs`。
4. 包含 `timestamp`、`level`、`service`、`message`、`instanceKey`、`operationTaskId`、`correlationId`、`traceId`、`labels`、`fields`、`source`、`nextCursor` 和 `partial` 的后端中立响应 DTO。

- [x] **步骤 5：验证后添加完成记录**

`pwsh scripts/verify-fourth-slice-real-infra.ps1` 通过后，在本计划顶部附近添加 `Completion Record` 章节，包含精确命令和最终输出行。

- [ ] **步骤 6：提交**

```powershell
git add README.md docs/architecture/implementation-readiness.md docs/architecture/deployment-baseline.md docs/architecture/api-contract-and-codegen.md docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md
git commit -m "docs: document fourth real infrastructure slice"
```

## 执行顺序

1. 必须首先运行任务 1，因为 AppHub 和 Ops Domain/Web/Infrastructure 需要 netcorepal、EF、CAP 和 PostgreSQL profile 包基线。
2. 必须在任务 3 之前运行任务 2，因为它冻结当前隐藏在内存态存储中的 AppHub 行为。
3. 任务 1 完成后，任务 3 和任务 4 可以并行运行；它们涉及不同服务文件夹，并遵循相同的 netcorepal/CleanDDD 目标形态。
4. 任务 5 依赖任务 3 和任务 4，因为 code-analysis 必须包含迁移后的 command/query/aggregate/repository 流程。
5. 任务 6 依赖任务 3、4 和 5，因为真实基础设施脚本必须验证 PostgreSQL 模式和 code-analysis 冒烟测试。
6. 任务 7 可在任务 3 和任务 4 后运行，因为 AppHost 应以 PostgreSQL 模式启动服务。
7. 任务 8 最后运行，因为它记录已经验证的行为。

任务 2 之后建议并行执行：

1. 一名执行者实施任务 3 的 AppHub netcorepal/CleanDDD 迁移和 PostgreSQL profile。
2. 一名执行者实施任务 4 的 Ops netcorepal/CleanDDD 迁移和 PostgreSQL profile。
3. 服务 DI 形态已知后，一名执行者准备任务 7 的 AppHost 项目。

## 第四次迭代完成定义

满足以下全部条件时，第四次迭代才算完成：

1. AppHub Web 端点调用 MediatR 命令/查询，而不是具体存储或 DbContext。
2. Ops Web 端点调用 MediatR 命令/查询，而不是具体存储或 DbContext。
3. AppHub 和 Ops Domain 项目包含 netcorepal 聚合根、强类型 ID 和领域事件，且不含 provider 特定代码。
4. AppHub 和 Ops Infrastructure 项目包含 `ApplicationDbContext : AppDbContextBase`、实体配置和基于 netcorepal repository 模式的 repository。
5. 内存态 AppHub 和 Ops 行为测试仍作为回归基线通过。
6. AppHub PostgreSQL 集成测试证明注册、心跳和状态事实在新 DbContext 中仍然存在。
7. Ops PostgreSQL 集成测试证明任务、尝试和审计事实在新 DbContext 中仍然存在。
8. AppHub 和 Ops 公开 `/code-analysis` endpoint，返回非空 netcorepal 代码流 HTML。
9. 后端服务在应用代码中使用 `ILogger<T>`，在 Host/Observability 注册中使用 Serilog，并使用 OpenTelemetry/OTLP 导出日志。
10. 本地日志回退实施为有界滚动 JSONL 文件；可选 .NET Aspire Dashboard profile 记录为短期本地遥测查看工具。
11. 不向 AppHub/Ops/IAM/FileStorage PostgreSQL schema 添加运行时日志表；Ops `AuditRecord` 仍仅用于审计，不是通用日志存储。
12. 部署文档定义内置日志持久化目标：Log Archive Worker、File Storage 压缩分块和独立 PostgreSQL `observability` 元数据索引。
13. 部署文档定义跨 Aspire AppHost、Docker Compose 和包/脚本安装的可观测性资源 profile。
14. 部署文档定义默认 `collector-only`、可选 `aspire-dashboard` 短期 UI、可选 `log-archive` 持久化 profile，且默认不依赖 Grafana/Loki/Elastic/Seq/ClickHouse。
15. API 契约文档将控制台日志查询定义为未来 Gateway OpenAPI 能力，并禁止前端直接访问可观测性后端。
16. `scripts/verify-second-slice-ops.ps1` 在没有 PostgreSQL 时仍可使用。
17. `scripts/verify-fourth-slice-real-infra.ps1` 启动本地 PostgreSQL、Redis 和 RabbitMQ，并以 `0` 退出。
18. `scripts/verify-third-slice-console.ps1 -UsePostgres` 以 `0` 退出。
19. 平台 AppHost 为 AppHub、Ops、Gateway、Connector Host、PostgreSQL、Redis 和 RabbitMQ 构建资源；OpenTelemetry Collector 继续作为已记录的后续可观测性资源 profile。
20. Provider 特定数据库代码隔离在 Infrastructure DI 扩展、profile 测试、脚本和 AppHost/部署配置中；Domain/Application/Endpoint/SDK 代码不引用 Npgsql 或 PostgreSQL 专有 SQL。
21. 出于本计划记录的原因，Gateway、Connector Host、Contracts/SDK 和前端控制台仍在完整 netcorepal 服务模型之外。
22. 文档注明第四阶段验证命令，并将 IAM/FileStorage/审批/通知/GaussDB-DMDB 生产 profile 保持为后续范围。

## 自检

规范覆盖：

1. README 下一阶段“PostgreSQL/RabbitMQ/Redis 真实基础设施和 database profile 形态”项：由任务 1、3、4、6 和 7 覆盖。
2. NetCorePal 采用决策：由“NetCorePal 采用决策”章节和任务 1、3、4、5 覆盖。
3. 日志库、字段和持久化边界：由任务 1 和 ADR/部署文档覆盖。
4. 可观测性后端资源 profile 和控制台日志查询边界：由部署基线和 API 契约文档覆盖。
5. 部署基线 AppHost 方向：由任务 7 覆盖。
6. 当前第三阶段控制台链路：由任务 6 使用 `-UsePostgres` 保持。
7. IAM/FileStorage/Ops 审批/通知后续工作：明确位于此聚焦计划范围外，并记录在文档中。

占位符扫描：

1. 不存在未解决的占位标记。
2. 文件路径、命令和预期输出均明确。
3. 代码片段定义具体方法名称、行模型、接口和服务注册形态；当修订后的 netcorepal 说明与遗留存储片段不同时，以前者为准。

类型一致性：

1. AppHub command/query 保持当前 `InMemoryAppHubStateStore` 公开行为。
2. Ops command/query 保持当前 `InMemoryOpsStateStore` 公开行为。
3. PostgreSQL 连接字符串使用与 `infra/docker-compose.dev.yml` 相同的本地凭证。
4. 本阶段不实施 GaussDB/DMDB；代码形态应使其成为未来 profile 添加项，而不是要求重写业务层。
5. AppHost 资源名称与现有验证脚本使用的服务名称一致。

## 执行交接

计划已完成并保存到 `docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md`。有两种执行选项：

**1. 子代理驱动（推荐）**——我为每个任务派发新的子代理，在任务之间审核，快速迭代

**2. 内联执行**——在此会话中使用 executing-plans 执行任务，按检查点分批执行

采用哪种方式？
