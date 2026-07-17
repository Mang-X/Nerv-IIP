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
认证：首批 header-secret 兼容入口必须携带 `X-Connector-Host-Id`、`X-Connector-Secret`、`X-Organization-Id` 和 `X-Environment-Id`。AppHub 必须校验这些 header 与注册 body 的 organizationId、environmentId、connectorHostId 一致；若 AppHub 配置了 `ConnectorHostCredential:ConnectorHostId`、`ConnectorHostCredential:OrganizationId` 或 `ConnectorHostCredential:EnvironmentId`，还必须与服务端配置一致。
响应：返回 `registrationId`、`instanceKey` 和绑定该注册实例的短期 `ingestionToken`。`ingestionToken` 只用于后续同一 organization/environment/connectorHostId/instanceKey 的心跳与状态同步。

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

1. `ingestionToken` 由 AppHub 签发并绑定 registrationId、organizationId、environmentId、connectorHostId、instanceKey、issuedAtUtc 和 expiresAtUtc。
2. 心跳与状态同步不得再依赖请求 body 自证租户或实例身份；服务端必须校验 token 中的身份与 body 完全一致后才写入实例事实。
3. AppHub 必须拒绝已过期的 `ingestionToken`；Connector Host 在 token 过期或收到 401 后通过重新注册刷新本实例 token。
4. 非 Development 环境必须显式配置 `ConnectorIngestionToken:SigningKey`，不得使用仓库内本地 fallback；`ConnectorIngestionToken:LifetimeMinutes` 默认 10 分钟，可按部署风险下调。

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

## OPC UA 采集扩展（MAN-367 / #683）

OPC UA 采集器归属 `connector-hosts`，作为 Connector Host 的设备协议适配能力，不进入 AppHub、Ops、PlatformGateway 或业务服务内部实现。采样数据不通过 AppHub ingestion；Connector Host 仍复用 AppHub registration、heartbeat 和 state-snapshot 上报采集器实例及健康事实，采样 bucket 通过 IndustrialTelemetry 公开服务接口写入。

配置边界：

- `OpcUa:Enabled` 默认关闭。
- `OpcUa:EndpointUrl`、`SecurityPolicy`、`SecurityMode`、`BrowseRootNodeId` 描述 OPC UA 连接与浏览入口。
- `OpcUa:CredentialReference` 只保存凭据引用；留空/null 时使用匿名会话。首批实现支持 `env:<PREFIX>`，并从 `<PREFIX>_USERNAME` / `<PREFIX>_PASSWORD` 读取 OPC UA 用户名密码；配置了 `env:<PREFIX>` 但环境变量缺失时会 fail-fast，避免静默降级为匿名。仓库配置不得保存设备用户名、密码、证书私钥或客户密钥。
- `OpcUa:Tags[]` 绑定 `DeviceAssetId`、`TagKey`、OPC UA `NodeId`、采样间隔和 bucket 秒数。

采样写入约束：

- Connector Host 订阅 OPC UA tag 节点并把通知按 tag bucket 聚合为 `SampleCount`、`MinValue`、`MaxValue` 和 `AverageValue`。
- 写入 `POST /api/business/v1/iiot/samples` 时必须携带 `source_system=opcua`、`source_connector={connectorHostId}/{connectorId}` 和稳定 `source_sequence=opcua:{connectorId}:{tagKey}:{bucketStartUnixMilliseconds}`，由 IndustrialTelemetry 侧已有幂等约束处理重复 bucket。
- 断线重连、订阅恢复、丢样计数和最近采样/写入时间通过 state-snapshot 的 metadata/metrics 暴露，heartbeat 仍只证明 Connector Host 存活。

首批自动化验证可使用 Connector Host fake OPC UA adapter 覆盖浏览、订阅、bucket 写入、source_sequence 幂等字段和重连/丢样指标；合入或发布前仍应优先补充 open62541、Prosys 或等价 OPC UA 模拟器 smoke。若 Docker 或外部模拟器不可用，交付说明必须明确限制与替代证据。

## Modbus TCP / MQTT 采集扩展（MAN-368 / #684）

Modbus TCP 与 MQTT 采集器同样归属 `connector-hosts`，复用 Connector Host 的实例发现、heartbeat、state-snapshot 和 IndustrialTelemetry `POST /api/business/v1/iiot/samples` 写入边界。它们不进入 AppHub、Ops、PlatformGateway 或业务服务内部实现，也不保存现场控制凭据。

配置边界：

- `Modbus:Enabled` 与 `Mqtt:Enabled` 默认关闭。
- `Modbus:Endpoint` 描述 PLC/仪表 TCP endpoint；`Modbus:Registers[]` 绑定 `DeviceAssetId`、`TagKey`、unit id、寄存器表、地址、寄存器数量、`DataType`、`WordOrder`、scale/offset 和 bucket 秒数。首批 `DataType` 支持 `UInt16`、`Int16`、`UInt32`、`Int32`、`Float32`；`WordOrder` 支持 `BigEndian` 与 32 位值的 `LittleEndian` word-swap。
- `Mqtt:Broker`、`ClientId`、`TopicMappings[]` 描述 broker 连接、topic filter 和 JSON path 到 tag 的映射；首批 JSON path 支持 `$.a.b` 形式的对象属性路径。
- `Mqtt:CredentialReference` 只保存凭据引用；首批支持 `env:<PREFIX>`，并从 `<PREFIX>_USERNAME` / `<PREFIX>_PASSWORD` 读取 broker 用户名密码。仓库配置不得保存 broker 密码、设备密码或客户密钥。

