# Connector Host 与平台协议 V1

本文档冻结 Nerv-IIP 首批纵切所需的 Connector Host 与平台交互基线。当前范围覆盖“实例如何进入平台事实模型”和“低风险动作如何完成任务、执行、结果与审计闭环”，但不一次性穷尽全部动作协议。

## 目标

1. 为 AppHub、PlatformGateway、Connector Host 和 Docker Connector 提供同一份协议边界。
2. 避免注册、心跳、状态同步在平台与 Connector Host 两侧各自长成不同模型。
3. 通过 Ops claim/lease 拉取模型冻结第二阶段低风险动作的最小下发、领取、续期、放弃和结果回传接口。

## 契约事实来源与独立升级

1. Connector Protocol 的源码事实来源固定放在 backend/common/Contracts/Nerv.IIP.Contracts.ConnectorProtocol。
2. Connector Protocol 的客户端封装固定落在 backend/common/Sdk/Nerv.IIP.Sdk.ConnectorProtocol，并依赖 Sdk.Core、Sdk.Auth 与公开协议契约。
3. Ops 的源码事实来源固定放在 backend/common/Contracts/Nerv.IIP.Contracts.Ops。
4. Ops 的客户端封装固定落在 backend/common/Sdk/Nerv.IIP.Sdk.Ops，并依赖 Sdk.Core、Sdk.Auth 与公开 Ops 契约。
5. 首批单仓实施时，backend/Nerv.IIP.sln 与 connector-hosts/Nerv.IIP.ConnectorHost.sln 可以临时以项目引用方式消费 SDK 与契约源码以减少重复代码。
6. 发布边界上，Connector Protocol 与 Ops SDK 都是 Platform SDK 的一部分，必须表现为版本化契约包、OpenAPI 契约或等价的公开契约；Connector Host 不能依赖主平台服务实现项目。
7. Connector Host 的主版本必须与主平台主版本对齐；同一主版本内，Connector Host 小版本可以低于主平台小版本。
8. 主平台必须兼容受支持的 Connector Protocol 与 Ops SDK 小版本；同一主版本内应尽量只做向后兼容新增，引入破坏性变更时必须显式提升协议主版本，并保留迁移窗口。
9. connector-hosts/src/Nerv.IIP.ConnectorHost.Contracts 只承载 Connector Host 内部抽象，例如 Connector 接口、本地执行上下文、宿主生命周期模型，不承载平台公共协议 DTO。
10. Connector Host 或外部客户端调用平台公开接口时，其身份凭证、组织环境范围和能力授权由 IAM 统一管理；本协议只定义业务载荷和公共上下文字段，不复制 IAM 的授权模型。
11. 主平台、Connector Host 和各类 Connector 在同一主版本内允许独立发布和升级；互操作性由 platformVersion、sdkVersion、protocolVersion、capabilityVersion、公开 API 版本和权限范围共同保证。

Connector Host 机器身份认证、短期 access token、capability scope 到 permission code 的映射和旧版 header-secret 迁移路径见 [Connector Host 机器身份认证终态](connector-host-machine-auth.md)。

## 首批公开接口

### AppHub.Web

1. POST /api/connectors/v1/registrations
作用：创建或更新 Application、ApplicationVersion、ManagedNode、ApplicationInstance 的基础事实。
响应：返回 `registrationId`、`instanceKey` 和绑定该注册实例的 `ingestionToken`。`ingestionToken` 只用于后续同一 organization/environment/connectorHostId/instanceKey 的心跳与状态同步。

2. POST /api/connectors/v1/heartbeats
作用：仅更新实例存活投影与最近一次可达时间，不直接改写 reported state。
认证：必须携带 `X-Connector-Ingestion-Token`。AppHub 从 token 派生 registrationId、organizationId、environmentId、connectorHostId 和 instanceKey，并拒绝 body 与 token 不一致的上报。

3. POST /api/connectors/v1/state-snapshots
作用：更新 ApplicationInstance.reportedStatus、状态详情和状态历史；只有状态变化时才发布状态变化事件。
认证：必须携带 `X-Connector-Ingestion-Token`，身份派生和 body 一致性要求与 heartbeats 相同。

### PlatformGateway.Web

1. GET /api/console/v1/instances
作用：为控制台首页或实例列表页返回当前已纳管实例事实。

2. GET /api/console/v1/instances/{instanceKey}
作用：返回单实例详情、能力清单、最近心跳和最近一次状态快照。

### Ops.Web

1. POST /api/ops/v1/operation-tasks
作用：创建 OperationTask，并记录任务请求审计事实；如果匹配的 operation template `RequiresApproval=true`，任务先进入 `approval-pending`。

2. GET /api/ops/v1/operation-tasks/{operationTaskId}
作用：查询 OperationTask、OperationAttempt 与 AuditRecord 明细。

3. POST /api/ops/v1/operation-tasks/{operationTaskId}/approval/approve
作用：批准高风险 OperationTask，使任务从 `approval-pending` 回到 `queued`，随后才可被 Connector Host 领取。

4. POST /api/ops/v1/operation-tasks/{operationTaskId}/approval/reject
作用：拒绝高风险 OperationTask，使任务进入 `rejected` 终态，不再进入执行队列。

