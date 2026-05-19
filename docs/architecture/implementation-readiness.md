# 实施状态清单

本文档记录 Nerv-IIP 从“文档冻结完成”到“第一、第二、第三阶段纵切已落地，第四阶段真实基础设施门禁已通过，第五阶段迁移发布底座已通过，第六阶段 schema governance hardening 已完成，第七阶段 IAM Persistent Auth Foundation 已落地，脚本自动化治理开始收敛”的状态，给出首批实施的环境前置、目录落点、引用规则、已完成范围和后续边界。

## 当前结论

1. 平台 HTTP 服务命名已经冻结为 .Web、.Domain、.Infrastructure。
2. Connector Host 与平台的 v1 协议边界已经冻结到公开接口和最小对象级别。
3. 后端 CleanDDD 与 netcorepal 的模板参数、目录、事件、事务、仓储和测试约定已经冻结。
4. 核心术语、Platform SDK 模块边界、IAM 对外授权边界、文件存储基线、通知能力基线、知识源生命周期和首批纵切验收口径已经补齐。
5. backend、connector-hosts 两个工作面已经完成第一迭代纵切骨架，可通过 `scripts/verify-first-slice.ps1` 做本地验证。
6. 第二阶段低风险动作闭环已经落地，可通过 `scripts/verify-second-slice-ops.ps1` 验证 Gateway、Ops、Connector Host 和 Docker Connector 的 restart 闭环。
7. 平台 HTTP 接口统一使用 FastEndpoints；新增接口必须放在 Web 项目的 `Endpoints/` 目录，不在启动文件中写 Minimal API 路由映射。
8. 部署策略已经冻结为“多部署目标，单一部署模型”：平台级 Aspire AppHost 作为统一拓扑入口，Docker Compose、安装包和整合安装脚本作为不同环境的交付目标。
9. 仓库根必须保留 `NuGet.config` 固定 NuGet restore 包源，避免本机全局多包源配置与 Central Package Management、`TreatWarningsAsErrors` 组合后触发 `NU1507`。
10. 第三阶段控制台纵切已经落地，可通过 `scripts/verify-third-slice-console.ps1` 验证 Gateway OpenAPI、Hey API 生成、Vue 控制台 typecheck/test/build。
11. 前端工作区使用 Vite+ 作为统一工具入口；`pnpm -C frontend check`、`lint`、`fmt` 需要 Node.js `>=22.18.0`，仓库根 `.node-version` 固定为 22.22.3，本机验证版本为 OpenJS.NodeJS.22 v22.22.3。
12. 技术栈官方文档与源码仓库链接统一维护在 docs/architecture/technology-stack-references.md；新增长期技术选型时必须同步更新，避免同名项目或社区分叉造成歧义。
13. 第四阶段已经补齐 AppHub/Ops 的 netcorepal/CleanDDD PostgreSQL profile、code-analysis endpoint、`scripts/verify-fourth-slice-real-infra.ps1` 和平台级 `infra/aspire/Nerv.IIP.AppHost`；当前 AppHost build 与完整真实基础设施验证均已通过。
14. 第四阶段统一验收口径见 docs/architecture/fourth-vertical-slice-real-infra.md；后续生产级迁移、初始化、seed 和回滚策略按 docs/adr/0009-database-migration-release-and-seed-strategy.md 执行。
15. 第五阶段 Release-grade Persistence Foundation 已落地：AppHub/Ops 已有初始 migrations，PostgreSQL profile tests 已从 `EnsureCreated()` 改为 `MigrateAsync()`，第五阶段验证脚本已经通过。
16. 前端 Design System 尚未冻结；除 OpenAPI 生成客户端和质量门禁外，后端基础阶段不得新增控制台页面、视觉组件、组件库迁移或样式体系决策。
17. 数据库 schema、建表注释、schema catalog 和发布 runbook 已补齐第一版，后续持久化服务必须先满足 docs/architecture/database-schema-conventions.md、docs/architecture/database-schema-catalog.md 和 docs/architecture/database-release-runbook.md。
18. 第六阶段 Schema Governance & Migration Hardening 用 AppHub/Ops 作为已迁移服务样本，把业务表注释、业务列注释、JSON/text 兼容注释、string ID 约束和 service-schema migrations history 配置固化为测试门禁；IAM 已沿用该门禁，FileStorage 等新增持久化服务开工前必须继续沿用。
19. 第七阶段 IAM Persistent Auth Foundation 已落地：IAM 保留 InMemory profile，同时具备 PostgreSQL `iam` schema、初始 admin seed、JWT access token、refresh token rotation、session revoke、`/me`、Connector Host credential validation 和 internal authorization check 的后端持久化基线。
20. 现有 PlatformGateway Console API 已接入 IAM-backed permission enforcement；Gateway 只转发 bearer token 与 permission/context，不直接引用 IAM Domain 或 Infrastructure。
21. Console Auth + shadcn-vue Baseline 已提供最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线；完整用户/角色/会话管理、OAuth/OIDC、SSO、MFA 和 ABAC 仍属于后续阶段。
22. 脚本自动化治理已冻结到 ADR 0010 和 docs/architecture/script-automation-governance.md；IAM、第五阶段和第四阶段核心 verify 脚本已迁移到 helper 门禁，Ubuntu WSL 兼容门禁已通过，新增或修改脚本必须声明分类、副作用、日志、清理和 helper 使用方式。

