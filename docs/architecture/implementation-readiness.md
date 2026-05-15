# 实施状态清单

本文档记录 Nerv-IIP 从“文档冻结完成”到“第一迭代纵切骨架已落地”的状态，给出首批实施的环境前置、目录落点、引用规则、已完成范围和后续边界。

## 当前结论

1. 平台 HTTP 服务命名已经冻结为 .Web、.Domain、.Infrastructure。
2. Connector Host 与平台的 v1 协议边界已经冻结到公开接口和最小对象级别。
3. 后端 CleanDDD 与 netcorepal 的模板参数、目录、事件、事务、仓储和测试约定已经冻结。
4. 核心术语、Platform SDK 模块边界、IAM 对外授权边界、文件存储基线、通知能力基线、知识源生命周期和首批纵切验收口径已经补齐。
5. backend、connector-hosts 两个工作面已经完成第一迭代纵切骨架，可通过 `scripts/verify-first-slice.ps1` 做本地验证。
6. 平台 HTTP 接口统一使用 FastEndpoints；新增接口必须放在 Web 项目的 `Endpoints/` 目录，不在启动文件中写 Minimal API 路由映射。

## 环境前置

根据 netcorepal-cloud-framework 官方入门文档，首批后端实施需要先满足以下条件：

1. 安装 .NET 10 SDK，作为 Nerv-IIP 当前目标框架。
2. 安装 Docker 环境，用于本地调试、自动化测试和依赖服务联调。
3. 创建服务时显式生成 `net10.0`；后续等 netcorepal-cloud-framework 明确适配 .NET 11 后，再统一升级到 .NET 11。
4. 安装 NetCorePal.Template：`dotnet new install NetCorePal.Template`。
5. 创建服务前运行 `dotnet new netcorepal-web --help` 核对本机模板参数。
6. 平台领域服务优先使用 netcorepal 的 web 模板作为初始骨架，但命令必须显式指定 `--Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false`，详见 docs/architecture/backend-cleanddd-netcorepal-guidelines.md。

## 共享契约落点

1. 平台与 Connector Host 公共协议契约的源码事实来源固定放在 backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol。
2. 首批单仓实施时，backend/Nerv.IIP.sln 与 connector-hosts/Nerv.IIP.ConnectorHost.sln 可以共同引用该项目，确保注册、心跳、状态同步、动作结果 DTO 只有一份代码实现。
3. Platform SDK 的首批源码落点固定放在 backend/common/Sdk，先按 Core、Auth、ConnectorProtocol、FileStorage 四个最小模块拆分。
4. 发布边界上，Connector Host 只依赖 Platform SDK、版本化公开契约包、OpenAPI 契约或等价契约，不依赖主平台源码或服务实现项目。
5. Connector Host 与主平台主版本必须对齐；同一主版本内，Connector Host 小版本可以低于主平台小版本。
6. connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts 只保留 Connector Host 内部抽象，不复制公共协议 DTO。

## 首批工程创建顺序

### Wave 1. 底座

1. backend/Nerv.IIP.sln
2. backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol
3. backend/common/Sdk/Nerv.IIP.Sdk.Core
4. backend/common/Sdk/Nerv.IIP.Sdk.Auth
5. backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol
6. backend/common/Sdk/Nerv.IIP.Sdk.FileStorage
7. backend/common/Caching/Nerv.IIP.Caching
8. backend/common/Observability/Nerv.IIP.Observability
9. backend/common/Testing/Nerv.IIP.Testing

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
6. frontend/packages/layer-base
7. frontend/packages/layer-platform

## 引用规则

1. *.Web 可以引用同服务的 *.Domain、*.Infrastructure 和 backend/common 下的窄共享库。
2. *.Domain 不引用 *.Infrastructure，也不引用其它服务的 Domain。
3. *.Infrastructure 可以引用同服务的 *.Domain 和 backend/common 下的窄共享库。
4. PlatformGateway.Web 不引用任何服务的 Domain 或 Infrastructure，只通过稳定契约、OpenAPI 客户端或明确的查询接口聚合数据。
5. ConnectorHost.Application 可以引用 Nerv.IIP.Sdk.Core、Nerv.IIP.Sdk.Auth、Nerv.IIP.Sdk.ConnectorProtocol、Nerv.IIP.Contracts.ConnectorProtocol 和 Connector Host 内部抽象，但不直接引用平台服务实现项目；发布后以 Platform SDK、版本化契约包或等价契约作为依赖边界。
6. SDK 项目只能引用公开契约、Sdk.Core、Sdk.Auth 或其它明确允许的 SDK 模块，不能引用服务 Web、Domain、Infrastructure 或数据库模型。

