# Nerv-IIP

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/Mang-X/Nerv-IIP)

Nerv-IIP 是一个以数字工厂业务平台为当前主线、以通用平台控制面为底座的工业应用平台。当前仓库已经不再只是验证平台接入样例，而是在同一治理框架下持续推进主数据、产品工程、计划、库存、质量、MES、WMS、ERP、设备运行、维护、审批、条码、APS lite、BusinessGateway、Business Console 和 Business PDA。

当前仓库以文档优先方式启动，并已经从首批架构冻结推进到平台控制面、业务平台 MVP、BusinessGateway/BusinessConsole、事件可靠性、生产安全和生产部署产物基线。当前事实源以 [docs/architecture/implementation-readiness.md](docs/architecture/implementation-readiness.md) 为准；README 只保留入口、导航和关键约束，避免和 readiness 形成第二套状态清单。

## 项目目标

1. 建立统一的平台控制面，覆盖身份、权限、组织、环境、对外授权、文件存储、应用目录、实例状态、运维动作、审计闭环与通知能力。
2. 建立数字工厂业务平台主干，覆盖从主数据、工程资料、需求/MRP、采购、库存/WMS、MES 执行、质量、设备、维护到财务应收应付和 APS lite 的 P0 闭环。
3. 建立标准化业务前端入口：BusinessGateway 作为业务 BFF，Business Console 承载 PC 业务工作台，Business PDA 承载一线移动作业。
4. 建立标准化应用接入模型，使 Docker、Windows Service、HTTP 服务等不同宿主环境都可以被平台纳管。
5. 建立受治理的 AI 能力边界，优先交付 MCP 查询工具、知识检索和低风险执行能力；复杂 AI 自主流程只在有明确业务闭环后进入。

## 核心原则

1. 平台底座已经成型，业务平台是当前主线；平台能力继续作为业务服务的治理、权限、契约、部署和观测底座。
2. 逻辑边界先冻结，物理部署保留弹性。
3. 前端采用显式 Vue 结构，不引入伪 Nuxt runtime。
4. 后端按服务边界组织，禁止以共享库名义回退到大单体。
5. 应用接入统一走 Connector Host 与 Connector 模式。
6. AI 能力先做治理、查询和低风险动作，不先做模型托管和复杂自治代理。
7. 主平台控制面不内置工厂、产线、设备等行业组织模型；这些语义由业务平台服务、BusinessGateway 和可插拔领域扩展承载。
8. 主平台通过模块化 Platform SDK 向应用、Connector Host 和扩展模块提供契约、认证、授权上下文、文件存储、运维调用、通知意图和观测上下文等客户端能力；SDK 不成为新的运行时中心，外部演进单元不直接依赖主平台内部实现。
9. 主平台提供通用 File Storage 能力，负责文件元数据、授权、上传下载会话与对象存储治理；业务服务只通过 fileId 或文件引用表达业务含义。
10. 主平台提供通用 Notification 能力，负责站内通知、待办入口、接收人解析、偏好、去重、投递状态和通道适配边界；业务服务只表达已发生事实或通知意图，不各自直连短信、邮件、企业 IM 或 Webhook。
11. 主平台与应用、Connector Host、行业扩展采用主版本对齐策略：同一主版本内小版本可以滞后并保持兼容，破坏性变更必须提升主版本。
12. 文档、契约和目录职责优先稳定，以降低团队协作和 AI 协作成本。
13. 部署采用“多部署目标，单一部署模型”：Aspire 作为统一编排模型和开发联调入口，Docker Compose、安装包和整合安装脚本作为面向不同环境的交付目标。
14. 自动化脚本必须作为可信工程资产治理：分类、副作用、超时、日志、进程清理、敏感信息脱敏和静态门禁都要可追踪。

## 仓库与文档入口