## 环境前置

根据 netcorepal-cloud-framework 官方入门文档，首批后端实施需要先满足以下条件：

1. 安装 .NET 10 SDK，作为 Nerv-IIP 当前目标框架。
2. 安装 Docker 环境，用于本地调试、自动化测试和依赖服务联调。
3. 创建服务时显式生成 `net10.0`；后续等 netcorepal-cloud-framework 明确适配 .NET 11 后，再统一升级到 .NET 11。
4. 安装 NetCorePal.Template：`dotnet new install NetCorePal.Template`。
5. 创建服务前运行 `dotnet new netcorepal-web --help` 核对本机模板参数。
6. 平台领域服务优先使用 netcorepal 的 web 模板作为初始骨架，但命令必须显式指定 `--Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false`，详见 docs/architecture/backend-cleanddd-netcorepal-guidelines.md。
7. 2026-05-17 已确认 NetCorePal.Template 3.2.0 支持 `PostgreSQL`、`GaussDB`、`DMDB` 等数据库参数；Nerv-IIP 默认落地 PostgreSQL profile，信创环境按 database profile 验证替换，不承诺无验证的完全无感切换。
8. 后续落地平台级 AppHost、Compose 生成和 Aspire Dashboard 时，需要安装 Aspire CLI；服务模板仍保持 `--UseAspire false`，避免生成服务级局部编排入口。
9. 第三阶段前端工具链需要 Node.js `>=22.18.0`、pnpm 11.1.2 和 Vite+ 0.1.21。仓库根 `.node-version` 固定为 22.22.3；本机已通过 `winget` 将 OpenJS.NodeJS.22 升级到 22.22.3，避免 Vite+ lint/fmt 路径读取 `vite.config.ts` 时触发 Node `22.17.x` 的 TS config 加载错误。
10. 第五阶段起仓库包含本地 `dotnet-tools.json`，用于固定 `dotnet-ef` 版本。首次生成或检查迁移前运行 `dotnet tool restore`，再使用 `dotnet tool run dotnet-ef ...`，避免依赖开发者全局工具。
11. AppHub/Ops 生成 EF migration 时必须显式进入 PostgreSQL profile：设置 `Persistence__Provider=PostgreSQL` 和对应 `ConnectionStrings__AppHubDb` 或 `ConnectionStrings__OpsDb`，否则 Web startup 默认 InMemory，design-time 无法解析服务 DbContext。

## 包源恢复基线

1. backend 启用 Central Package Management，且 `backend/Directory.Build.props` 将 warning 视为 error。
2. 如果开发者全局 NuGet 配置同时启用多个 HTTP 包源，NuGet 会产生 `NU1507`；在本仓库中该 warning 会被提升为 restore error。
3. 仓库根 `NuGet.config` 使用 `<clear />` 后只保留 `nuget.org`，用于确保本地与自动化 restore 结果一致。
4. 如客户或离线环境需要镜像源，应通过受控环境配置或后续交付脚本生成，不修改仓库默认 `NuGet.config` 为私有镜像。
5. 验证脚本默认使用仓库根 `NuGet.config`，不要求开发者修改个人全局 NuGet 源。

