# 设备状态事件流

本文档补充 #233 的文档子任务，细化 #207 设备 IIoT 运行事实与 #206 APS lite 排程输入之间的事件驱动链路。边界以 `docs/adr/0014-aps-and-iiot-scheduling-boundary.md` 和 `docs/architecture/business-platform-domain-architecture.md` 为准。

## 结论

设备状态、报警、维修和排程可用性不是同一份事实在多个服务中的重复保存，而是从现场运行事实到维护处置事实，再到 MES/APS 可消费可用性投影的分层链路：

```text
IndustrialTelemetry.AlarmEvent
  -> industrialTelemetry.AlarmRaised
  -> Maintenance.MaintenanceWorkOrder
  -> maintenance.AssetUnavailable / maintenance.AssetRestored
  -> MES.WorkCenterUnavailability
  -> Scheduling resource availability / MES readiness
```

IndustrialTelemetry 拥有采集后的设备运行事实；Maintenance 拥有维修处置和资产不可用原因；MES 拥有生产执行侧的工作中心不可用投影；Scheduling/APS lite 拥有排程问题、排程方案和资源负载，不拥有报警、维修工单或 MES 执行事实。

## 事件链路

### 1. 报警事实进入 IndustrialTelemetry

IndustrialTelemetry 保存 `AlarmEvent`、设备状态快照、tag 映射和采集汇总。`AlarmEvent` 表达“某个设备资产在某个采集口径下发生了报警”这一运行事实，来源可以是 Connector Host、OPC UA、MQTT、SCADA adapter 或其他受控数据入口。

IndustrialTelemetry 只发布 `industrialTelemetry.AlarmRaised`、`industrialTelemetry.AlarmCleared` 等公共集成事件。事件 payload 可携带报警 ID、设备资产 ID、报警代码、严重度、发生时间、清除时间和 correlation 信息，但不得携带 PLC 控制指令、现场控制凭据、大体积时序数据或 SCADA 画面状态。

### 2. Maintenance 消费报警并形成维修事实

Maintenance 通过 `Nerv.IIP.Contracts.IndustrialTelemetry` 消费 `industrialTelemetry.AlarmRaised`。当报警符合维修规则时，Maintenance 创建或关联 `MaintenanceWorkOrder`，表达“需要点检、维修、保养或故障处置”的维护事实。

这不是把 `AlarmEvent` 复制到 Maintenance。报警是触发条件和可追溯来源，维修工单是维护域自己的事实，拥有处置状态、责任人、维护类型、备件需求、停机原因和恢复条件。Maintenance 不引用 IndustrialTelemetry 的 Domain、Infrastructure 或 Web 项目，也不写入 IndustrialTelemetry schema。

### 3. Maintenance 发布资产可用性事实

当维修处置、故障确认、保养窗口或人工停机判断影响生产能力时，Maintenance 发布：

1. `maintenance.AssetUnavailable`：资产在某个组织/环境和设备资产维度进入不可用状态；当前契约携带 `DeviceAssetId`、原因和开始时间。
2. `maintenance.AssetRestored`：同一资产或资源约束恢复可用。

这些事件表达维护域对资产可用性的判定，不等同于原始报警。一个报警可能不导致不可用；多个报警、点检结果或保养计划可能合并成一个不可用窗口；一个维修工单也可能经历多次不可用/恢复边界。

### 4. MES 投影为工作中心不可用

MES 消费 `maintenance.AssetUnavailable` 和 `maintenance.AssetRestored`，在 MES 边界内把设备资产解析到工作中心，并形成 `WorkCenterUnavailability` 投影。当前代码主要把该投影用于规则排程避让、产能影响查询和执行视图提示；#207/#206 后可以进一步把它接入 readiness 阻断和正式 APS 资源可用性约束。

`WorkCenterUnavailability` 是 MES 的执行侧投影，不是 MaintenanceWorkOrder 的副本。当前代码保存 MES 派工、工序执行、产能影响查询和生产现场看板需要的最小字段：`DeviceAssetId`、`WorkCenterId`、原因、开始时间和恢复时间。MES 不修改维修工单状态，不直接关闭报警，也不把 Maintenance 的内部处置字段复制进 MES。

当前 MES 还存在 `DeviceAssetWorkCenterMapping`，用于把 Maintenance 事件中的 `DeviceAssetId` 翻译成规则排程可用的 `WorkCenterId`。这只能视为 #233 跟踪的过渡映射/投影，不能升级为 MES 对设备归属主数据的所有权。设备资产、工作中心及二者归属关系的长期事实源应归 BusinessMasterData；#207 或后续 MasterData 接线应通过 resolve API 或 `masterData.DeviceAssetChanged` 等事件投影替代 MES 本地写入表。

