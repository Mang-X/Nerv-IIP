# Nerv-IIP

Nerv-IIP 是一个从 0 到 1 规划的原生 AI 应用管理平台，可面向多类行业和应用场景扩展。核心目标不是先做复杂业务系统，而是先建立一个稳定的控制面与应用管理底座，使平台能够统一管理身份权限、文件存储与对外受控访问能力，并接入、发现、观测、控制和治理真实运行中的应用实例。

当前仓库以文档优先方式启动，并已经从首批架构冻结推进到第一、第二、第三阶段纵切实现、第四阶段真实基础设施门禁、第五阶段迁移发布底座、第六阶段 schema governance hardening、第七阶段 IAM 持久化认证底座和 Phase 8 IAM Admin Console：第一阶段验证 Connector Host 接入、AppHub 状态沉淀与 Gateway 查询，第二阶段验证低风险运维动作创建、派发、执行、结果回传与审计记录，第三阶段验证 Gateway OpenAPI、类型安全前端 API client 与 Vue 控制台工作区，第四阶段把 AppHub/Ops 迁移到 netcorepal/CleanDDD、PostgreSQL profile、结构化日志和平台级 Aspire AppHost，第五阶段把 AppHub/Ops 的 PostgreSQL 路径从 `EnsureCreated()` 推进到 migration-based 验证，第六阶段固化 AppHub/Ops schema metadata 和 convention tests，第七阶段为 IAM 增加 PostgreSQL profile 下的持久化登录、refresh rotation、session revoke 和 Connector Host credential validation 基线；Phase 8 已交付 Calm Control Plane 蓝色 Design System baseline、Console IAM Admin facade、用户/角色/权限 catalog/会话管理页面，并让 Gateway Console 接口把 bearer token 和 permission/context 转发给 IAM internal authorization check。

## 项目目标

1. 建立统一的平台控制面，覆盖身份、权限、组织、环境、对外授权、文件存储、应用目录、实例状态、运维动作、审计闭环与通知能力。
2. 建立标准化应用接入模型，使 Docker、Windows Service、HTTP 服务等不同宿主环境都可以被平台纳管。
3. 建立受治理的 AI 能力边界，优先交付 MCP 查询工具、知识检索和低风险执行能力。
4. 在不托管模型的前提下，采用官方 .NET AI 生态完成模型接入、知识处理与工具治理。
5. 通过一个独立于主平台发布边界的示例应用验证平台能力，而不是在首期堆叠复杂业务功能。

## 核心原则

1. 平台底座优先，业务域后置。
2. 逻辑边界先冻结，物理部署保留弹性。
3. 前端采用显式 Vue 结构，不引入伪 Nuxt runtime。
4. 后端按服务边界组织，禁止以共享库名义回退到大单体。
5. 应用接入统一走 Connector Host 与 Connector 模式。
6. AI 能力先做治理、查询和低风险动作，不先做模型托管和复杂自治代理。
7. 主平台不内置工厂、产线、设备等行业组织模型，行业语义通过后续领域扩展承载。
8. 主平台通过模块化 Platform SDK 向应用、Connector Host 和扩展模块提供契约、认证、授权上下文、文件存储、运维调用、通知意图和观测上下文等客户端能力；SDK 不成为新的运行时中心，外部演进单元不直接依赖主平台内部实现。
9. 主平台提供通用 File Storage 能力，负责文件元数据、授权、上传下载会话与对象存储治理；业务服务只通过 fileId 或文件引用表达业务含义。
10. 主平台提供通用 Notification 能力，负责站内通知、待办入口、接收人解析、偏好、去重、投递状态和通道适配边界；业务服务只表达已发生事实或通知意图，不各自直连短信、邮件、企业 IM 或 Webhook。
11. 主平台与应用、Connector Host、行业扩展采用主版本对齐策略：同一主版本内小版本可以滞后并保持兼容，破坏性变更必须提升主版本。
12. 文档、契约和目录职责优先稳定，以降低团队协作和 AI 协作成本。
13. 部署采用“多部署目标，单一部署模型”：Aspire 作为统一编排模型和开发联调入口，Docker Compose、安装包和整合安装脚本作为面向不同环境的交付目标。
14. 自动化脚本必须作为可信工程资产治理：分类、副作用、超时、日志、进程清理、敏感信息脱敏和静态门禁都要可追踪。