## 共享契约落点

1. 平台与 Connector Host 公共协议契约的源码事实来源固定放在 backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol。
2. Ops 公共协议契约的源码事实来源固定放在 backend/common/Contracts/Nerv.IIP.Contracts.Ops。
3. 首批单仓实施时，backend/Nerv.IIP.sln 与 connector-hosts/Nerv.IIP.ConnectorHost.sln 可以共同引用这些公开契约项目，确保注册、心跳、状态同步、运维任务和动作结果 DTO 只有一份代码实现。
4. Platform SDK 的首批源码落点固定放在 backend/common/Sdk，当前已经按 Core、Auth、ConnectorProtocol、FileStorage、Ops 五个最小模块拆分。
5. 发布边界上，Connector Host 只依赖 Platform SDK、版本化公开契约包、OpenAPI 契约或等价契约，不依赖主平台源码或服务实现项目。
6. Connector Host 与主平台主版本必须对齐；同一主版本内，Connector Host 小版本可以低于主平台小版本。
7. connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts 只保留 Connector Host 内部抽象，不复制公共协议 DTO。

## 首批工程创建顺序

### Wave 1. 底座

1. backend/Nerv.IIP.sln
2. backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol
3. backend/common/Contracts/Nerv.IIP.Contracts.Ops
4. backend/common/Sdk/Nerv.IIP.Sdk.Core
5. backend/common/Sdk/Nerv.IIP.Sdk.Auth
6. backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol
7. backend/common/Sdk/Nerv.IIP.Sdk.FileStorage
8. backend/common/Sdk/Nerv.IIP.Sdk.Ops
9. backend/common/Caching/Nerv.IIP.Caching
10. backend/common/Observability/Nerv.IIP.Observability
11. backend/common/Testing/Nerv.IIP.Testing

### Wave 2. 平台服务骨架

1. backend/services/AppHub/src/Nerv.IIP.AppHub.Web
2. backend/services/AppHub/src/Nerv.IIP.AppHub.Domain
3. backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure
4. backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web
5. backend/services/Iam/src/Nerv.IIP.Iam.Web
6. backend/services/Iam/src/Nerv.IIP.Iam.Domain
7. backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure
8. backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web
9. backend/services/FileStorage/src/Nerv.IIP.FileStorage.Domain
10. backend/services/FileStorage/src/Nerv.IIP.FileStorage.Infrastructure
11. backend/services/Ops/src/Nerv.IIP.Ops.Web
12. backend/services/Ops/src/Nerv.IIP.Ops.Domain
13. backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure

### Wave 3. Connector Host 工程骨架

1. connector-hosts/Nerv.IIP.ConnectorHost.sln
2. connector-hosts/src/Nerv.IIP.ConnectorHost.Host
3. connector-hosts/src/Nerv.IIP.ConnectorHost.Application
4. connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts
5. connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Abstractions
6. connector-hosts/src/Nerv.IIP.ConnectorHost.Connectors.Docker

### Wave 4. 前端骨架

1. frontend/package.json、pnpm-workspace.yaml、vite.config.ts、tsconfig.base.json
2. frontend/apps/console
3. frontend/packages/api-client
4. frontend/packages/ui
5. frontend/packages/app-shell
6. frontend/packages/layer-base、layer-platform、auth、shared-types 作为长期边界保留，不在第三阶段空建；Console Auth 当前为 app 内实现，`packages/auth` 仅在出现跨应用登录复用时按 frontend-structure 留档边界抽取。

## 引用规则

