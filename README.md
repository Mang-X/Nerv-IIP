# Nerv-IIP

Nerv-IIP 是一个从 0 到 1 规划的原生 AI 应用管理平台，可面向多类行业和应用场景扩展。核心目标不是先做复杂业务系统，而是先建立一个稳定的控制面与应用管理底座，使平台能够统一管理身份权限、文件存储与对外受控访问能力，并接入、发现、观测、控制和治理真实运行中的应用实例。

当前仓库以文档优先方式启动，并已经从首批架构冻结推进到平台控制面、业务平台 MVP、BusinessGateway/BusinessConsole、事件可靠性、生产安全和生产部署产物基线。当前事实源以 [docs/architecture/implementation-readiness.md](docs/architecture/implementation-readiness.md) 为准；README 只保留入口、导航和关键约束，避免和 readiness 形成第二套状态清单。

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
- 当前能力基线：已完成 IAM Persistent Auth Foundation、Gateway-wide permission enforcement、Console Auth + shadcn-vue 基线、Phase 8 IAM Admin Console、IAM 企业身份入口、FileStorage MVP、本地 tus hardening、业务平台 Wave 1/2/2.5/3、BusinessGateway/BusinessConsole MVP、事件可靠性基线、ExternalClient client_credentials、生产安全硬化、生产部署产物、opt-in 发布演练和性能阈值化基线。
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

该命令通过 `scripts/dev.ps1` 启动平台级 Aspire AppHost。Aspire 是完整本地拓扑入口，会编排 PlatformGateway、AppHub、IAM、Ops、FileStorage、Notification、Business 服务、Connector Host、Console 和本地依赖服务。
首次启动前建议按 [infra/aspire/README.md](infra/aspire/README.md) 配置本机 Aspire secret parameters；如果缺少参数，Aspire 会在启动时提示输入。

只需要启动 PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector 等依赖服务时，使用：

```powershell
.\nerv.ps1 dev -InfraOnly
```

查看本地端口矩阵：

```powershell
.\nerv.ps1 ports
```

平台 HTTP 服务固定在 `5100-5125`：PlatformGateway `5100`、AppHub `5101`、IAM `5102`、Ops `5103`、FileStorage `5104`、Console `5105`、Notification `5106`、BusinessMasterData `5107`、BusinessProductEngineering `5108`、BusinessInventory `5109`、BusinessQuality `5110`、BusinessMES `5111`、BusinessDemandPlanning `5112`、BusinessBarcodeLabel `5113`、BusinessApproval `5114`、BusinessWMS `5115`、BusinessIndustrialTelemetry `5116`、BusinessMaintenance `5117`、BusinessERP `5118`、BusinessGateway `5119`、BusinessConsole `5125`。完整端口矩阵以 `.\nerv.ps1 ports` 输出为准。

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
7. docs/superpowers/specs/2026-05-23-business-wave-3-agent-session-design.md
8. docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md
9. docs/superpowers/specs/2026-05-23-business-wave-2-5-equipment-reliability-closure.md
10. docs/superpowers/specs/2026-05-23-business-full-chain-acceptance-design.md

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
20. docs/superpowers/plans/2026-05-23-erp-procurement-mvp.md
21. docs/superpowers/plans/2026-05-23-erp-sales-mvp.md
22. docs/superpowers/plans/2026-05-23-erp-finance-mvp.md
23. docs/superpowers/plans/2026-05-23-business-wave-3-erp-registration-verify-readiness.md
24. docs/superpowers/plans/2026-05-23-business-full-chain-acceptance.md
25. docs/superpowers/plans/2026-05-26-p2-deployment-performance-hardening.md

其中 `2026-05-20-business-erp-procurement-sales-finance-mvp.md` 和 `2026-05-20-business-full-chain-acceptance.md` 仅作为历史输入；当前 ERP 和全链路验收以后续 2026-05-23 的规格和计划为准。

## 里程碑

