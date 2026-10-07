# 设备状态事件流

本文只描述设备运行事实从 **IndustrialTelemetry → Maintenance → MES → Scheduling** 的当前所有权、事件和投影边界。M2-L 清理前含 issue 阶段状态的原文冻结于 [`../../reports/m2-l-equipment-status-event-flow-pre-clean-2026-09-07.md`](../../reports/m2-l-equipment-status-event-flow-pre-clean-2026-09-07.md)。长期 APS/IIoT 边界见 [ADR 0014](../../adr/0014-aps-and-iiot-scheduling-boundary.md)。

## 总体链路

```text
IndustrialTelemetry.DeviceStateSnapshot
  -> industrialTelemetry.DeviceStateChanged
  -> runtime availability / Scheduling invalidation input

IndustrialTelemetry.AlarmEvent
  -> industrialTelemetry.AlarmRaised / AlarmCleared
  -> Maintenance.MaintenanceWorkOrder
  -> maintenance.AssetUnavailable / AssetRestored
  -> MES.WorkCenterUnavailability
  -> Scheduling resource availability / MES readiness
```

IndustrialTelemetry 拥有采集后的设备运行事实；Maintenance 拥有维修处置和资产不可用原因；MES 拥有生产执行侧工作中心不可用投影；Scheduling 拥有排程问题、方案、资源负载和冲突，不拥有报警、维修工单或 MES 执行事实。

## IndustrialTelemetry：运行事实

`DeviceStateSnapshot` 是设备当前状态的权威运行事实。状态变化可发布 `industrialTelemetry.DeviceStateChanged`，供跨域消费者按稳定事件身份幂等投影；`AlarmEvent` 表达受控采集入口观察到的报警事实，并发布 `industrialTelemetry.AlarmRaised` / `industrialTelemetry.AlarmCleared`。

事件可以携带设备资产引用、状态/报警代码、严重度、来源序列、发生/清除时间和 correlation/causation，但不得携带 PLC 控制指令、控制凭据、大体积时序样本或 SCADA 画面状态。

运行时可用性可以从当前 `DeviceStateSnapshot` 投影；`running` 是 OEE 生产性运行时间，`standby` / `idle` / `ready` 可以表示资源可用但不计入生产性运行时间。缺少状态、报工、统一单位、理论速率或有效运行时间时，OEE 相关因子保持未知/降级，不以 0 或 1 伪造完整值。

Maintenance 等内部消费者通过 IndustrialTelemetry 的公开运行小时边界读取生产性运行小时；长期历史存储和聚合实现不能改变 `DeviceStateSnapshot` 对当前运行状态的事实所有权。

## Maintenance：维修与资产可用性

Maintenance 消费报警事件，在符合维护规则时创建或关联 `MaintenanceWorkOrder`。报警恢复可以标记工单来源报警已清除，但不能自动完成维修工单：报警事实与维修处置事实是两种不同所有权。

Maintenance 还拥有预防性维护计划、点检、故障、停机原因、备件需求和资产恢复判定。运行小时阈值由 IndustrialTelemetry 公开边界提供；provider 无真实遥测或不可用时不得消费阈值或伪造运行小时。

维修工单完工产生备件消耗意图时，Maintenance 发布公开 Inventory movement request；Inventory 负责库存幂等过账，Maintenance 不保存库存余额。

### 资产不可用事件

- `maintenance.AssetUnavailable` 表达维护域判定某资产进入生产不可用状态。
- `maintenance.AssetRestored` 表达同一资产恢复可用。
- 当前 v1/v2 不可用契约可以共存，但必须共享稳定业务幂等语义；v2 `reasonCode` 是受控目录引用，legacy `reason` 只承担兼容语义。
- 多个报警可以汇聚为一个不可用窗口；单个报警不必然导致不可用；单个工单也可以跨越多个不可用/恢复边界。

事件版本兼容不改变 Maintenance 对维修处置和可用性判定的所有权，也不允许消费者把报警原始事实复制为自己的主事实。

### 预计恢复与停机 MTTR

按 [ADR 0032 §2](../../adr/0032-scheduling-local-rescheduling-unfreeze.md)，Maintenance 保存维修人员显式填写的 ETR；创建与在途更新事件只携带这个显式输入，无显式输入时保持 null。预测更新沿用原停机开始时间，使用工单修订区分更新事实；完工或取消才形成实际恢复，预测不会解除停机。