1. *.Web 可以引用同服务的 *.Domain、*.Infrastructure 和 backend/common 下的窄共享库。
2. *.Domain 不引用 *.Infrastructure，也不引用其它服务的 Domain。
3. *.Infrastructure 可以引用同服务的 *.Domain 和 backend/common 下的窄共享库。
4. PlatformGateway.Web 不引用任何服务的 Domain 或 Infrastructure，只通过稳定契约、OpenAPI 客户端或明确的查询接口聚合数据。
5. ConnectorHost.Application 可以引用 Nerv.IIP.Sdk.Core、Nerv.IIP.Sdk.Auth、Nerv.IIP.Sdk.ConnectorProtocol、Nerv.IIP.Sdk.Ops、Nerv.IIP.Contracts.ConnectorProtocol、Nerv.IIP.Contracts.Ops 和 Connector Host 内部抽象，但不直接引用平台服务实现项目；发布后以 Platform SDK、版本化契约包或等价契约作为依赖边界。
6. SDK 项目只能引用公开契约、Sdk.Core、Sdk.Auth 或其它明确允许的 SDK 模块，不能引用服务 Web、Domain、Infrastructure 或数据库模型。

## 开工边界

### 第一迭代已落地范围

1. backend 与 connector-hosts 两套 solution 创建。
2. Platform SDK 的 Core、Auth、ConnectorProtocol、FileStorage、Ops 最小项目骨架。
3. IAM 用户、角色、权限、会话、外部客户端、Connector Host 凭证和授权授予的最小事实骨架。
4. FileStorage 文件元数据、上传会话、上传指令、下载授权、Upload Provider 抽象、FilePurposePolicy、scanStatus 和 MinIO/object storage 适配的最小服务骨架。
5. AppHub registrations、heartbeats、state-snapshots 三个接口。
6. PlatformGateway 实例列表与实例详情查询接口。
7. Connector Host 通过 Nerv.IIP.Sdk.ConnectorProtocol 到 AppHub 的客户端与 Docker Connector 空壳。
8. 统一 OpenTelemetry 接线、health、build info、基础 structured logging。
9. 统一 FusionCache 接线、Redis L2/backplane、缓存键命名和首批读侧缓存策略。
10. 以 docs/architecture/first-vertical-slice.md 作为首批纵切验收口径。
11. 以 docs/architecture/backend-cleanddd-netcorepal-guidelines.md 作为后端代码放置、事件转换、事务和测试验收口径。

### 第二迭代已落地范围

1. `Nerv.IIP.Contracts.Ops` 与 `Nerv.IIP.Sdk.Ops` 已落地，用于运维任务、pending 拉取、任务详情和结果回传。
2. Ops.Web 已提供 operation task 创建、详情查询、pending 拉取和 operation result 回传接口。
3. PlatformGateway 已提供实例 restart facade 与 operation task detail facade。
4. Connector Host 已提供 operation loop，可领取低风险任务、调用 Connector 执行，并回传结果。
5. Docker Connector 已支持 `lifecycle.restart` 执行抽象。
6. Ops 当前会记录 OperationTask、OperationAttempt 和 AuditRecord 的内存态事实。
7. 以 docs/architecture/second-vertical-slice-ops.md 和 docs/superpowers/plans/2026-05-15-second-vertical-slice-low-risk-ops.md 作为第二阶段验收口径。

### 第三迭代已落地范围

1. Gateway 已提供控制台 OpenAPI 文档与稳定 operationId。
2. frontend 工作区已创建 pnpm workspace、Vite+ 根配置、console 应用、api-client、ui 和 app-shell 初版。
3. api-client 已通过 Hey API 从 Gateway OpenAPI 生成 types、fetch SDK 和 Pinia Colada query/mutation options。
4. console 首屏已展示实例列表、实例详情、restart 动作入口和 OperationTask 状态页。
5. Vite+ 质量门禁已覆盖 `check`、`lint`、`fmt`、`typecheck`、`test`、`build`。
6. 以 docs/architecture/third-vertical-slice-console.md、docs/superpowers/plans/2026-05-16-third-vertical-slice-console.md 和 scripts/verify-third-slice-console.ps1 作为第三阶段验收口径。

### 第四迭代已完成范围