### 5. Scheduling/APS 消费可用性约束

Scheduling/APS lite 在 #206 中拥有 `SchedulingProblem`、`SchedulePlan`、资源负载、冲突项、锁定任务和排程版本事实。它可以通过标准化可用性输入消费以下事实：

1. MasterData 的工作中心、设备资产、资源能力和日历。
2. DemandPlanning 的计划工单建议和 MES 的工单候选。
3. Maintenance 或 MES 投影出的资产/工作中心不可用窗口。
4. Quality、Inventory/WMS 等其他运行约束的事件投影。

APS 不直接读取 PLC/DCS/SCADA，不直接读取 IndustrialTelemetry 原始时序，不创建或更新 MaintenanceWorkOrder，也不直接改 MES OperationTask。排程方案发布后，由 MES 命令按方案落地执行域变化。

## 事实所有权

| 层 | 拥有事实 | 可发布/暴露 | 不拥有 |
| --- | --- | --- | --- |
| IndustrialTelemetry | `AlarmEvent`、`DeviceStateSnapshot`、tag 映射、采集汇总 | `industrialTelemetry.AlarmRaised`、`industrialTelemetry.AlarmCleared`、设备状态查询 | 维修工单、停机处置、工作中心不可用、排程方案 |
| Maintenance | `MaintenanceWorkOrder`、保养计划、点检、故障、停机原因、资产恢复判定 | `maintenance.AssetUnavailable`、`maintenance.AssetRestored`、维修工单查询 | 报警原始事实、设备主数据、MES 工单、APS 排程 |
| MES | `WorkOrder`、`OperationTask`、报工、停机记录、`WorkCenterUnavailability` 投影 | 生产 readiness、产能影响查询、MES 执行事件 | 维修工单处置、报警清除、排程算法、库存余额 |
| Scheduling / APS lite | `SchedulingProblem`、`SchedulePlan`、资源负载、冲突项、排程版本 | 排程结果、冲突解释、资源负载查询 | 设备报警、维修工单、MES 执行报工、现场控制 |

## 为什么不是冗余

1. **语义不同**：`AlarmEvent` 是设备运行事实，`MaintenanceWorkOrder` 是维护处置事实，`WorkCenterUnavailability` 是生产执行可用性投影，`SchedulePlan` 是排程决策事实。
2. **生命周期不同**：报警可能秒级清除，维修工单可能跨班次，MES 不可用窗口可能只覆盖生产资源，排程版本需要可复现和可审计。
3. **消费目标不同**：维修人员看处置，班组长看能否派工，排程员看资源约束，系统集成看事件可追溯。
4. **幂等和回放需要**：事件投影允许 MES 或 Scheduling 重建自己的 read model；跨服务写表会破坏回放、补偿和 ownership。

## 禁止跨服务写入的事实

以下事实不能由其他服务直接写入，即使它们消费了相关事件：

1. Maintenance 不写 `industrial_telemetry` schema，不补写、清除或修正 `AlarmEvent`。
2. IndustrialTelemetry 不创建、更新或关闭 `MaintenanceWorkOrder`。
3. MES 不写 Maintenance 工单、保养计划、点检记录、停机处置或备件需求。
4. Maintenance 不写 MES 工单、工序任务、报工、完工入库请求或 MES 停机记录。
5. Scheduling/APS 不写 IndustrialTelemetry、Maintenance 或 MES 的业务表；它只保存排程域事实。
6. BusinessGateway 只做 facade、权限检查和上下文透传，不持久化这些事件投影。

跨服务协作必须通过公共 contracts、IntegrationEvent、内部服务 API 或明确的 snapshot/resolve 契约完成，不允许跨 schema 外键、共享表或引用其他服务 Domain/Infrastructure 项目。

## Scheduling、APS、MES 的消费口径

MES 当前消费可用性事实用于规则排程避让、设备停机视图和产能影响查询。把不可用工作中心变成派工阻断、生产计划 readiness 问题或更细的执行工作流限制，属于 #207/#206 后续接线目标，不能把当前代码描述为已经具备完整阻断策略。MES 可以记录生产执行中实际发生的停机，但不能把该记录当作维修工单或报警原始事实。

Scheduling/APS lite 消费可用性事实用于构造 `SchedulingProblem`：将不可用窗口、维护窗口、活动报警和恢复事件转成资源日历约束、冲突项或不可排原因。APS 输出的是方案和解释，不是现场控制命令。

Business Console 和甘特图只消费 MES/Scheduling/BusinessGateway 暴露的 DTO，展示可用性、冲突和调整意图。前端不计算正式排程，不绕过 MES 或 Maintenance 修改可用性事实。