Maintenance owner 查询优先使用显式 ETR，否则取同组织、环境、设备的全历史已结束停机持续时间算术平均，无样本时取维护服务配置的默认时长。样本开始来自实际停机，结束来自完工或取消；报警清除不算结束，维修劳动时长不替代停机持续时间。查询结果包含选定预测、依据与该次来源输入的稳定版本；派生预测不写回显式 ETR 列。配置入口与默认值以 Maintenance `RestorePrediction` options 和 `appsettings.json` 为准。

## MES：执行侧工作中心投影

MES 消费 Maintenance 的不可用/恢复事实，把设备资产解析到工作中心并维护 `WorkCenterUnavailability`。该投影只保存生产执行、派工避让、readiness 和产能影响所需的最小字段，例如设备资产引用、工作中心、原因、开始/恢复时间。

`WorkCenterUnavailability` 不是 `MaintenanceWorkOrder` 副本。MES 不修改维修工单、不关闭报警，也不取得设备主数据所有权。

设备资产到工作中心的归属由 BusinessMasterData 拥有，MES 不保存本地映射表。MES 在消费停机事件和遥测计数时，直接向 MasterData 资源读面查询设备当前归属的工作中心，主数据改了归属，下一次事件就按新归属生效。主数据暂时不可达时，消费者抛出并交由消息系统重试，不把它当成「没有归属」。主数据里查不到该设备的工作中心时，不得扩大为任意工作中心，也不得以设备编号冒充工作中心：停机事实跳过并记警告，遥测候选挂起为「设备未绑定工作中心」。

## Scheduling / APS：标准化约束消费

Scheduling 的输入可包括：

1. MasterData 的工作中心、设备资产、资源能力和日历；
2. DemandPlanning 计划建议与 MES 工单候选；
3. Maintenance/MES 形成的资产或工作中心不可用窗口；
4. Inventory/WMS 齐套、Quality 阻断等其它标准化运行约束投影。

Scheduling 不直接读取 PLC/DCS/SCADA、IndustrialTelemetry 原始时序或 Maintenance 数据库，不创建/更新 MaintenanceWorkOrder，也不直接修改 MES OperationTask。排程方案发布后由 MES 的受控命令落地执行域变化。

## 事实所有权

| 层 | 拥有事实 | 可发布/暴露 | 不拥有 |
| --- | --- | --- | --- |
| IndustrialTelemetry | AlarmEvent、DeviceStateSnapshot、tag 映射、采集汇总、运行小时/OEE 输入 | DeviceStateChanged、AlarmRaised、AlarmCleared、运行状态/小时查询 | 维修工单、停机处置、工作中心不可用、排程方案 |
| Maintenance | MaintenanceWorkOrder、计划/点检/故障、停机原因、资产恢复、备件需求事实 | AssetUnavailable、AssetRestored、Inventory movement request、可靠性/维护查询 | 报警原始事实、设备主数据、MES 工单、APS 排程、库存余额 |
| MES | WorkOrder、OperationTask、报工、停机记录、WorkCenterUnavailability 投影 | readiness、产能影响、MES 执行事件 | 维修处置、报警清除、排程算法、库存余额 |
| Scheduling | SchedulingProblem、SchedulePlan、资源负载、冲突、锁定任务、排程版本 | 排程方案/冲突/发布事实 | 原始遥测、维修工单、MES 执行、库存余额 |
| BusinessMasterData | DeviceAsset、WorkCenter 与静态资源归属/能力 | resolve API、资源变更事件 | 运行状态、报警、维修执行、排程方案 |

## Fail-closed 不变量

1. 原始遥测、维修处置、MES 投影和 APS 约束必须保持分层，不通过跨库读取“快捷合并”。
2. 设备/工作中心关系缺失或冲突时，消费者不得扩大可用资源范围。
3. v1/v2 双契约或重复投递必须以同一业务幂等事实收敛，不产生两条不可用窗口。
4. 报警清除不等于维修完成；资产恢复必须由拥有可用性判定的维护流程显式形成事实。
5. Gateway/UI 对状态的聚合或展示不改变上述 owner。
