# Nerv-IIP

Nerv-IIP 是一个从 0 到 1 规划的原生 AI 工业互联网平台，核心目标不是先做复杂业务系统，而是先建立一个稳定的控制面与应用管理底座，使平台能够统一管理身份权限，对外授予受控访问能力，并接入、发现、观测、控制和治理真实运行中的应用实例。

当前仓库以文档优先方式启动。第一阶段先冻结不可轻易反转的架构决策、服务边界、前端范式、AI 边界与基础设施基线，再进入工程骨架和最短纵切链路实现。

## 项目目标

1. 建立统一的平台控制面，覆盖身份、权限、组织、环境、对外授权、应用目录、实例状态、运维动作与审计闭环。
2. 建立标准化应用接入模型，使 Docker、Windows Service、HTTP 服务等不同宿主环境都可以被平台纳管。
3. 建立受治理的 AI 能力边界，优先交付 MCP 查询工具、知识检索和低风险执行能力。
4. 在不托管模型的前提下，采用官方 .NET AI 生态完成模型接入、知识处理与工具治理。
5. 通过一个可被平台完整管理并受底座授权约束的示例应用验证平台底座，而不是在首期堆叠复杂业务功能。

## 核心原则

1. 平台底座优先，业务域后置。
2. 逻辑边界先冻结，物理部署保留弹性。
3. 前端采用显式 Vue 结构，不引入伪 Nuxt runtime。
4. 后端按服务边界组织，禁止以共享库名义回退到大单体。
5. 应用接入统一走 Agent Host 与 Connector 模式。
6. AI 能力先做治理、查询和低风险动作，不先做模型托管和复杂自治代理。
7. 文档、契约和目录职责优先稳定，以降低团队协作和 AI 协作成本。

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
- OpenTelemetry
- PostgreSQL
- Redis
- RabbitMQ
- MinIO
- Qdrant

### AI

- Microsoft.Extensions.AI
- Microsoft.Extensions.DataIngestion
- Microsoft.Extensions.VectorData
- Microsoft Agent Framework 仅在确有 agentic workflow 需求时再评估引入

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
  agents/
  infra/
  scripts/
```

## 文档导航

建议先按“ADR -> 架构说明 -> 实施说明”的顺序阅读。ADR 解释为什么这样取舍，架构说明定义边界和术语，实施说明负责把首批工程落到可执行粒度。

### ADR

1. docs/adr/0001-backend-solution-and-service-boundaries.md
2. docs/adr/0002-agent-connector-and-app-integration-contract.md
3. docs/adr/0003-data-and-messaging-baseline.md
4. docs/adr/0004-ai-integration-boundary-and-governance.md
5. docs/adr/0005-knowledge-ingestion-and-retrieval.md
6. docs/adr/0006-frontend-workspace-structure.md
7. docs/adr/0007-vue-router-file-routing-colocation.md

### 架构说明

1. docs/architecture/repo-layout.md
2. docs/architecture/context-map.md
3. docs/architecture/glossary.md
4. docs/architecture/backend-cleanddd-netcorepal-guidelines.md
5. docs/architecture/core-domain-model-v1.md
6. docs/architecture/agent-platform-protocol-v1.md
7. docs/architecture/first-vertical-slice.md
8. docs/architecture/frontend-structure.md
9. docs/architecture/api-contract-and-codegen.md
10. docs/architecture/ai-boundaries.md
11. docs/architecture/knowledge-source-lifecycle.md
12. docs/architecture/backend-bootstrap-plan.md
13. docs/architecture/implementation-readiness.md

## 里程碑

1. M0：完成 ADR、单仓结构、前后端基础工程与文档冻结。
2. M1：完成 IAM、组织环境上下文、控制台基础登录能力和外部应用授权基线。
3. M2：完成 Agent 接入、应用注册、心跳与状态同步。
4. M3：完成启停、重启、日志与审计闭环。
5. M4：完成 MCP 查询工具、知识入库与低风险执行能力。
6. M5：完成平台 MVP 首发与真实演练。

## 当前状态

当前仓库尚未创建业务代码，已完成第一轮架构决策冻结，并将关键设计沉淀为 ADR 与架构文档。当前已经补齐首批实现所需的环境前置、平台与 Agent 的 v1 协议边界、共享契约放置规则、核心术语、后端 CleanDDD/netcorepal 落地规范、知识源生命周期，以及第一条纵切链路的验收口径。下一阶段可以按以下顺序直接进入工程实现：

1. 建立 backend 与 agents 两套 solution 骨架。
2. 建立 frontend 工作区骨架与 api-client 生成链路。
3. 打通应用注册、心跳、状态同步到控制台可见的最短纵切链路。
4. 再进入低风险运维动作与知识能力实现。

## 非目标

1. 首期不做工业协议接入。
2. 首期不做完整 MES、ERP、WMS 业务产品。
3. 首期不做模型托管平台。
4. 首期不做微前端与复杂前端运行时抽象。