1. M0：完成 ADR、单仓结构、前后端基础工程与文档冻结。
2. M1：完成 IAM、组织环境上下文、文件存储基线、控制台基础登录能力和外部应用授权基线。
3. M2：完成 Connector Host 接入、应用注册、心跳与状态同步。
4. M3：完成启停、重启、日志与审计闭环。
5. M4：完成 MCP 查询工具、知识入库与低风险执行能力。
6. M5：完成平台 MVP 首发与真实演练。

## 当前状态

当前仓库已经在首批架构文档基础上落地平台控制面、业务服务、BusinessGateway/BusinessConsole、FileStorage、本地部署产物和脚本治理基线；关键设计已经沉淀为 ADR 与 architecture 文档。平台 HTTP 接口统一采用 FastEndpoints；Program.cs 只负责服务注册、中间件和 `UseFastEndpoints()` 接线，具体接口放在各 Web 项目的 `Endpoints/` 目录。

控制面已经覆盖 Connector Host 注册/心跳/状态、低风险 Ops restart 闭环、Gateway OpenAPI/api-client、IAM 持久化登录、Gateway-wide permission enforcement、Console Auth、IAM Admin Console、OIDC callback/MFA hook/SSO session binding、Notification 站内消息/任务纵切、ExternalClient client_credentials、resource-scope ABAC grant 和生产安全硬化。部署侧已有平台级 Aspire AppHost、Compose dependencies/platform overlay、统一 Dockerfile、生产 env 样例、AppHost release-install 启动脚本、zip package 生成脚本、生产部署产物验证脚本和 opt-in 发布演练脚本。

业务平台已经完成 Wave 1、Wave 2、Equipment Reliability、Wave 3 ERP 和 Business Console MVP：BusinessMasterData、ProductEngineering、Inventory、Quality、MES、DemandPlanning、BarcodeLabel、BusinessApproval、WMS、IndustrialTelemetry、Maintenance 和 ERP 均已有服务代码、AppHost 注册、schema catalog、IAM seed 和对应 verify 脚本；BusinessGateway 提供 `/api/business-console/v1/**` facade，BusinessConsole 已交付 #166 到 #169 的首批页面。

FileStorage MVP 已提供公开 contracts、`Sdk.FileStorage`、metadata API、PostgreSQL-backed API service、`filestorage` schema 和本地 tus `HEAD`/`PATCH` 上传与 download content endpoint。本地 tus size/checksum/expiration hardening 已完成；MinIO/S3 multipart 仍是 post-MVP 对象存储部署联调项。

#77 P0 收口已完成：`scripts/verify-business-full-chain-acceptance.ps1` 默认覆盖 WMS service-local live HTTP TestServer 验收、MES/ERP public-surface 支撑测试和七条链路 acceptance evidence。当前默认 profile 不启动 Docker、PostgreSQL、RabbitMQ 或外部 WCS 设备；后续真实基础设施联调应作为 hardening 扩展，而不是阻塞业务推进。

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

1. 推进 #142 FileStorage MinIO/S3 multipart provider，补齐短期上传指令、complete 校验、MinIO profile 验证、SDK/OpenAPI 文档和部署配置。
2. 以已有 Mobile PDA PRD/architecture 为输入，拆出 BusinessGateway `/api/mobile/v1/**` facade、Capacitor app、scanner bridge、offline outbox/sync 和 WMS PDA MVP issue。
3. 深化 #170/#171 之后的事件可靠性：跨进程多服务 CAP 联调、持久 inbox、DLQ 自动 replay executor 和管理入口。
4. 在 #173/#174 基础上补 Windows Service/systemd 注册器、客户现场备份恢复演练、生产日志长期查询后端和发布 runbook 证据；当前发布演练入口已覆盖 disposable Compose dependencies/platform smoke。
5. 继续推进完整 OAuth/OIDC 授权码服务器、WebAuthn/企业 IdP 深化、复杂 ABAC 策略语言、高风险动作审批 Console 入口和通知外部通道；#77 的 P0 验收脚本继续作为业务链路回归入口。

## 非目标

1. 首期不做特定行业协议接入。
2. 首期不做完整行业业务套件。
3. 首期不做模型托管平台。
4. 首期不做微前端与复杂前端运行时抽象。
