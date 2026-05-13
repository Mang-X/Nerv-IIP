# Agent 与平台协议 V1

本文档冻结 Nerv-IIP 首批纵切所需的 Agent 与平台交互基线，目标不是一次性穷尽全部动作协议，而是先把“实例如何进入平台事实模型”这件事压到可直接实现的粒度。

## 目标

1. 为 AppHub、PlatformGateway、Agent Host 和 Docker Connector 提供同一份协议边界。
2. 避免注册、心跳、状态同步在平台与 Agent 两侧各自长成不同模型。
3. 在不冻结平台到 Agent 最终下发传输机制的前提下，先冻结 Agent 到平台的公开写接口和最小查询接口。

## 单一事实来源

1. 平台与 Agent 共用的协议契约固定放在 backend/common/Contracts/Nerv.IIP.Contracts.AgentProtocol。
2. backend/Nerv.IIP.sln 与 agents/Nerv.IIP.AgentHost.sln 共同引用同一个契约项目，禁止在 agents/src/Nerv.IIP.AgentHost.Contracts 中复制一份同构 DTO。
3. agents/src/Nerv.IIP.AgentHost.Contracts 只承载 Agent 内部抽象，例如 Connector 接口、本地执行上下文、宿主生命周期模型，不承载平台公共协议 DTO。

## 首批公开接口

### AppHub.Web

1. POST /api/agent/v1/registrations
作用：创建或更新 Application、ApplicationVersion、ManagedNode、ApplicationInstance 的基础事实。

2. POST /api/agent/v1/heartbeats
作用：仅更新实例存活投影与最近一次可达时间，不直接改写 reported state。

3. POST /api/agent/v1/state-snapshots
作用：更新 ApplicationInstance.reportedStatus、状态详情和状态历史；只有状态变化时才发布状态变化事件。

### PlatformGateway.Web

1. GET /api/console/v1/instances
作用：为控制台首页或实例列表页返回当前已纳管实例事实。

2. GET /api/console/v1/instances/{instanceKey}
作用：返回单实例详情、能力清单、最近心跳和最近一次状态快照。

### Ops.Web

1. POST /api/ops/v1/operation-tasks
作用：为下一条低风险动作链路预留统一入口；首批脚手架阶段可以先落审计和任务创建，不要求同时冻结平台到 Agent 的下发传输机制。

2. POST /api/ops/v1/operation-results
作用：接收 Agent 回传的执行结果，写入 OperationTask、OperationAttempt 与 AuditRecord；实例最终状态仍以后续状态同步驱动 AppHub 更新。

## 最小契约对象

### 公共元数据

每个 Agent 到平台的写请求都应带以下公共字段：

- protocolVersion
- correlationId
- occurredAtUtc
- organizationId
- environmentId
- agentId

其中 protocolVersion 首批固定为 1.0。

### ApplicationRegistration

最小字段建议：

- idempotencyKey
- nodeKey
- nodeName
- deploymentKind
- applicationKey
- applicationName
- version
- instanceKey
- instanceName
- capabilities: CapabilityDescriptor[]
- metadata

领域映射：

- Application
- ApplicationVersion
- ManagedNode
- ApplicationInstance
- CapabilityManifest

### CapabilityDescriptor

最小字段建议：

- capabilityCode
- capabilityVersion
- category
- supportedOperations
- metadata

首批 category 只需要覆盖 runtime、log、backup、restore、lifecycle 四类。

### ApplicationHeartbeat

最小字段建议：

- instanceKey
- heartbeatAtUtc
- reachable
- agentStartedAtUtc
- latencyMs
- metadata

约束：

1. 心跳只证明存活，不承载 reported status。
2. 心跳超时后的不可达标记由 AppHub 自己计算，不要求 Agent 显式上报 unreachable 终态。

### InstanceStateSnapshot

最小字段建议：

- instanceKey
- observedAtUtc
- reportedStatus
- healthStatus
- summary
- detail
- metrics
- metadata

约束：

1. state snapshot 是事实更新，不是动作结果。
2. 只有状态变化时才发布 InstanceStatusChanged；每次快照都可以写状态历史。

### OperationResult

最小字段建议：

- operationTaskId
- attemptId
- instanceKey
- operationCode
- startedAtUtc
- finishedAtUtc
- executionStatus
- failure: FailureReason?
- output

### FailureReason

最小字段建议：

- code
- message
- category
- retryable
- detail

首批 category 只需要覆盖 validation、timeout、unreachable、permission、runtime 五类。

## 首批事件边界

### AppHub 发布

- ApplicationRegistered
- ManagedNodeDiscovered
- CapabilityManifestDeclared
- InstanceHeartbeatReceived
- InstanceStateSynchronized
- InstanceStatusChanged
- InstanceMarkedUnreachable

### Ops 发布

- OperationRequested
- OperationStarted
- OperationCompleted
- OperationFailed
- AuditRecorded

## 幂等与跟踪约束

1. 注册和动作结果必须显式携带 idempotencyKey。
2. 心跳和状态同步至少需要 correlationId 与 occurredAtUtc，用于日志关联和重复消息判定。
3. 平台日志与追踪统一使用 correlationId 和 W3C trace context 贯通，不在首批实现中自造第二套链路追踪协议。

## 首批实现范围

1. 立即实现 AppHub 的 registrations、heartbeats、state-snapshots 三个写接口。
2. 立即实现 PlatformGateway 的实例列表与实例详情两个查询接口。
3. 立即实现 Agent Host 到 AppHub 的注册、心跳、状态同步 HTTP 客户端。
4. Ops 的 operation-tasks 与 operation-results 允许先落 API 骨架和审计模型，不要求在第一批提交中完成完整动作派发。

## 非目标

1. 不在本文档中冻结平台到 Agent 的最终命令下发传输机制。
2. 不在本文档中定义全部动作参数 schema 与错误码表。
3. 不在本文档中定义 Windows Service Connector 与 HTTP Connector 的特有扩展字段。