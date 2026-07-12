# 设备状态事件流

本文档补充 #233 的文档子任务，细化 #207 设备 IIoT 运行事实与 #206 APS lite 排程输入之间的事件驱动链路。边界以 `docs/adr/0014-aps-and-iiot-scheduling-boundary.md` 和 `docs/architecture/business-platform-domain-architecture.md` 为准。

## 结论

设备状态、报警、维修和排程可用性不是同一份事实在多个服务中的重复保存，而是从现场运行事实到维护处置事实，再到 MES/APS 可消费可用性投影的分层链路：

```text
IndustrialTelemetry.DeviceStateSnapshot
  -> IndustrialTelemetry runtime availability projection

IndustrialTelemetry.DeviceStateSnapshot
  -> industrialTelemetry.DeviceStateChanged
  -> Scheduling generated-plan invalidation

IndustrialTelemetry.AlarmEvent
  -> industrialTelemetry.AlarmRaised
  -> Maintenance.MaintenanceWorkOrder
  -> industrialTelemetry.AlarmCleared
  -> Maintenance.MaintenanceWorkOrder alarm-cleared marker
  -> maintenance.AssetUnavailable / maintenance.AssetRestored
  -> MES.WorkCenterUnavailability
  -> Scheduling resource availability / MES readiness
```

IndustrialTelemetry 拥有采集后的设备运行事实；Maintenance 拥有维修处置和资产不可用原因；MES 拥有生产执行侧的工作中心不可用投影；Scheduling/APS lite 拥有排程问题、排程方案和资源负载，不拥有报警、维修工单或 MES 执行事实。

## 事件链路

### 1. 报警事实进入 IndustrialTelemetry

IndustrialTelemetry 保存 `AlarmEvent`、设备状态快照、tag 映射和采集汇总。`DeviceStateSnapshot` 表达“某个设备资产在某个采集序列上的实时状态”。IndustrialTelemetry 的 runtime availability 查询直接从当前状态快照投影 `equipment.stateUnavailable` / `device-state` 窗口；同一状态快照在当前/latest 状态值变化时发布 `industrialTelemetry.DeviceStateChanged`，供跨服务消费者按事件幂等键处理。`AlarmEvent` 表达“某个设备资产在某个采集口径下发生了报警”这一运行事实，来源可以是 Connector Host、OPC UA、MQTT、SCADA adapter 或其他受控数据入口。

IndustrialTelemetry 只发布 `industrialTelemetry.DeviceStateChanged`、`industrialTelemetry.AlarmRaised`、`industrialTelemetry.AlarmCleared` 等公共集成事件。事件 payload 可携带状态快照 ID、设备资产 ID、当前状态、source sequence，或报警 ID、报警代码、严重度、发生时间、清除时间和 correlation 信息，但不得携带 PLC 控制指令、现场控制凭据、大体积时序数据或 SCADA 画面状态。OEE 只将设备状态事实值 `running` 视为 productive runtime；`standby`、`idle` 和 `ready` 可以表示 runtime availability 可用，但不计入 OEE productive running ticks。Business Console 既有 `availabilityRate` 字段名暂保持兼容，口径是 productive runtime rate；没有状态事实时该因子同样返回 `null`，不再以 `0` 冒充有效分母。MES 通过 `mes.ProductionReportRecorded` 把已分派设备、良品/报废/返工、单位与工序计划数量/标准工时快照的理论速率投影到 IndustrialTelemetry，绝不跨 schema 读取 MES；冲销报工复用原报工已持久化的 OEE 快照。性能率为总产出 ÷（productive runtime × 理论速率），质量率为良品 ÷（良品 + 报废 + 返工），OEE 为三项因子的乘积；状态、报工、统一单位、理论速率或 productive runtime 任一不足时，因子与 OEE 返回 `null` 并携带降级原因，不以 `1` 伪造完整值。`GET /api/business/v1/iiot/runtime-hours` 是 Maintenance 等内部消费者获取运行小时的服务边界，按 UTC 日拆分 productive runtime/loading hours，并仍以 `DeviceStateSnapshot` 为当前事实来源；长生命周期窗口会在服务端按 366 天分片查询以避免单次物化过大，#689 historian/聚合表仍负责后续更高效的历史承接。