5. POST /api/ops/v1/operation-tasks/claims
作用：Connector Host 按 organizationId、environmentId 和 connectorHostId 原子领取待执行任务，并获得 leaseId、leasedAtUtc、leasedUntilUtc、attemptNo 和 maxAttempts。GET /api/ops/v1/operation-tasks/pending 仅作为第二阶段兼容入口，语义等同于默认 5 分钟 lease 的 claim。

6. POST /api/ops/v1/operation-tasks/{operationTaskId}/lease/heartbeat
作用：Connector Host 使用当前 leaseId 续期 leasedUntilUtc；leaseId、connectorHostId、organizationId 和 environmentId 必须匹配当前 active attempt。

7. POST /api/ops/v1/operation-tasks/{operationTaskId}/lease/abandon
作用：Connector Host 使用当前 leaseId 主动放弃任务，写入 abandonReason；未耗尽 maxAttempts 时任务回到 queued，耗尽后转 failed。

8. POST /api/ops/v1/operation-results
作用：接收 Connector Host 回传的执行结果，写入 OperationTask、OperationAttempt 与 AuditRecord；实例最终状态仍以后续状态同步驱动 AppHub 更新。

## 最小契约对象

### 公共元数据

每个 Connector Host 到平台的写请求都应带以下公共字段：

- protocolVersion
- sdkVersion
- correlationId
- occurredAtUtc
- organizationId
- environmentId
- connectorHostId

其中 protocolVersion 和 sdkVersion 首批固定为 1.0。平台应拒绝未知主版本，并对受支持主版本内的小版本差异做兼容处理或返回可诊断错误。

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

注册响应最小字段：

- registrationId
- instanceKey
- ingestionToken

约束：

1. `ingestionToken` 由 AppHub 签发并绑定 registrationId、organizationId、environmentId、connectorHostId 和 instanceKey。
2. 心跳与状态同步不得再依赖请求 body 自证租户或实例身份；服务端必须校验 token 中的身份与 body 完全一致后才写入实例事实。
3. 非 Development 环境必须显式配置 `ConnectorIngestionToken:SigningKey`，不得使用仓库内本地 fallback。

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
- connectorHostStartedAtUtc
- latencyMs
- metadata

约束：

1. 心跳只证明存活，不承载 reported status。
2. 心跳超时后的不可达标记由 AppHub 自己计算，不要求 Connector Host 显式上报 unreachable 终态。

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

### OperationTask

最小字段建议：

- operationTaskId
- idempotencyKey
- organizationId
- environmentId
- connectorHostId
- instanceKey
- operationCode
- requestedBy
- requestedAtUtc
- executionStatus
- auditRecords
- attempts

### OperationAttempt / Lease

最小字段建议：

- attemptId
- leaseId
- connectorHostId
- status: started | completed | failed | abandoned
- leasedAtUtc
- leasedUntilUtc
- attemptNo
- maxAttempts
- abandonReason
- startedAtUtc
- finishedAtUtc
- failure: FailureReason?

约束：

1. claim 必须是原子操作；同一 OperationTask 在同一时刻只能有一个 status=started 的 active lease。
2. leaseId 是 heartbeat、abandon 和后续诊断的幂等令牌；不匹配当前 active lease 的更新必须拒绝。
3. leasedUntilUtc 到期后，下一次 claim 会先把过期 attempt 标记为 abandoned，abandonReason 固定为 lease-timeout；attemptNo 小于 maxAttempts 时任务回到 queued，否则任务转 failed。

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
- OperationClaimed
- OperationLeaseHeartbeat
- OperationLeaseAbandoned
- OperationCompleted
- OperationFailed
- AuditRecorded

## 幂等与跟踪约束

1. 注册和动作任务创建必须显式携带 idempotencyKey。
2. 动作结果通过 operationTaskId、attemptId、organizationId、environmentId 和 connectorHostId 关联任务范围；lease 续期或放弃还必须携带 leaseId。
3. 心跳、状态同步、任务创建和结果回传至少需要 correlationId 与 occurredAtUtc，用于日志关联和重复消息判定。
4. 平台日志与追踪统一使用 correlationId 和 W3C trace context 贯通，不在首批实现中自造第二套链路追踪协议。

## 首批实现范围

1. 立即实现 AppHub 的 registrations、heartbeats、state-snapshots 三个写接口。
2. 立即实现 PlatformGateway 的实例列表与实例详情两个查询接口。
3. 立即实现 Connector Host 通过 Nerv.IIP.Sdk.ConnectorProtocol 到 AppHub 的注册、心跳、状态同步客户端。
4. 立即实现 Ops 的 operation-tasks、operation-results、claim/heartbeat/abandon 和任务详情接口。
5. 立即实现 Connector Host 通过 Nerv.IIP.Sdk.Ops claim task、执行低风险 restart 并回传结果。

## 非目标

1. 不在本文档中定义全部命令下发传输形态；当前只冻结第二阶段采用的 HTTP claim/lease 拉取模型。
2. 不在本文档中定义全部动作参数 schema 与错误码表。
3. 不在本文档中定义 Windows Service Connector 与 HTTP Connector 的特有扩展字段。
4. 不在本文档中定义外部客户端注册、授权授予、令牌签发或 consent 页面细节，这些属于 IAM 边界。