1. AppHub 和 Ops 已作为 netcorepal/CleanDDD 迁移试点，落 Domain aggregate、Application command/query、Infrastructure repository/ApplicationDbContext 和 mediator-driven endpoint。
2. PostgreSQL 使用服务级 database 与 schema：AppHub 默认连接 `nerv_iip_apphub` 并使用 `apphub` schema，Ops 默认连接 `nerv_iip_ops` 并使用 `ops` schema；provider 选择只留在 Infrastructure/profile/test/deployment 层。
3. AppHub/Ops 已暴露 `/code-analysis`，用于查看 netcorepal 识别的命令、查询、聚合、事件和处理器流向。
4. `scripts/verify-fourth-slice-real-infra.ps1` 已作为第四阶段验收入口，默认通过 `infra/docker-compose.dev.yml` 拉起 PostgreSQL、Redis 和 RabbitMQ；本机 PostgreSQL 默认端口为 `15432`，避免撞到本机已有 `5432`。
5. 平台级 AppHost 已落到 `infra/aspire/Nerv.IIP.AppHost`，覆盖 AppHub、Ops、Gateway、Connector Host、PostgreSQL、Redis 和 RabbitMQ；AppHost 当前 build 通过，并为 AppHub/Ops 使用独立 database resource。
6. PlatformGateway、Connector Host、Contracts/SDK 和 frontend console 不强行套完整 netcorepal 三项目模型；IAM 完整授权、FileStorage 上传下载、CAP outbox、通知和审批不进入本阶段实现范围。
7. `pwsh scripts/verify-fourth-slice-real-infra.ps1` 已在 Docker Desktop 环境下通过，最终输出 `Fourth vertical slice real infrastructure verified.`。

### 第五迭代已完成范围

1. AppHub 和 Ops 已增加 EF Core 初始迁移，并把 PostgreSQL profile 测试切换到 migration-based schema creation。
2. AppHub.Web 和 Ops.Web 已移除 PostgreSQL 启动期 `EnsureCreated()`，只允许通过 `Persistence:AutoMigrate=true` 显式执行本地/dev auto-migration。
3. 已增加 service-owned migration runner，供测试、脚本和后续安装流程复用。
4. 已增加 `scripts/verify-fifth-slice-persistence-foundation.ps1`，覆盖迁移、后端 solution、connector-host solution 和 SDK/契约回归。
5. 前端只在后端 OpenAPI 变化时机械生成 api-client 并跑质量门禁；不新增页面、路由、组件皮肤或 Design System 决策。
6. 前端 Design System 规划记录在 docs/architecture/frontend-design-system-planning.md，后续需要独立 Superpowers spec 后才能实施。
7. `pwsh scripts/verify-fifth-slice-persistence-foundation.ps1` 已通过，最终输出 `Fifth slice release-grade persistence foundation verified.`。

### 第六迭代已完成范围

1. AppHub/Ops 已补齐业务表 table comment，并通过 EF Core migration 固化为 schema metadata。
2. AppHub `application_instances.Metadata`、`application_instances.Capabilities` 和 Ops `operation_tasks.ParametersJson`、`operation_attempts.FailureJson` 已补齐 JSON 格式、生产方、消费方和兼容策略注释。
3. AppHub/Ops PostgreSQL profile 已显式把 `__EFMigrationsHistory` 配置到各自 service schema。
4. `Nerv.IIP.Testing` 已提供 schema convention helper，覆盖 business table comment、business column comment、JSON/text compatibility、string ID 和 migrations history schema 规则。
5. AppHub/Ops Web tests 已增加 schema convention tests；IAM 已在第七迭代复用同一门禁，后续 FileStorage、Notification、Knowledge、AI Integration 和 Observability 索引建表前必须继续复用。
6. 客户发布包、安装脚本、备份恢复演练、seed 清单和诊断输出契约仍是后续交付工作，不属于本阶段完成范围。

### 第七迭代已完成范围

1. IAM 保留 InMemory profile，并新增 PostgreSQL profile，默认 schema 为 `iam`。
2. IAM 已有 `users`、`roles`、`role_permissions`、`memberships`、`user_sessions`、`connector_host_credentials` 和 seed manifest 等首批持久化表。
3. IAM 登录、refresh token rotation、logout/session revoke、`/me` 和 Connector Host credential validation 已可在 PostgreSQL profile 下运行。
4. IAM 初始 admin、platform admin role、seed permissions、membership 和 local Connector Host credential seed 具备幂等执行语义。
5. IAM schema convention tests 与 PostgreSQL profile tests 已作为后续 IAM 持久化变更门禁。
6. Gateway-wide permission enforcement 已覆盖现有 Console endpoints：实例列表、实例详情、restart 运维任务创建和 operation task detail 查询都会先通过 IAM internal authorization check。
7. Console Auth + shadcn-vue Baseline 已提供最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线；完整用户/角色/会话管理、OAuth/OIDC、SSO、MFA、ABAC 和客户发布 bundle 仍属于后续阶段。

