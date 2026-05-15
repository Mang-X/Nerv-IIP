# Nerv-IIP

Nerv-IIP 是一个从 0 到 1 规划的原生 AI 应用管理平台，可面向多类行业和应用场景扩展。核心目标不是先做复杂业务系统，而是先建立一个稳定的控制面与应用管理底座，使平台能够统一管理身份权限、文件存储与对外受控访问能力，并接入、发现、观测、控制和治理真实运行中的应用实例。

当前仓库以文档优先方式启动，并已经从首批架构冻结推进到第一、第二阶段纵切实现：第一阶段验证 Connector Host 接入、AppHub 状态沉淀与 Gateway 查询，第二阶段验证低风险运维动作创建、派发、执行、结果回传与审计记录。

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

## 技术基线

### 前端

- Vue 3
- Vue Router 官方文件路由插件
- Pinia
- Pinia Colada
- VueUse
- shadcn-vue
- es-toolkit
- pnpm workspace
- Vite+
- Hey API

### 后端

- .NET 10 SDK
- netcorepal-cloud-framework
- FastEndpoints
- ASP.NET Core Authentication/Authorization
- OpenTelemetry
- PostgreSQL
- Redis
- FusionCache
- RabbitMQ
- MinIO
- Qdrant

### AI

- Microsoft.Extensions.AI
- Microsoft.Extensions.DataIngestion
- Microsoft.Extensions.VectorData
- 复杂 AI 自主工作流框架仅在确有 autonomous workflow 需求时再评估引入

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

### 架构说明

1. docs/architecture/repo-layout.md
2. docs/architecture/context-map.md
3. docs/architecture/glossary.md
4. docs/architecture/caching-baseline.md
5. docs/architecture/iam-authentication-baseline.md
6. docs/architecture/platform-sdk-baseline.md
7. docs/architecture/file-storage-baseline.md
8. docs/architecture/notification-baseline.md
9. docs/architecture/backend-cleanddd-netcorepal-guidelines.md
10. docs/architecture/core-domain-model-v1.md
11. docs/architecture/connector-platform-protocol-v1.md
12. docs/architecture/first-vertical-slice.md
13. docs/architecture/second-vertical-slice-ops.md
14. docs/architecture/frontend-structure.md
15. docs/architecture/api-contract-and-codegen.md
16. docs/architecture/ai-boundaries.md
17. docs/architecture/knowledge-source-lifecycle.md
18. docs/architecture/backend-bootstrap-plan.md
19. docs/architecture/implementation-readiness.md

### 实施计划

1. docs/superpowers/plans/2026-05-14-first-vertical-slice.md
2. docs/superpowers/plans/2026-05-15-second-vertical-slice-low-risk-ops.md

## 里程碑

1. M0：完成 ADR、单仓结构、前后端基础工程与文档冻结。
2. M1：完成 IAM、组织环境上下文、文件存储基线、控制台基础登录能力和外部应用授权基线。
3. M2：完成 Connector Host 接入、应用注册、心跳与状态同步。
4. M3：完成启停、重启、日志与审计闭环。
5. M4：完成 MCP 查询工具、知识入库与低风险执行能力。
6. M5：完成平台 MVP 首发与真实演练。

## 当前状态

当前仓库已经在首批架构文档基础上落地第一、第二阶段纵切，并将关键设计沉淀为 ADR 与架构文档。平台 HTTP 接口统一采用 FastEndpoints；Program.cs 只负责服务注册、中间件和 `UseFastEndpoints()` 接线，具体接口放在各 Web 项目的 `Endpoints/` 目录。

第一阶段可以用 `scripts/verify-first-slice.ps1` 完成本地纵切验证：backend 与 connector-hosts 两套 solution 可 restore/build/test，AppHub 可接收注册、心跳和状态快照，PlatformGateway 可通过 AppHub 查询实例列表与详情，Connector Host 可通过 Platform SDK 上报一个 Docker Connector 发现的目标。

第二阶段可以用 `scripts/verify-second-slice-ops.ps1` 完成本地低风险动作闭环验证：Gateway 创建实例 restart 运维任务，Ops 记录任务、尝试和审计事实，Connector Host 通过 Ops SDK 拉取 pending task，Docker Connector 执行动作并回传结果，Gateway 可查询任务详情。当前状态适合工程联调、接口走查和后续功能开发；尚不是面向真实用户的可部署产品。

下一阶段重点：

1. 建立 frontend 工作区骨架与 api-client 生成链路，把实例查询和低风险 restart 操作接入控制台。
2. 将当前内存态 IAM、AppHub、Ops 和缓存实现推进到 PostgreSQL、RabbitMQ、Redis 等真实基础设施。
3. 补齐 FileStorage 的真实上传下载闭环、IAM 完整授权链路和控制台登录能力。
4. 扩展 Ops 的审批、权限、通知和持久化 outbox，逐步覆盖更高风险动作。

## 非目标

1. 首期不做特定行业协议接入。
2. 首期不做完整行业业务套件。
3. 首期不做模型托管平台。
4. 首期不做微前端与复杂前端运行时抽象。
