# 后端启动与首批实施计划

本文档把后端架构决策转成首批可执行顺序，目标是在最短时间内验证平台控制面的核心闭环，而不是一次性铺满全部服务。

## 实施策略

1. 先建工程底座，再建服务骨架。
2. 先冻结契约和边界，再做动作闭环。
3. 先跑 Agent 与 Docker Connector，再扩展更多宿主环境。
4. 先验证应用注册、心跳、状态同步与一个低风险动作，不先追全量运维能力。

## 实施顺序

### Step 1. 建后端工程底座

- 创建 backend 根 solution。
- 创建 Directory.Build.props 与 Directory.Packages.props。
- 建立 services、gateway、common、tests 的基础目录与命名规则。
- 每个平台 HTTP 服务目录内部默认采用 src 与 tests，并在 src 下采用 .Web、.Domain、.Infrastructure 三项目主线。
- Iam、AppHub、Ops 优先通过 netcorepal-web 模板创建，但必须显式传入 `--Framework net10.0`、`--Database PostgreSQL`、`--MessageQueue RabbitMQ`、`--UseAspire false`、`--IncludeCopilotInstructions false`、`--UseAdmin false`，具体约定见 docs/architecture/backend-cleanddd-netcorepal-guidelines.md。
- PlatformGateway 是薄 BFF 例外，默认只保留 .Web，不为它强行创建空 Domain 与 Infrastructure。

产物：

- backend/Nerv.IIP.sln
- backend/Directory.Build.props
- backend/Directory.Packages.props
- backend/common/Contracts、Observability、Testing 三个最小共享库

验收：

- dotnet restore backend/Nerv.IIP.sln 通过
- dotnet build backend/Nerv.IIP.sln 通过

### Step 2. 起平台核心服务骨架

- 先起 PlatformGateway、Iam、AppHub、Ops 四个最小 Web 服务。
- 每个服务只放健康检查、基础配置、OpenTelemetry 接线、最小 HTTP 入口。
- Application 相关命令、查询和事件处理器先以内聚目录形式放在 Web/Application 下，不默认拆成独立项目。

产物：

- backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web
- backend/gateway/PlatformGateway/tests/Nerv.IIP.PlatformGateway.Web.Tests
- backend/services/Iam/src/Nerv.IIP.Iam.Web
- backend/services/Iam/src/Nerv.IIP.Iam.Domain
- backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure
- backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests
- backend/services/AppHub/src/Nerv.IIP.AppHub.Web
- backend/services/AppHub/src/Nerv.IIP.AppHub.Domain
- backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure
- backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests
- backend/services/Ops/src/Nerv.IIP.Ops.Web
- backend/services/Ops/src/Nerv.IIP.Ops.Domain
- backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure
- backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests

验收：

- 四个 Web 服务都能启动
- 至少暴露 health 与 build info 端点
- 输出统一 traces、metrics 和 structured logs

### Step 3. 定应用接入协议最小闭环

- 定义注册、心跳、能力声明、实例状态同步四类契约。
- 固化版本号、幂等键与错误结果模型。
- 共享契约项目固定放在 backend/common/Contracts/Nerv.IIP.Contracts.AgentProtocol，并由 backend 与 agents 两套 solution 共同引用。

推荐首批契约对象：

- ApplicationRegistration
- ApplicationHeartbeat
- CapabilityDescriptor
- InstanceStateSnapshot
- OperationResult
- FailureReason

验收：

- 平台与 Agent 共享同一份契约定义
- 至少完成一轮端到端序列化与反序列化测试

### Step 4. 起 Agent Host 独立工程与首个 Connector

- 在 agents 根目录下建立独立 solution。
- 实现 Agent Host 最小宿主。
- 首个 Connector 优先做 Docker Connector。
- Agent Host 属于独立后台宿主，不适用平台 HTTP 服务的 .Web、.Domain、.Infrastructure 命名约束。

产物：

- agents/Nerv.IIP.AgentHost.sln
- agents/src/Nerv.IIP.AgentHost.Host
- agents/src/Nerv.IIP.AgentHost.Application
- agents/src/Nerv.IIP.AgentHost.Contracts
- agents/src/Nerv.IIP.AgentHost.Connectors.Abstractions
- agents/src/Nerv.IIP.AgentHost.Connectors.Docker

验收：

- Docker Connector 能发现本地测试容器
- 能向平台发送注册、心跳、状态同步

### Step 5. 落基础设施开发编排

- 提供 PostgreSQL、Redis、RabbitMQ、MinIO、Qdrant、OpenTelemetry 的本地开发编排。
- 让 Gateway、AppHub、Ops、Agent Host 在同一本地开发编排中联调。

验收：

- 本地依赖服务可一键拉起
- 平台服务与 Agent Host 可共同运行并互通

### Step 6. 打第一条纵切链路

详细验收口径见 docs/architecture/first-vertical-slice.md。

第一条链路：

- 应用注册 -> 心跳 -> 实例状态同步 -> 控制台可见

第二条链路：

- 重启动作 -> 审计记录 -> 结果回传

验收：

- 控制台或 Gateway 可查询最新实例事实
- 至少一个低风险动作具备任务记录、执行、审计与结果回传

## 并行关系

1. Step 1 与 Step 2 顺序执行。
2. Step 3 与 Step 4 可在 Step 2 后并行推进。
3. Step 5 可在 Step 2 后并行推进。
4. Step 6 依赖 Step 3、Step 4、Step 5。

## 命名与结构约束

1. 平台 HTTP 服务入口项目统一使用 .Web，而不是 .Host。
2. Domain 与 Infrastructure 保持独立项目。
3. Application 默认作为 .Web 项目内部目录，而不是默认独立项目。
4. Contracts 仅在确有跨进程共享契约时按需拆出，不作为默认层。
5. Agent Host 仍可保留 .Host 命名，因为它不是平台 HTTP Web 服务。
6. 项目名统一采用点分 PascalCase，例如 Nerv.IIP.AppHub.Web。

## 建议命令

模板创建命令以 docs/architecture/backend-cleanddd-netcorepal-guidelines.md 为准。后端 solution 创建后，基础验证命令为：

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
dotnet restore agents/Nerv.IIP.AgentHost.sln
dotnet build agents/Nerv.IIP.AgentHost.sln
```

## 冻结结论

1. 不先做 Knowledge 实现，先做平台控制面闭环。
2. 不先做复杂 AI agent 流程，先做 AI Integration 的治理边界。
3. 不先做多 Connector，先用 Docker Connector 跑通协议。
4. 不先做所有运维动作，先做注册、状态同步和一个低风险动作。
5. 不先细抠全部领域模型，先用最短纵切验证服务边界是否合理。