采样写入约束：

- Modbus TCP 轮询配置化寄存器地址表，支持 holding/input registers，采样值按 scale/offset 归一化后进入 bucket 聚合。
- MQTT 订阅配置化 topic filter，按 topic + JSON path 解析 payload 数值后进入 bucket 聚合。
- 写入 `POST /api/business/v1/iiot/samples` 时必须携带 `source_system=modbus|mqtt`、`source_connector={connectorHostId}/{connectorId}` 和稳定 `source_sequence={protocol}:{connectorId}:{tagKey}:{bucketStartUnixMilliseconds}`，由 IndustrialTelemetry 侧已有幂等约束处理重复 bucket。
- 连接失败、订阅失败、丢样计数和最近采样/写入时间通过 state-snapshot 的 metadata/metrics 暴露，heartbeat 仍只证明 Connector Host 存活。

首批自动化验证覆盖 Modbus 模拟采样入库、MQTT broker/payload 映射采样入库、点位映射配置化、下游写入失败后的 bucket 恢复与稳定 `source_sequence`。真实现场联调仍应在部署 profile 中提供 endpoint/broker 与凭据引用，不得把凭据写入仓库。

## Canonical identity、连接状态与 tag manifest 扩展（#947 / #951）

### Canonical collection connector identity

`collectionConnectorId` 是 organization/environment 内一条采集连接器配置的稳定身份。完整能力 profile 的 Connector Host 配置显式提供该值，并把同一值用于 AppHub `instanceKey`、collection health `connectorId`、tag manifest、telemetry sample、BusinessGateway route 和 Business Console 查询。当前 adapters 为旧配置保留 deterministic derived-ID fallback，但该 fallback 只是同协议 V1 的迁移兼容，不是历史数据关联或跨配置重命名机制。`sourceConnector` 继续保留为向后兼容的来源诊断文本，不是 join key；既有 telemetry 行保持 nullable `collection_connector_id`，不得从旧文本猜测或回填。显式 ID 变化表示新连接器身份，不自动合并历史。

### 四个独立事实面

1. **Host liveness**：AppHub heartbeat 只证明 Connector Host 进程存活。
2. **Field connection**：协议适配器上报 `unknown`、`alive` 或 `lost` 及独立 `observedAtUtc`；alive 带 `connectedSinceUtc`，lost 带 `disconnectedSinceUtc`，可附有界 reason category 和脱敏 diagnostic code。
3. **Collector health**：采集循环自己的 reported/health status、counter epoch、received/dropped/error counters 和 last sample。
4. **Tag sample presence**：IndustrialTelemetry summary 只说明一个 current manifest binding 是否曾有 sample，以及 first/last sample time。

四轴分别排序和持久化。sample silence、collector error 或 Host heartbeat 不能伪造 field `lost`；field `lost`、Host timeout、collector terminal failure 的读面优先级分别产生 `field-connection`、`host-liveness` 或 fault，但原始轴仍同时返回。旧 Host 没有 connection object 时保持 null/unknown，不历史回填。

受治理 profile 固定为 heartbeat 2 秒、field probe 4 秒、AppHub Host liveness timeout 6 秒、backend deadline 不超过 8 秒，Business Console 每 10 秒轮询。Connector Host 与 AppHub 对超出这些边界的配置执行启动校验；采样 bucket 周期可以更长，但不能替代 4 秒的协议连接探测。

### Connector tag manifest

Connector Host 通过 IndustrialTelemetry 内部 endpoint `POST /api/business/v1/iiot/connector-tag-manifests`（operation `reportBusinessIiotConnectorTagManifest`）上报 replace-style current manifest。`manifestRevision` 是配置 shape 的 canonical SHA-256；`manifestObservedAtUtc`/exact ticks 决定 definition 顺序。逐 tag activation 使用独立 observation，因此 pending/active/error/disabled 变化不改变 revision。重启、配置变化和 rebirth request 会触发上报；未确认 payload 以有界 exponential backoff 重试。

这些字段是 Connector Protocol V1 的 additive compatibility extension，没有伪造新的协议主版本。完整 connection/manifest 体验的最低兼容条件是 Host build 同时支持显式 `collectionConnectorId`、connection object、manifest report/retry 和 sample `collectionConnectorId`；更早的 V1 Host 继续注册和采样，但读面会保守显示 connection unknown 与 manifest unavailable。当前仓库没有独立可引用的 Connector Host 小版本号，因此文档不编造数值版本门槛。

IndustrialTelemetry 通过 `GET /api/business/v1/iiot/connectors/{collectionConnectorId}/tag-coverage`（operation `getBusinessIiotConnectorTagCoverage`）从 current bindings 出发并 LEFT JOIN `telemetry_summaries`。它不扫描 raw historian、不使用 device-control bindings，也不把 sample presence 推断成 quality/freshness。旧 Host 从未上报 manifest 时返回 `manifestStatus=unavailable`；current manifest 的零 bindings 才表示真实空配置。

## 非目标

1. 不在本文档中定义全部命令下发传输形态；当前只冻结第二阶段采用的 HTTP claim/lease 拉取模型。
2. 不在本文档中定义全部动作参数 schema 与错误码表。
3. 不在本文档中定义 Windows Service Connector 与 HTTP Connector 的特有扩展字段。
4. 不在本文档中定义外部客户端注册、授权授予、令牌签发或 consent 页面细节，这些属于 IAM 边界。
