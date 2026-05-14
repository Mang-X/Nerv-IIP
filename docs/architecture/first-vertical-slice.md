# 首批纵切链路说明

本文档把 README、上下文地图、领域模型、Agent 协议和后端启动计划中的首批闭环串成一条可实现、可验收的纵切链路。目标是在最少服务和最少动作下验证 Nerv-IIP 的平台控制面是否成立。

## 纵切目标

首批纵切只证明一件事：平台可以通过 Agent 接入一个真实运行目标，沉淀应用与实例事实，并让控制台或 Gateway 查询到最新状态。

最短链路：

```text
Docker Connector
  -> Agent Host
  -> AppHub registrations
  -> AppHub heartbeats
  -> AppHub state-snapshots
  -> PlatformGateway instances
  -> Console or API verification
```

第二条链路在第一条链路稳定后推进：

```text
Console or Gateway
  -> Ops operation-tasks
  -> Agent Host executes low-risk operation
  -> Ops operation-results
  -> AuditRecord
  -> AppHub waits for later state-snapshot
```

## 范围

### 立即实现

1. backend 与 agents 两套 solution 骨架。
2. AppHub 的 registration、heartbeat、state snapshot 三个 Agent 写接口。
3. PlatformGateway 的实例列表与实例详情查询接口。
4. Agent Host 的 HTTP 客户端和 Docker Connector 最小发现能力。
5. OpenTelemetry、health、build info 和 structured logging 的统一接线。

### 暂不阻塞

1. Ops 到 Agent 的最终命令下发传输机制。
2. Windows Service Connector 与 HTTP Connector。
3. 完整控制台 UI。
4. AI Integration 与 Knowledge 的代码骨架。
5. 高风险动作审批和人工确认 UI。

## 服务责任切分

| 环节 | 主要拥有者 | 输入 | 输出 | 不做 |
| --- | --- | --- | --- | --- |
| 发现本地目标 | Docker Connector | Docker 运行时信息 | nodeKey、applicationKey、instanceKey、capabilities | 不直接写平台数据库。 |
| 上报协议 | Agent Host | Connector 发现结果与状态 | Agent Protocol HTTP 请求 | 不拥有平台领域事实。 |
| 注册事实 | AppHub | ApplicationRegistration | Application、ApplicationVersion、ManagedNode、ApplicationInstance、CapabilityManifest | 不创建运维任务。 |
| 存活投影 | AppHub | ApplicationHeartbeat | InstanceLiveness | 不改写 reportedStatus。 |
| 状态事实 | AppHub | InstanceStateSnapshot | reportedStatus、healthStatus、state history、状态变化事件 | 不记录动作执行结果。 |
| 查询聚合 | PlatformGateway | 页面查询上下文 | 实例列表、实例详情 | 不依赖服务 Domain 或 Infrastructure。 |
| 动作闭环 | Ops | OperationTask、OperationResult | OperationAttempt、AuditRecord、失败分类 | 不成为实例最终状态真相源。 |

## 数据口径

1. `applicationKey` 标识平台管理的应用逻辑实体。
2. `version` 标识 Agent 当前观测到的应用版本。
3. `nodeKey` 标识受管节点。
4. `instanceKey` 标识具体应用实例，必须在重复注册、心跳和状态同步中保持稳定。
5. `correlationId` 串联 Agent、AppHub、Gateway、Ops 的日志和追踪。
6. `idempotencyKey` 用于注册和动作结果幂等处理。

## 实现顺序

### 1. 建工程底座

1. 创建 `backend/Nerv.IIP.sln`。
2. 创建 `backend/common/Contracts/Nerv.IIP.Contracts.AgentProtocol`。
3. 创建 `backend/common/Observability/Nerv.IIP.Observability`。
4. 创建 `backend/common/Testing/Nerv.IIP.Testing`。
5. 创建 `agents/Nerv.IIP.AgentHost.sln`。