### 当前初步使用方式

1. 运行 `pwsh scripts/verify-first-slice.ps1` 可验证 backend 与 connector-hosts 的 restore、build、test，以及 AppHub 到 PlatformGateway 的第一条本地纵切。
2. 运行 `pwsh scripts/verify-second-slice-ops.ps1` 可验证 Gateway、Ops、Connector Host 和 Docker Connector 的低风险 restart 闭环。
3. 运行 `pwsh scripts/verify-third-slice-console.ps1` 可验证 Gateway OpenAPI 导出、前端 api-client 生成、Vue 控制台 typecheck/test/build。
4. 运行 `pwsh scripts/verify-third-slice-console.ps1 -UsePostgres` 可在 PostgreSQL profile 下复跑第三阶段链路，前提是本地 PostgreSQL 和 RabbitMQ 已可用；可通过 `NERV_IIP_APPHUB_POSTGRES`、`NERV_IIP_IAM_POSTGRES` 与 `NERV_IIP_OPS_POSTGRES` 分别覆盖服务连接串。
5. 运行 `pwsh scripts/verify-fourth-slice-real-infra.ps1` 可拉起本地依赖并执行第四阶段真实基础设施门禁；脚本会重建 `nerv_iip_apphub_verify`、`nerv_iip_iam_verify` 和 `nerv_iip_ops_verify` 验证库，避免共享库或旧数据影响结果。
6. 运行 `pwsh scripts/verify-fifth-slice-persistence-foundation.ps1` 可验证 AppHub/Ops 迁移发布底座和后端 SDK/契约回归。
7. 运行 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1` 可验证 IAM PostgreSQL profile、迁移、seed、登录/刷新/退出、`/me`、Connector Host credential validation 和后端回归。
8. 运行 `pwsh scripts/check-script-governance.ps1` 可验证脚本解析、分类声明、高风险命令 wrapper 和 legacy exemption 是否仍受控。
9. 运行 `pwsh scripts/check-script-compatibility.ps1` 可在 macOS/Linux 上记录脚本兼容门禁证据；Windows 本地只能使用 `-AllowWindows -FastOnly` 做 smoke，不作为兼容性声明依据。
10. 运行 AppHub/Ops/IAM schema convention tests 可验证当前已迁移服务的 schema metadata 门禁。
11. 运行 `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore` 可验证平台级 AppHost 编译。
12. 运行 `pnpm -C frontend check`、`lint`、`fmt`、`typecheck`、`test`、`build` 可单独验证前端工作区质量门禁；第五阶段只有发生 OpenAPI/api-client 变化时才需要触发。
13. AppHub 当前提供 registration、heartbeat、state-snapshot 和内部实例查询接口。
14. PlatformGateway 当前提供实例列表、实例详情、实例 restart 和 operation task detail 查询接口；这些 Console API 需要 bearer token，并由 Gateway 转发到 IAM 做权限校验。
15. Connector Host 当前可通过 Platform SDK 将 Docker Connector 的发现结果上报到 AppHub，并通过 Ops SDK 拉取和回传低风险动作。
16. 当前实现用于本地开发和接口联调，不包含生产部署、完整认证授权 UI 或高风险动作审批。
17. 当前部署交付已经有平台级 AppHost 编译入口；生成式 Compose、安装包和 Windows/Linux 整合安装脚本尚未落地。

### 可以并行但不阻塞开工的事项

1. Ops 任务持久化、领取租约、并发执行限制、失败重试和持久化 outbox。
2. 高风险动作审批、人工确认 UI、权限 scope 和通知联动。
3. Sdk.Observability 的完整实现和诊断附件链路。
4. AI Integration 与 Knowledge 的具体代码骨架。
5. Notification 的具体代码骨架、站内通知纵切和外部通道 provider；边界口径应遵守 docs/architecture/notification-baseline.md。
6. KnowledgeSource 的完整管理后台，但生命周期口径应遵守 docs/architecture/knowledge-source-lifecycle.md。
7. 复杂 IAM 授权能力，包括跨组织委派、临时授权、完整 OAuth/OIDC 协议矩阵、MFA、SSO、细粒度 ABAC 与第三方应用市场。
8. 超出 Console Auth + shadcn-vue Baseline 的前端视觉系统、组件皮肤、主题和导航策略；需要先按 docs/architecture/frontend-design-system-planning.md 的 Future Spec Triggers 创建独立设计规格。
9. Compose 发布产物、安装包和整合安装脚本，口径见 docs/architecture/deployment-baseline.md 与 docs/architecture/database-release-runbook.md。
10. 剩余 legacy 脚本继续迁移到 docs/architecture/script-automation-governance.md 的 helper 和门禁；剩余顺序是 OpenAPI 导出、第三阶段 console、第二阶段 Ops、第一阶段 slice。

## 开工验收标准

满足以下条件时，说明仓库已经从“规划阶段”进入“可持续实施阶段”：

1. dotnet restore backend/Nerv.IIP.sln 通过。
2. dotnet build backend/Nerv.IIP.sln 通过。
3. dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln 通过。
4. dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln 通过。
5. AppHub.Web 可接收 registration、heartbeat、state snapshot。
6. Connector Host 可向本地 AppHub 成功发送至少一组注册、心跳、状态同步请求。
7. PlatformGateway 能查询到至少一个被注册的实例事实。
8. Ops.Web 可创建 restart OperationTask，并接收 Connector Host 回传的 OperationResult。
9. Connector Host 可领取 pending task，执行 `lifecycle.restart`，并在结果回传失败时先做本轮内存重试。

## 结论

Nerv-IIP 已经完成第一迭代接入查询纵切、第二迭代低风险动作闭环、第三迭代控制台纵切、第四迭代真实基础设施门禁、第五迭代迁移发布底座、第六迭代 schema governance hardening、第七迭代 IAM Persistent Auth Foundation，并已把现有 PlatformGateway Console API 接入 IAM-backed permission enforcement：backend/common、Iam、FileStorage、AppHub、PlatformGateway、Ops、Connector Host、Docker Connector、frontend console、api-client、ui、app-shell 和 infra/aspire 的最小工程结构与验证链路已经存在。Console Auth + shadcn-vue Baseline 已提供最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线。脚本自动化治理已补入 ADR 和架构说明，后续新增或修改脚本必须进入分类、副作用、helper、日志、进程清理和门禁口径。下一步可以在迁移基线、schema 门禁、IAM 持久化登录基线、Gateway 权限门禁和 Console Auth 基线之上进入完整用户/角色/会话管理、OAuth/OIDC/SSO/MFA/ABAC、FileStorage 上传下载、高风险动作审批、通知联动和多目标部署交付。真实持久化先主推 PostgreSQL，同时用 database profile 约束后续 GaussDB/DMDB 等信创替换成本。数据库建表和注释规范、schema catalog、Observability baseline 与数据库发布 runbook 已有第一版，AppHub/Ops/IAM table comment、JSON/text 注释、migrations history schema 配置和 convention tests 已作为后续持久化服务的门禁样本。后续前端功能必须沿用 Console Auth + shadcn-vue Baseline 已选 registry、preset、token 和 `@nerv-iip/ui` 导出边界。后续任务继续参考 docs/architecture/third-vertical-slice-console.md、docs/architecture/fourth-vertical-slice-real-infra.md、docs/architecture/frontend-design-system-planning.md、docs/architecture/database-schema-conventions.md、docs/architecture/database-schema-catalog.md、docs/architecture/database-release-runbook.md、docs/architecture/script-automation-governance.md、docs/architecture/observability-baseline.md、docs/adr/0009-database-migration-release-and-seed-strategy.md、docs/adr/0010-automation-script-trusted-execution-governance.md、docs/superpowers/specs/2026-05-17-release-grade-persistence-foundation-design.md、docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md、docs/superpowers/specs/2026-05-17-schema-governance-migration-hardening-design.md、docs/superpowers/plans/2026-05-17-schema-governance-migration-hardening.md、docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md 与 docs/architecture/deployment-baseline.md。