## 仓库与文档入口

- 代码仓库：[Mang-X/Nerv-IIP](https://github.com/Mang-X/Nerv-IIP)
- 当前能力基线：已完成 IAM Persistent Auth Foundation、Gateway-wide permission enforcement、pnpm 11.1.2 基线、Console Auth + shadcn-vue 基线、Phase 8 蓝色 Design System baseline、IAM Admin Console workflow 和统一本地开发启动入口。
- 架构总览：[docs/architecture/context-map.md](docs/architecture/context-map.md)
- 业务平台领域架构：[docs/architecture/business-platform-domain-architecture.md](docs/architecture/business-platform-domain-architecture.md)
- BusinessMasterData 治理 ADR：[docs/adr/0013-business-master-data-governance.md](docs/adr/0013-business-master-data-governance.md)
- BusinessMasterData 字段矩阵：[docs/architecture/business-master-data-field-matrix.md](docs/architecture/business-master-data-field-matrix.md)
- 流程型制造主数据补充：[docs/architecture/business-master-data-process-manufacturing-supplement.md](docs/architecture/business-master-data-process-manufacturing-supplement.md)
- 业务平台完整规格：[docs/superpowers/specs/2026-05-20-business-platform-domain-design.md](docs/superpowers/specs/2026-05-20-business-platform-domain-design.md)
- 业务平台实施计划入口：[docs/superpowers/plans/2026-05-20-business-main-platform-integration-readiness.md](docs/superpowers/plans/2026-05-20-business-main-platform-integration-readiness.md)
- BusinessMasterData realignment 计划：[docs/superpowers/plans/2026-05-21-business-master-data-realignment.md](docs/superpowers/plans/2026-05-21-business-master-data-realignment.md)
- 移动端 PDA Capacitor PRD：[docs/superpowers/specs/2026-05-21-mobile-pda-capacitor-prd.md](docs/superpowers/specs/2026-05-21-mobile-pda-capacitor-prd.md)
- 移动端 PDA Capacitor 架构：[docs/architecture/mobile-pda-capacitor-architecture.md](docs/architecture/mobile-pda-capacitor-architecture.md)
- 仓库结构：[docs/architecture/repo-layout.md](docs/architecture/repo-layout.md)
- 实施状态：[docs/architecture/implementation-readiness.md](docs/architecture/implementation-readiness.md)
- 前端结构：[docs/architecture/frontend-structure.md](docs/architecture/frontend-structure.md)
- 前端 Design System 规划：[docs/architecture/frontend-design-system-planning.md](docs/architecture/frontend-design-system-planning.md)
- API 契约与生成：[docs/architecture/api-contract-and-codegen.md](docs/architecture/api-contract-and-codegen.md)
- 数据库 Schema 规范：[docs/architecture/database-schema-conventions.md](docs/architecture/database-schema-conventions.md)
- 数据库 Schema Catalog：[docs/architecture/database-schema-catalog.md](docs/architecture/database-schema-catalog.md)
- 数据库发布 Runbook：[docs/architecture/database-release-runbook.md](docs/architecture/database-release-runbook.md)
- 脚本自动化治理：[docs/architecture/script-automation-governance.md](docs/architecture/script-automation-governance.md)
- Observability 基线：[docs/architecture/observability-baseline.md](docs/architecture/observability-baseline.md)
- 第三阶段控制台纵切：[docs/architecture/third-vertical-slice-console.md](docs/architecture/third-vertical-slice-console.md)
- 第四阶段真实基础设施纵切：[docs/architecture/fourth-vertical-slice-real-infra.md](docs/architecture/fourth-vertical-slice-real-infra.md)
- 技术栈参考链接：[docs/architecture/technology-stack-references.md](docs/architecture/technology-stack-references.md)
- Connector Host 机器身份认证终态：[docs/architecture/connector-host-machine-auth.md](docs/architecture/connector-host-machine-auth.md)
- 第五阶段计划：[docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md](docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md)
- 第六阶段计划：[docs/superpowers/plans/2026-05-17-schema-governance-migration-hardening.md](docs/superpowers/plans/2026-05-17-schema-governance-migration-hardening.md)
- 第七阶段计划：[docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md](docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md)

## 技术基线

### 已落地工程基线

### 前端

- Node.js >= 22.18.0；仓库根 `.node-version` 保留 22.22.3 作为保守复现基线，当前工具链也允许使用更新的 Current 版本（如 Node.js 26）做本地开发验证。
- pnpm 11.1.2 workspace。
- Vite+ 0.1.21 作为工作区统一入口，负责 check、fmt、lint、test、run 与 workspace task 编排。
- Vite 8.0.13 / Vitest 4.1.6 通过 Vite+ 的 `@voidzero-dev/vite-plus-core` 与 `@voidzero-dev/vite-plus-test` override 接入。
- Vue 3.5.34、Vue Router 5.0.7 官方文件路由插件、Pinia 3.0.4、Pinia Colada 1.3.0、Pinia Colada Auto Refetch 0.2.6。
- Hey API OpenAPI TypeScript 0.97.1，生成 fetch client、TypeScript DTO、SDK 和 Pinia Colada query/mutation options。
- shadcn-vue 2.7.3，使用 `reka-nova` style、Tailwind CSS v4、Calm Control Plane 蓝色 semantic tokens 和 `@nerv-iip/ui` 稳定导出边界。

### 后端

- .NET 10 SDK
- netcorepal-cloud-framework
- Aspire AppHost
- FastEndpoints
- ASP.NET Core Authentication/Authorization
- OpenTelemetry
- PostgreSQL (primary persistence profile)
- GaussDB / DMDB (信创替换候选 profile，需按环境验证)
- Redis
- FusionCache
- RabbitMQ
- MinIO
- Qdrant

### 已冻结但按需引入的前端候选

- VueUse
- es-toolkit

### AI

- Microsoft.Extensions.AI
- Microsoft.Extensions.DataIngestion
- Microsoft.Extensions.VectorData
- 复杂 AI 自主工作流框架仅在确有 autonomous workflow 需求时再评估引入

## 日常开发启动

主平台本地联调入口是仓库根目录的轻量 CLI：

```powershell
.\nerv.ps1 dev
```

该命令通过 `scripts/dev.ps1` 启动平台级 Aspire AppHost。Aspire 是完整本地拓扑入口，会编排 PlatformGateway、AppHub、IAM、Ops、FileStorage、Connector Host、Console 和本地依赖服务。

只需要启动 PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector 等依赖服务时，使用：

```powershell
.\nerv.ps1 dev -InfraOnly
```

查看本地端口矩阵：

```powershell
.\nerv.ps1 ports
```

平台 HTTP 服务固定为 `5100-5105`：Gateway `5100`、AppHub `5101`、IAM `5102`、Ops `5103`、FileStorage `5104`、Console `5105`。Console 避开 Vite 默认 `5173`，降低与其他前端项目冲突的概率。

## 仓库规划

```text
Nerv-IIP/
  README.md
  docs/
    adr/
    architecture/
  frontend/
    apps/
    packages/
  backend/
    services/
    gateway/
    common/
    tests/
  connector-hosts/
  infra/
  scripts/
```

## 文档导航

建议先按“ADR -> 架构说明 -> 实施说明”的顺序阅读。ADR 解释为什么这样取舍，架构说明定义边界和术语，实施说明负责把首批工程落到可执行粒度。

### ADR

1. docs/adr/0001-backend-solution-and-service-boundaries.md
2. docs/adr/0002-connector-host-and-app-integration-contract.md
3. docs/adr/0003-data-and-messaging-baseline.md
4. docs/adr/0004-ai-integration-boundary-and-governance.md
5. docs/adr/0005-knowledge-ingestion-and-retrieval.md
6. docs/adr/0006-frontend-workspace-structure.md
7. docs/adr/0007-vue-router-file-routing-colocation.md
8. docs/adr/0008-multi-target-deployment-and-aspire-apphost.md
9. docs/adr/0009-database-migration-release-and-seed-strategy.md
10. docs/adr/0010-automation-script-trusted-execution-governance.md
11. docs/adr/0011-integration-event-contract-baseline.md
12. docs/adr/0012-business-platform-domain-layering.md
13. docs/adr/0013-business-master-data-governance.md

### 架构说明

1. docs/architecture/repo-layout.md
2. docs/architecture/context-map.md
3. docs/architecture/business-platform-domain-architecture.md
4. docs/architecture/glossary.md
5. docs/architecture/caching-baseline.md
6. docs/architecture/iam-authentication-baseline.md
7. docs/architecture/platform-sdk-baseline.md
8. docs/architecture/file-storage-baseline.md
9. docs/architecture/notification-baseline.md
10. docs/architecture/backend-cleanddd-netcorepal-guidelines.md
11. docs/architecture/core-domain-model-v1.md
12. docs/architecture/connector-platform-protocol-v1.md
13. docs/architecture/first-vertical-slice.md
14. docs/architecture/second-vertical-slice-ops.md
15. docs/architecture/third-vertical-slice-console.md
16. docs/architecture/frontend-structure.md
17. docs/architecture/api-contract-and-codegen.md
18. docs/architecture/ai-boundaries.md
19. docs/architecture/knowledge-source-lifecycle.md
20. docs/architecture/backend-bootstrap-plan.md
21. docs/architecture/implementation-readiness.md
22. docs/architecture/deployment-baseline.md
23. docs/architecture/technology-stack-references.md
24. docs/architecture/fourth-vertical-slice-real-infra.md
25. docs/architecture/frontend-design-system-planning.md
26. docs/architecture/database-schema-conventions.md
27. docs/architecture/database-schema-catalog.md
28. docs/architecture/database-release-runbook.md
29. docs/architecture/script-automation-governance.md
30. docs/architecture/observability-baseline.md
31. docs/architecture/connector-host-machine-auth.md
32. docs/architecture/business-master-data-field-matrix.md
33. docs/architecture/business-master-data-process-manufacturing-supplement.md
34. docs/architecture/mobile-pda-capacitor-architecture.md

### 规格设计

1. docs/superpowers/specs/2026-05-17-iam-persistent-auth-foundation-design.md
2. docs/superpowers/specs/2026-05-17-release-grade-persistence-foundation-design.md
3. docs/superpowers/specs/2026-05-17-schema-governance-migration-hardening-design.md
4. docs/superpowers/specs/2026-05-18-console-auth-shadcn-design.md
5. docs/superpowers/specs/2026-05-20-business-platform-domain-design.md
6. docs/superpowers/specs/2026-05-21-mobile-pda-capacitor-prd.md

### 实施计划

1. docs/superpowers/plans/2026-05-14-first-vertical-slice.md
2. docs/superpowers/plans/2026-05-15-second-vertical-slice-low-risk-ops.md
3. docs/superpowers/plans/2026-05-16-third-vertical-slice-console.md
4. docs/superpowers/plans/2026-05-17-fourth-vertical-slice-real-infra-foundation.md
5. docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md
6. docs/superpowers/plans/2026-05-17-schema-governance-migration-hardening.md
7. docs/superpowers/plans/2026-05-17-iam-persistent-auth-foundation.md
8. docs/superpowers/plans/2026-05-20-business-main-platform-integration-readiness.md
9. docs/superpowers/plans/2026-05-20-business-master-data-foundation.md
10. docs/superpowers/plans/2026-05-21-business-master-data-realignment.md
11. docs/superpowers/plans/2026-05-20-business-product-engineering-mvp.md
12. docs/superpowers/plans/2026-05-20-business-common-capability-foundation.md
13. docs/superpowers/plans/2026-05-20-business-demand-planning-mvp.md
14. docs/superpowers/plans/2026-05-20-business-erp-procurement-sales-finance-mvp.md
15. docs/superpowers/plans/2026-05-20-business-wms-execution-mvp.md
16. docs/superpowers/plans/2026-05-20-business-mes-execution-mvp.md
17. docs/superpowers/plans/2026-05-20-business-industrial-telemetry-mvp.md
18. docs/superpowers/plans/2026-05-20-business-maintenance-mvp.md
19. docs/superpowers/plans/2026-05-20-business-full-chain-acceptance.md

## 里程碑

1. M0：完成 ADR、单仓结构、前后端基础工程与文档冻结。
2. M1：完成 IAM、组织环境上下文、文件存储基线、控制台基础登录能力和外部应用授权基线。
3. M2：完成 Connector Host 接入、应用注册、心跳与状态同步。
4. M3：完成启停、重启、日志与审计闭环。
5. M4：完成 MCP 查询工具、知识入库与低风险执行能力。
6. M5：完成平台 MVP 首发与真实演练。

## 当前状态

当前仓库已经在首批架构文档基础上落地第一、第二、第三阶段纵切，并完成第四阶段真实基础设施底座门禁；关键设计已经沉淀为 ADR 与架构文档。平台 HTTP 接口统一采用 FastEndpoints；Program.cs 只负责服务注册、中间件和 `UseFastEndpoints()` 接线，具体接口放在各 Web 项目的 `Endpoints/` 目录。

第一阶段可以用 `scripts/verify-first-slice.ps1` 完成本地纵切验证：backend 与 connector-hosts 两套 solution 可 restore/build/test，AppHub 可接收注册、心跳和状态快照，PlatformGateway 可通过 AppHub 查询实例列表与详情，Connector Host 可通过 Platform SDK 上报一个 Docker Connector 发现的目标。

第二阶段可以用 `scripts/verify-second-slice-ops.ps1` 完成本地低风险动作闭环验证：Gateway 创建实例 restart 运维任务，Ops 记录任务、尝试和审计事实，Connector Host 通过 Ops SDK 拉取 pending task，Docker Connector 执行动作并回传结果，Gateway 可查询任务详情。当前状态适合工程联调、接口走查和后续功能开发；尚不是面向真实用户的可部署产品。

第三阶段控制台纵切可以用 `scripts/verify-third-slice-console.ps1` 验证：Gateway 暴露稳定 OpenAPI，frontend 工作区可生成类型安全 api-client，console 可展示实例列表与详情、创建 restart 任务并查看 OperationTask 状态。

第四阶段真实基础设施底座入口已经补齐并通过 `scripts/verify-fourth-slice-real-infra.ps1`：脚本会拉起 PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector，验证 PostgreSQL 持久化 profile，并用 `-UsePostgres` 复跑第三阶段控制台纵切。PostgreSQL 本机默认映射到 `15432`，AppHub/Ops 使用独立数据库连接以避免共享库下早期建表路径漏建服务表。平台级 Aspire AppHost 已落到 `infra/aspire/Nerv.IIP.AppHost`，当前 `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore` 和第四阶段完整验证均已通过。

第五阶段 Release-grade Persistence Foundation 已落地并通过 `scripts/verify-fifth-slice-persistence-foundation.ps1`：AppHub/Ops 已有初始 EF Core migrations，PostgreSQL profile tests 通过 `Database.MigrateAsync()` 创建 schema，Web startup 只有在 `Persistence:AutoMigrate=true` 时才执行本地/dev auto-migration。数据库 schema 规范、schema catalog、Observability baseline 和数据库发布 runbook 已补第一版，但完整 PoC/私有化发布仍需要发布脚本、备份恢复演练、seed 清单和诊断输出契约。第五阶段曾暂缓前端功能实施；当前 Console Auth + shadcn-vue Baseline 已完成后，后续控制台页面、视觉组件和组件库迁移必须沿用已选 Design System 边界。

第六阶段 Schema Governance & Migration Hardening 已完成 AppHub/Ops 的表注释、JSON 兼容注释、migrations history schema 和 schema convention tests 门禁固化；IAM 已在第七阶段沿用同一 helper 与 catalog 口径，FileStorage 也已沿用到 `filestorage` schema、初始 migration 和 schema convention tests。

第七阶段 IAM Persistent Auth Foundation 已落地：IAM 在保留 InMemory profile 的同时新增 PostgreSQL iam schema、EF migrations、schema convention tests、idempotent seed、JWT access token、refresh token rotation、session revoke 和 Connector Host credential validation 的持久化后端基线。现有 PlatformGateway Console API 已接入 IAM-backed permission enforcement，覆盖实例列表、实例详情、restart 运维任务创建和 operation task detail 查询；Gateway 不直接引用 IAM Domain 或 Infrastructure。Console Auth + shadcn-vue Baseline 已补齐最小登录 UI、会话恢复、Gateway bearer 注入、路由守卫、退出登录和 shadcn-vue UI 基线。

Phase 8 IAM Admin Console 已落地：Console Design System 采用 Calm Control Plane 蓝色基线，IAM admin 用户、角色、权限 catalog 和会话页面位于 `/iam/users`、`/iam/roles`、`/iam/sessions`，通过 `frontend/apps/console/src/composables/useIamAdmin.ts` 消费 generated Gateway api-client。PlatformGateway 暴露 11 个 Console IAM operation IDs，并在转发 IAM 管理请求前执行 IAM-backed permission enforcement。

FileStorage MVP 已继续推进：服务提供上传会话创建、完成、文件元数据读取和下载授权 API，公开 contracts 与 `Sdk.FileStorage` HTTP client 已落地；默认仍使用 in-memory/server-proxy 本地路径，设置 `Persistence:Provider=PostgreSQL` 后可启用 PostgreSQL-backed API service，设置 `FileStorage:UploadProvider=tus` 后返回并支持 tus 本地上传入口。当前 tus MVP 支持 `HEAD /api/files/v1/tus/{uploadSessionId}` 查询 offset、`PATCH /api/files/v1/tus/{uploadSessionId}` 追加字节、暂停后按 offset 续传，以及通过 download grant content endpoint 下载已上传字节；字节先落本地 `FileStorage:Tus:RootPath`，MinIO/S3 multipart 不进入 MVP，放到后续对象存储部署联调。

脚本自动化治理已冻结到 ADR 0010 和 docs/architecture/script-automation-governance.md。现有验证脚本会按迁移清单逐步接入共享 helper 和静态门禁；新增或修改脚本必须声明分类、副作用、写入路径、清理策略和诊断输出。

第三阶段前端质量门禁：

```powershell
pnpm -C frontend check
pnpm -C frontend lint
pnpm -C frontend fmt
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

下一阶段重点：

1. 在 IAM 持久化登录、Gateway 权限门禁、Console Auth 和 IAM Admin Console 基线之上演进 OAuth/OIDC、SSO、MFA、ABAC 和外部应用授权。
2. 补齐 PostgreSQL profile 的备份、恢复、seed、初始化脚本和后续安装包迁移入口。
3. 继续硬化 FileStorage 本地 tus endpoint，补齐 size/checksum 强校验、过期清理和更完整协议兼容；MinIO/S3 multipart 继续留到 post-MVP 部署联调。
4. 扩展 Ops 的审批、权限、通知和持久化 outbox，逐步覆盖更高风险动作。
5. 基于 Console Auth + shadcn-vue Baseline 和 Phase 8 Calm Control Plane 蓝色基线继续收敛前端 Design System，后续控制台页面、视觉组件和组件库迁移必须沿用已选 registry、preset、semantic tokens 和导出边界。
6. 以平台级 Aspire AppHost 为统一拓扑入口，继续衍生 Docker Compose、安装包和 Windows/Linux 整合安装脚本。
7. 将现有 `verify`/`generate` 脚本迁移到脚本治理 helper 与 fast gate，先收敛 IAM、第五阶段和第四阶段真实基础设施验证入口。
8. BusinessMasterData realignment 已开始落地，已覆盖核心主数据 create/list/resolve 合同、稳定 operationId、权限码和重复键测试；继续完善下游 ProductEngineering Recipe/Formula 计划和业务前端入口前，应先通过 `scripts/verify-business-master-data-realignment.ps1` 保持主数据基石稳定。

## 非目标

1. 首期不做特定行业协议接入。
2. 首期不做完整行业业务套件。
3. 首期不做模型托管平台。
4. 首期不做微前端与复杂前端运行时抽象。