验收：

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet build backend/Nerv.IIP.sln
dotnet restore agents/Nerv.IIP.AgentHost.sln
dotnet build agents/Nerv.IIP.AgentHost.sln
```

### 2. 起 AppHub 最小写入

1. 实现 `POST /api/agent/v1/registrations`。
2. 实现 `POST /api/agent/v1/heartbeats`。
3. 实现 `POST /api/agent/v1/state-snapshots`。
4. 为每个写入路径记录 `correlationId`、`organizationId`、`environmentId`、`agentId`。
5. 注册路径必须处理重复 `idempotencyKey`。

验收：

1. 重复发送同一注册请求不会创建重复应用、节点或实例。
2. 心跳更新 InstanceLiveness，但不修改 reportedStatus。
3. 状态快照更新 reportedStatus 和历史记录；状态未变化时不发布 InstanceStatusChanged。

### 3. 起 Agent Host 与 Docker Connector

1. Docker Connector 枚举本地测试容器。
2. Agent Host 将发现结果转换为 ApplicationRegistration。
3. Agent Host 按固定间隔发送 ApplicationHeartbeat。
4. Agent Host 在状态变化或轮询周期发送 InstanceStateSnapshot。

验收：

1. 本地至少一个测试容器可被发现并生成稳定 `instanceKey`。
2. Agent Host 能向本地 AppHub 成功发送注册、心跳和状态同步。
3. Agent 侧日志与 AppHub 侧日志可以通过 `correlationId` 关联。

### 4. 起 PlatformGateway 查询

1. 实现 `GET /api/console/v1/instances`。
2. 实现 `GET /api/console/v1/instances/{instanceKey}`。
3. 查询结果包含应用、版本、节点、实例、能力、最近心跳和最近状态快照。

验收：

1. 注册后的实例能出现在实例列表中。
2. 实例详情能显示最近心跳时间、reportedStatus、healthStatus 和能力清单。
3. Gateway 不直接依赖 AppHub.Domain 或 AppHub.Infrastructure。

### 5. 加入低风险动作闭环

1. 控制台或 Gateway 创建 restart 类型 OperationTask。
2. Ops 写入 AuditRecord。
3. Agent 执行动作并回传 OperationResult。
4. Ops 记录 OperationCompleted 或 OperationFailed。
5. AppHub 只通过后续 InstanceStateSnapshot 观察最终状态变化。

验收：

1. 每次动作都有 OperationTask、OperationAttempt 和 AuditRecord。
2. OperationResult 包含 executionStatus 与 FailureReason。
3. Ops 不直接修改 AppHub 的 ApplicationInstance 状态。

## 失败场景

| 场景 | 期望行为 |
| --- | --- |
| 注册请求重复 | 根据 idempotencyKey 和业务键幂等处理，不创建重复事实。 |
| 心跳延迟 | AppHub 根据超时策略标记不可达，不要求 Agent 显式上报 unreachable 终态。 |
| 状态快照重复 | 可写状态历史，但只有状态变化时发布 InstanceStatusChanged。 |
| Agent 暂时离线 | 平台保留最近一次状态事实，并通过 InstanceLiveness 表达可达性变化。 |
| 动作执行失败 | Ops 记录 OperationFailed、FailureReason 和审计信息，AppHub 等待后续状态同步。 |
| Gateway 查询失败 | Gateway 返回可诊断错误，不吞掉领域服务不可用信息。 |

## 首批完成定义

满足以下条件时，首批纵切可以视为完成：

1. 后端和 Agent 两套 solution 能 restore 和 build。
2. 本地依赖服务能拉起，并支持 AppHub、PlatformGateway、Ops、Agent Host 共同运行。
3. Docker Connector 能发现一个本地测试容器。
4. Agent Host 能完成注册、心跳、状态同步三类上报。
5. PlatformGateway 能查询到该实例及其最近状态。
6. 至少一个低风险动作完成任务创建、审计、执行结果回传与失败分类。
7. 日志和追踪能通过 correlationId 串起 Agent、AppHub、Gateway、Ops 的关键步骤。
