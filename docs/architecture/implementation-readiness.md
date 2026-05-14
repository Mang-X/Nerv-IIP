# 实施就绪清单

本文档把 Nerv-IIP 从“文档冻结完成”推进到“可以直接开始建工程”的状态，给出首批实施的环境前置、目录落点、引用规则与开工边界。

## 当前结论

1. 平台 HTTP 服务命名已经冻结为 .Web、.Domain、.Infrastructure。
2. Agent 与平台的 v1 协议边界已经冻结到公开接口和最小对象级别。
3. 后端 CleanDDD 与 netcorepal 的模板参数、目录、事件、事务、仓储和测试约定已经冻结。
4. 核心术语、IAM 对外授权边界、知识源生命周期和首批纵切验收口径已经补齐。
5. 首批实现可以从 backend、agents 两个工作面直接开工，无需再等待新的架构决策。

## 环境前置

根据 netcorepal-cloud-framework 官方入门文档，首批后端实施需要先满足以下条件：

1. 安装 .NET 10 SDK，作为 Nerv-IIP 当前目标框架。
2. 安装 Docker 环境，用于本地调试、自动化测试和依赖服务联调。
3. 创建服务时显式生成 `net10.0`；后续等 netcorepal-cloud-framework 明确适配 .NET 11 后，再统一升级到 .NET 11。
4. 安装 NetCorePal.Template：`dotnet new install NetCorePal.Template`。
5. 创建服务前运行 `dotnet new netcorepal-web --help` 核对本机模板参数。
6. 平台领域服务优先使用 netcorepal 的 web 模板作为初始骨架，但命令必须显式指定 `--Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false`，详见 docs/architecture/backend-cleanddd-netcorepal-guidelines.md。

## 共享契约落点

1. 平台与 Agent 公共协议契约固定放在 backend/common/Contracts/Nerv.IIP.Contracts.AgentProtocol。
2. backend/Nerv.IIP.sln 与 agents/Nerv.IIP.AgentHost.sln 共同引用该项目，确保注册、心跳、状态同步、动作结果 DTO 只有一份代码实现。
3. agents/src/Nerv.IIP.AgentHost.Contracts 只保留 Agent 内部抽象，不复制公共协议 DTO。

## 首批工程创建顺序

### Wave 1. 底座

1. backend/Nerv.IIP.sln
2. backend/common/Contracts/Nerv.IIP.Contracts.AgentProtocol
3. backend/common/Caching/Nerv.IIP.Caching
4. backend/common/Observability/Nerv.IIP.Observability
5. backend/common/Testing/Nerv.IIP.Testing

### Wave 2. 平台服务骨架

1. backend/services/AppHub/src/Nerv.IIP.AppHub.Web
2. backend/services/AppHub/src/Nerv.IIP.AppHub.Domain
3. backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure
4. backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web
5. backend/services/Iam/src/Nerv.IIP.Iam.Web
6. backend/services/Iam/src/Nerv.IIP.Iam.Domain
7. backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure
8. backend/services/Ops/src/Nerv.IIP.Ops.Web
9. backend/services/Ops/src/Nerv.IIP.Ops.Domain
10. backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure

### Wave 3. Agent 工程骨架

1. agents/Nerv.IIP.AgentHost.sln
2. agents/src/Nerv.IIP.AgentHost.Host
3. agents/src/Nerv.IIP.AgentHost.Application
4. agents/src/Nerv.IIP.AgentHost.Contracts
5. agents/src/Nerv.IIP.AgentHost.Connectors.Abstractions
6. agents/src/Nerv.IIP.AgentHost.Connectors.Docker

### Wave 4. 前端骨架

1. frontend/package.json、pnpm-workspace.yaml、vite.config.ts、tsconfig.base.json
2. frontend/apps/console
3. frontend/packages/api-client
4. frontend/packages/ui
5. frontend/packages/app-shell
6. frontend/packages/layer-base
7. frontend/packages/layer-platform

## 引用规则

1. *.Web 可以引用同服务的 *.Domain、*.Infrastructure 和 backend/common 下的窄共享库。
2. *.Domain 不引用 *.Infrastructure，也不引用其它服务的 Domain。
3. *.Infrastructure 可以引用同服务的 *.Domain 和 backend/common 下的窄共享库。
4. PlatformGateway.Web 不引用任何服务的 Domain 或 Infrastructure，只通过稳定契约、OpenAPI 客户端或明确的查询接口聚合数据。
5. AgentHost.Application 可以引用 Nerv.IIP.Contracts.AgentProtocol 和 AgentHost 内部抽象，但不直接引用平台服务实现项目。

## 开工边界

### 立即进入实现的范围

1. backend 与 agents 两套 solution 创建。
2. IAM 用户、角色、权限、外部客户端和授权授予的最小事实骨架。
3. AppHub registrations、heartbeats、state-snapshots 三个接口。
4. PlatformGateway 实例列表与实例详情查询接口。
5. Agent Host 到 AppHub 的 HTTP 客户端与 Docker Connector 空壳。
6. 统一 OpenTelemetry 接线、health、build info、基础 structured logging。
7. 统一 FusionCache 接线、Redis L2/backplane、缓存键命名和首批读侧缓存策略。
8. 以 docs/architecture/first-vertical-slice.md 作为首批纵切验收口径。
9. 以 docs/architecture/backend-cleanddd-netcorepal-guidelines.md 作为后端代码放置、事件转换、事务和测试验收口径。

### 可以并行但不阻塞开工的事项

1. Ops 到 Agent 的最终命令下发传输机制。
2. AI Integration 与 Knowledge 的具体代码骨架。
3. KnowledgeSource 的完整管理后台，但生命周期口径应遵守 docs/architecture/knowledge-source-lifecycle.md。
4. 复杂 IAM 授权能力，包括跨组织委派、临时授权、完整 OAuth/OIDC 协议矩阵、细粒度 ABAC 与第三方应用市场。
5. 前端视觉系统和组件皮肤细节。

## 开工验收标准

满足以下条件时，说明仓库已经从“规划阶段”进入“可持续实施阶段”：

1. dotnet restore backend/Nerv.IIP.sln 通过。
2. dotnet build backend/Nerv.IIP.sln 通过。
3. dotnet restore agents/Nerv.IIP.AgentHost.sln 通过。
4. dotnet build agents/Nerv.IIP.AgentHost.sln 通过。
5. AppHub.Web 可接收 registration、heartbeat、state snapshot。
6. Agent Host 可向本地 AppHub 成功发送至少一组注册、心跳、状态同步请求。
7. PlatformGateway 能查询到至少一个被注册的实例事实。

## 结论

就当前文档状态而言，Nerv-IIP 已经达到可以开始实施的程度。下一步不再需要新增架构决策，直接进入 backend/common、AppHub、PlatformGateway、Agent Host 的实际 scaffold 与最短纵切实现。