## 开工边界

### 第一迭代已落地范围

1. backend 与 connector-hosts 两套 solution 创建。
2. Platform SDK 的 Core、Auth、ConnectorProtocol、FileStorage 最小项目骨架。
3. IAM 用户、角色、权限、会话、外部客户端、Connector Host 凭证和授权授予的最小事实骨架。
4. FileStorage 文件元数据、上传会话、上传指令、下载授权、Upload Provider 抽象、FilePurposePolicy、scanStatus 和 MinIO/object storage 适配的最小服务骨架。
5. AppHub registrations、heartbeats、state-snapshots 三个接口。
6. PlatformGateway 实例列表与实例详情查询接口。
7. Connector Host 通过 Nerv.IIP.Sdk.ConnectorProtocol 到 AppHub 的客户端与 Docker Connector 空壳。
8. 统一 OpenTelemetry 接线、health、build info、基础 structured logging。
9. 统一 FusionCache 接线、Redis L2/backplane、缓存键命名和首批读侧缓存策略。
10. 以 docs/architecture/first-vertical-slice.md 作为首批纵切验收口径。
11. 以 docs/architecture/backend-cleanddd-netcorepal-guidelines.md 作为后端代码放置、事件转换、事务和测试验收口径。

### 当前初步使用方式

1. 运行 `pwsh scripts/verify-first-slice.ps1` 可验证 backend 与 connector-hosts 的 restore、build、test，以及 AppHub 到 PlatformGateway 的第一条本地纵切。
2. AppHub 当前提供 registration、heartbeat、state-snapshot 和内部实例查询接口。
3. PlatformGateway 当前提供实例列表与实例详情查询接口。
4. Connector Host 当前可通过 Platform SDK 将 Docker Connector 的发现结果上报到 AppHub。
5. 当前实现用于本地开发和接口联调，不包含生产部署、真实持久化、完整认证授权 UI 或运维动作闭环。

### 可以并行但不阻塞开工的事项

1. Ops 到 Connector Host 的最终命令下发传输机制。
2. Sdk.Ops 与 Sdk.Observability 的完整实现；第一迭代只冻结方向，不阻塞注册纵切。
3. AI Integration 与 Knowledge 的具体代码骨架。
4. Notification 的具体代码骨架、站内通知纵切和外部通道 provider；边界口径应遵守 docs/architecture/notification-baseline.md。
5. KnowledgeSource 的完整管理后台，但生命周期口径应遵守 docs/architecture/knowledge-source-lifecycle.md。
6. 复杂 IAM 授权能力，包括跨组织委派、临时授权、完整 OAuth/OIDC 协议矩阵、MFA、SSO、细粒度 ABAC 与第三方应用市场。
7. 前端视觉系统和组件皮肤细节。

## 开工验收标准

满足以下条件时，说明仓库已经从“规划阶段”进入“可持续实施阶段”：

1. dotnet restore backend/Nerv.IIP.sln 通过。
2. dotnet build backend/Nerv.IIP.sln 通过。
3. dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln 通过。
4. dotnet build connector-hosts/Nerv.IIP.ConnectorHost.sln 通过。
5. AppHub.Web 可接收 registration、heartbeat、state snapshot。
6. Connector Host 可向本地 AppHub 成功发送至少一组注册、心跳、状态同步请求。
7. PlatformGateway 能查询到至少一个被注册的实例事实。

## 结论

Nerv-IIP 已经完成第一迭代纵切骨架：backend/common、Iam、FileStorage、AppHub、PlatformGateway、Ops、Connector Host 和 Docker Connector 的最小工程结构与验证链路已经存在。下一步不再是 scaffold，而是把当前内存态和骨架能力推进到真实持久化、完整 IAM 授权、FileStorage 上传下载、前端控制台和第二迭代低风险动作闭环。具体第一迭代任务清单见 docs/superpowers/plans/2026-05-14-first-vertical-slice.md。