- 代码仓库：[Mang-X/Nerv-IIP](https://github.com/Mang-X/Nerv-IIP)
- 当前能力基线：业务平台主干已进入持续深化阶段，已覆盖 MasterData、ProductEngineering、DemandPlanning、Inventory、Quality、MES、WMS、ERP、IndustrialTelemetry、Maintenance、BarcodeLabel、BusinessApproval、BusinessScheduling / APS lite、BusinessGateway、Business Console 和 Business PDA v1；平台底座已覆盖 IAM、Gateway-wide permission enforcement、Console Auth、IAM Admin、FileStorage、本地 tus hardening、Notification、ExternalClient、事件可靠性、生产安全和生产部署产物。
- 架构总览：[docs/architecture/context-map.md](docs/architecture/context-map.md)
- 业务平台领域架构：[docs/architecture/business-platform-domain-architecture.md](docs/architecture/business-platform-domain-architecture.md)
- 业务模块交付清单：[docs/architecture/business-module-delivery-checklist.md](docs/architecture/business-module-delivery-checklist.md)
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
.\nerv.ps1 bootstrap
```

在有网的空白 Windows 开发机器上，可以先执行：

```powershell
.\nerv.ps1 bootstrap -InstallMissing
```

该命令会检查 .NET SDK 10、Node.js、pnpm、Docker、Aspire CLI，必要时通过
`winget` 安装 Windows 侧缺失工具，初始化本地 Development AppHost
user-secrets，检查/信任本地 HTTPS 开发证书，执行 `dotnet restore`、
`pnpm install` 和 AppHost build。Docker Desktop 安装后如果 daemon 尚未运行，
需要先启动 Docker Desktop 再重跑 bootstrap。完成后日常启动使用：

如需固定本地 IAM seed 管理员密码，可在首次数据库 seed 前显式传入
`-LocalAdminPassword`；不传时 bootstrap 会生成随机 Development-only 值并写入
本机 user-secrets，不会把固定密码写入仓库文件。

```powershell
.\nerv.ps1 dev
```

该命令通过 `scripts/dev.ps1` 启动平台级 Aspire AppHost。Aspire 是完整本地拓扑入口，会编排 PlatformGateway、AppHub、IAM、Ops、FileStorage、Notification、Business 服务、Connector Host、Console 和本地依赖服务。
首次启动前建议先运行 bootstrap；如需手工配置本机 Aspire secret parameters，可参考 [infra/aspire/README.md](infra/aspire/README.md)。
本地基础设施镜像版本由 AppHost 显式固定到新版 major：PostgreSQL 使用 `18`，
Redis 使用 `8`。不要改成 `latest`；PostgreSQL 18+ 的官方 Docker 镜像数据目录
布局与旧 `nerv-iip-postgres` 持久卷不兼容，所以本地开发使用
`nerv-iip-postgres-18` 作为新的 PostgreSQL 18 卷。
当前仓库如果以 linked worktree 方式打开，`.\nerv.ps1 dev` 会自动使用 Aspire
`--isolated`，实际前后端 URL 可能不是固定端口。用
`.\nerv.ps1 describe business-console` 和 `.\nerv.ps1 describe business-gateway`
查看本次启动的真实地址。

只需要启动 PostgreSQL、Redis、RabbitMQ、MinIO 和 OpenTelemetry Collector 等依赖服务时，使用：

```powershell
.\nerv.ps1 dev -InfraOnly
```

查看本地端口矩阵：

```powershell
.\nerv.ps1 ports
```

平台与业务开发端口固定在 `5100-5126`，另有 design-system 文档站 `5180`：PlatformGateway `5100`、AppHub `5101`、IAM `5102`、Ops `5103`、FileStorage `5104`、Console `5105`、Notification `5106`、BusinessMasterData `5107`、BusinessProductEngineering `5108`、BusinessInventory `5109`、BusinessQuality `5110`、BusinessMES `5111`、BusinessDemandPlanning `5112`、BusinessBarcodeLabel `5113`、BusinessApproval `5114`、BusinessWMS `5115`、BusinessIndustrialTelemetry `5116`、BusinessMaintenance `5117`、BusinessERP `5118`、BusinessGateway `5119`、BusinessScheduling `5120`、BusinessConsole `5125`、BusinessPDA `5126`、DesignSystem `5180`。完整端口矩阵以 `.\nerv.ps1 ports` 输出为准。

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
14. docs/adr/0014-aps-and-iiot-scheduling-boundary.md
15. docs/adr/0015-gateway-http-client-resilience-strategy.md
16. docs/adr/0016-victorialogs-central-log-backend.md
17. docs/adr/0017-business-process-manager-and-compensation-strategy.md

### 架构说明

1. docs/architecture/repo-layout.md
2. docs/architecture/context-map.md
3. docs/architecture/business-platform-domain-architecture.md
4. docs/architecture/business-module-delivery-checklist.md
5. docs/architecture/glossary.md
6. docs/architecture/caching-baseline.md
7. docs/architecture/iam-authentication-baseline.md
8. docs/architecture/platform-sdk-baseline.md
9. docs/architecture/file-storage-baseline.md
10. docs/architecture/notification-baseline.md
11. docs/architecture/backend-cleanddd-netcorepal-guidelines.md
12. docs/architecture/core-domain-model-v1.md
13. docs/architecture/connector-platform-protocol-v1.md
14. docs/architecture/first-vertical-slice.md
15. docs/architecture/second-vertical-slice-ops.md
16. docs/architecture/third-vertical-slice-console.md
17. docs/architecture/frontend-structure.md
18. docs/architecture/api-contract-and-codegen.md
19. docs/architecture/ai-boundaries.md
20. docs/architecture/knowledge-source-lifecycle.md
21. docs/architecture/backend-bootstrap-plan.md
22. docs/architecture/implementation-readiness.md
23. docs/architecture/deployment-baseline.md
24. docs/architecture/technology-stack-references.md
25. docs/architecture/fourth-vertical-slice-real-infra.md
26. docs/architecture/frontend-design-system-planning.md
27. docs/architecture/database-schema-conventions.md
28. docs/architecture/database-schema-catalog.md
29. docs/architecture/database-release-runbook.md
30. docs/architecture/script-automation-governance.md
31. docs/architecture/observability-baseline.md
32. docs/architecture/connector-host-machine-auth.md
33. docs/architecture/business-master-data-field-matrix.md
34. docs/architecture/business-master-data-process-manufacturing-supplement.md
35. docs/architecture/mobile-pda-capacitor-architecture.md
36. docs/architecture/mobile-pda-module-product-design.md
37. docs/architecture/mobile-pda-deployment.md

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

已完成：

1. 平台控制面 MVP：IAM、Gateway、Console Auth、AppHub、Ops、Connector Host、Notification、FileStorage、审计和基础观测已经形成可运行纵切。
2. 业务平台服务主干：MasterData、ProductEngineering、DemandPlanning、Inventory、Quality、MES、WMS、ERP、IndustrialTelemetry、Maintenance、BarcodeLabel、BusinessApproval 和 BusinessScheduling / APS lite 均已落地服务、schema、AppHost 注册和 focused verify 入口。
3. 业务前端主干：BusinessGateway `/api/business-console/v1/**`、Business Console PC 工作台和 Business PDA v1 已成为真实业务入口。
4. 交付与治理底座：Aspire AppHost、Compose 生成入口、release-install/package 脚本、脚本治理、事件契约/DLQ 基线、生产安全和性能阈值化 gate 已存在。

当前主线：

1. 以业务链路真实可用为中心，继续深化 MES operational foundation、APS/IIoT 联动、ERP/WMS/MES/Quality/Inventory 的跨域闭环和 Business Console/PDA 工作流。
2. 对已存在的业务服务继续做代码事实、OpenAPI/api-client、前端页面、权限和验收脚本的同步收口。
3. 把平台能力作为业务主线的支撑面继续补强，而不是重新回到纯平台骨架阶段。

## 当前状态

当前仓库已经进入业务平台持续交付阶段。平台 HTTP 接口统一采用 FastEndpoints；Program.cs 只负责服务注册、中间件和 `UseFastEndpoints()` 接线，具体接口放在各 Web 项目的 `Endpoints/` 目录。业务服务按 CleanDDD 边界组织，并通过 BusinessGateway 暴露面向业务前端的 facade。

控制面已经覆盖 Connector Host 注册/心跳/状态、低风险 Ops restart 闭环、Ops 高风险动作 approval gate、Gateway OpenAPI/api-client、IAM 持久化登录、Gateway-wide permission enforcement、Console Auth、IAM Admin Console、OIDC callback/MFA hook/SSO session binding、Notification 站内消息/任务纵切、ExternalClient client_credentials、resource-scope ABAC grant 和生产安全硬化。部署侧以 Aspire AppHost 为拓扑真相源，Compose/安装包/发布演练从该模型适配，不维护第二套完整服务图。

业务平台当前覆盖：

| 能力区 | 当前事实 |
| --- | --- |
| 主数据与工程 | BusinessMasterData、ProductEngineering 已承载 SKU、伙伴、资源、工作中心、设备资产、UOM、字典、MBOM、Routing、ProductionVersion、ECO/ECN 等事实，并通过 BusinessGateway 暴露读写/读面子集。 |
| 计划与 APS | DemandPlanning/MRP 与 BusinessScheduling / APS lite 已落地；Scheduling 拥有排程问题、方案、资源负载、冲突、preview/create/list/detail/gantt/release 和 MES release handoff。 |
| 执行与仓储 | MES、WMS、Inventory、Quality 已形成工单、工序任务、报工、完工入库、库存台账、预留/分配、收发货、上架、拣货、盘点、质量检验和 NCR 处置等 P0 能力。 |
| 经营与设备 | ERP Procurement/Sales/Finance MVP、IndustrialTelemetry、Maintenance、BusinessApproval、BarcodeLabel 已落地，并接入 schema catalog、IAM seed、AppHost 和 focused verify。 |
| 业务前端 | Business Console 已接入 MasterData、ProductEngineering、Planning、Inventory、Quality、MES、ERP、WMS、Equipment/Maintenance 和 APS lite 的 route-ready 页面；Business PDA v1 已覆盖 WMS、MES 和设备轻量作业页面。 |

Business PDA v1 当前复用 `@nerv-iip/api-client` 的 business-console 稳定导出，通过 BusinessGateway 现有 `/api/business-console/v1/**` facade 工作；独立 `/api/mobile/v1/**`、mobile OpenAPI 快照、离线 outbox/sync、设备注册和扫码解释仍是后续移动专用 API 轨道。

FileStorage 已提供公开 contracts、`Sdk.FileStorage`、metadata API、PostgreSQL-backed API service、`filestorage` schema、本地 tus `HEAD`/`PATCH` 上传与 download content endpoint；MinIO/S3 multipart 仍是 post-MVP 对象存储部署联调项。

#77 P0 收口已完成：`scripts/verify-business-full-chain-acceptance.ps1` 覆盖 WMS public-surface/event contract、MES/ERP 支撑 surface 和七条链路 acceptance evidence。真实 PostgreSQL/RabbitMQ/外部设备联调属于后续 hardening，不是当前业务推进的前置阻塞。

常用验证入口：

```powershell
pwsh scripts/verify-business-full-chain-acceptance.ps1
pwsh scripts/verify-business-scheduling-aps-lite.ps1
pwsh scripts/verify-business-iiot-runtime-facts-aps-mes.ps1
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
pnpm -C frontend --filter @nerv-iip/business-pda typecheck
pnpm -C frontend --filter @nerv-iip/business-pda test
pnpm -C frontend --filter @nerv-iip/business-pda build
```

## 当前业务推进重点

1. 继续收口 MES operational foundation：工单释放、执行生命周期、齐套/物料、质量设备 readiness、班次/追溯和 PC/PDA 一线流程必须与真实上游事实对齐。
2. 深化 APS/IIoT/MES 联动：BusinessScheduling 继续消费库存、质量、设备和维护窗口变化，前端甘特/资源负载只消费 Scheduling 输出，不在 MES 或前端实现调度算法。
3. 强化跨域业务闭环：ERP/WMS/Inventory/MES/Quality/Approval/Notification 的事件、facade、页面和验收脚本按真实链路推进，避免 metadata-only 或页面壳替代业务闭环。
4. 补齐 Business Console 与 PDA 的正式工作流：已落地页面继续做真实操作、错误处理、权限裁剪、上下文穿透和 focused verification；PDA 后续再拆独立 `/api/mobile/v1/**`、扫码解释、设备注册和离线 outbox/sync。
5. 平台 hardening 并行推进：MinIO/S3 multipart、DLQ replay executor、客户现场备份恢复、Windows Service/systemd 注册器、日志 UI、OAuth/OIDC/WebAuthn/外部通知 provider 等继续作为支撑线，不再把 README 叙事拉回“平台骨架阶段”。

## 非目标

1. 当前不把 PLC/DCS/SCADA 等现场协议控制逻辑写入主平台或业务服务内部；现场接入继续走 Connector Host/外部系统边界。
2. 当前不承诺完整行业套件的所有高级能力；P0 聚焦数字工厂主链路，税务、银行、完整总账月结、高级 APS 优化、完整 CMMS、LPN/HU、FEFO/FIFO 等按业务优先级后续扩展。
3. 当前不做模型托管平台；AI 能力先服务治理、查询、知识检索和低风险动作。
4. 当前不做微前端与复杂前端运行时抽象；多应用前端通过明确 workspace app/package 边界演进。