### 2. Maintenance 消费报警并形成维修事实

Maintenance 通过 `Nerv.IIP.Contracts.IndustrialTelemetry` 消费 `industrialTelemetry.AlarmRaised`。当报警符合维修规则时，Maintenance 创建或关联 `MaintenanceWorkOrder`，表达“需要点检、维修、保养或故障处置”的维护事实。Maintenance 也消费 `industrialTelemetry.AlarmCleared`，按 `SourceAlarmId` 标记未完成工单的 `alarm_cleared` / `alarm_cleared_at_utc`，表示报警已恢复、仍待维修人员确认；该标记不会自动完成工单。

这不是把 `AlarmEvent` 复制到 Maintenance。报警是触发条件和可追溯来源，维修工单是维护域自己的事实，拥有处置状态、责任人、维护类型、备件需求、停机原因、报警恢复提示和恢复条件。Maintenance 不引用 IndustrialTelemetry 的 Domain、Infrastructure 或 Web 项目，也不写入 IndustrialTelemetry schema。

Maintenance 的预防性维护计划支持 day-interval 和运行小时触发：`next_due_on` 到期后由 `GenerateDueMaintenanceWorkOrdersCommand` 或默认关闭的 `MaintenancePlanDueScheduler` 创建计划来源工单，并推进下一次到期日；配置 `runtime_hour_interval` 后，Maintenance 通过 `IAssetRuntimeHoursProvider` 调用 IndustrialTelemetry runtime-hours 边界，累计运行小时跨过阈值时创建计划来源工单并推进下一运行小时阈值。调度器使用 `Maintenance:PmGeneration:TimeZoneId` 将当前 UTC 时间换算为工厂业务日；未配置时默认 UTC，配置值可使用 IANA 或 Windows timezone ID。provider 无真实遥测样本或不可用时不会消费阈值，会记录 warning 并等待下轮 tick 重试；状态触发仍属于后续 CBM/TPM 深化。

维修工单完工时，Maintenance 对每条备件行发布 `inventory.InventoryMovementRequested` 出库请求。该事件使用 `Nerv.IIP.Contracts.Inventory`，Inventory 负责幂等过账和库存扣减；Maintenance 仍不保存库存余额。P0 未建维修仓库主数据映射时，事件使用 `siteCode=maintenance`、`locationCode=maintenance-spares` 作为显式默认值，后续可由配置或 MasterData/WMS 仓库事实替换。

Maintenance 还暴露设备可靠性 P0 查询，用带 `SourceAlarmId` 的维修工单计算故障次数、已完成修复次数、MTTR 和 MTBF。当前 MTTR 使用有效维修段；MTBF 优先使用 IndustrialTelemetry productive runtime hours，遥测无样本或不可用时回退到 Maintenance availability windows 扣减后的近似运行小时，并通过响应字段标明来源。无故障样本时 MTBF 返回 `null`，无已完成修复样本时 MTTR 返回 `null`，避免把“无样本”误读为 0 小时/分钟。

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
| Maintenance | `MaintenanceWorkOrder`、保养计划、点检、故障、停机原因、资产恢复判定、维修备件用量事实 | `maintenance.AssetUnavailable`、`maintenance.AssetRestored`、`inventory.InventoryMovementRequested`、维修工单查询、可靠性 P0 查询 | 报警原始事实、设备主数据、MES 工单、APS 排程、库存余额 |
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

Scheduling/APS lite 消费可用性事实用于构造 `SchedulingProblem`：将不可用窗口、维护窗口、活动报警和恢复事件转成资源日历约束、冲突项或不可排原因。`industrialTelemetry.DeviceStateChanged` 不让 Scheduling 解释原始状态语义；它只作为输入失效信号，使引用该设备资产的 generated plan 标记为需要重排，实际可用性仍通过 EquipmentRuntime availability provider 读取标准 reason code。APS 输出的是方案和解释，不是现场控制命令。

Business Console 和甘特图只消费 MES/Scheduling/BusinessGateway 暴露的 DTO，展示可用性、冲突和调整意图。前端不计算正式排程，不绕过 MES 或 Maintenance 修改可用性事实。
