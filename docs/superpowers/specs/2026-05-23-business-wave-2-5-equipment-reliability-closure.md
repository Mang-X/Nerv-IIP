# 业务第 2.5 波设备可靠性收口

## 背景

IndustrialTelemetry #129 和 Maintenance #130 原先被列为后续业务纵切。第二波之后，它们作为设备可靠性侧线波次完成，并在 ERP 之前注册到平台。本记录用于防止未来计划将其当作第三波中代码尚未开始的工作。

## 当前代码事实

| 服务 | 当前事实 | 验证 |
| --- | --- | --- |
| IndustrialTelemetry | `backend/services/Business/IndustrialTelemetry` 下已存在 Domain/Infrastructure/Web 服务；该服务负责遥测标签、设备状态快照、报警事件和遥测摘要。公开报警/状态事件契约位于 `backend/common/Contracts/Nerv.IIP.Contracts.IndustrialTelemetry`。 | `scripts/verify-business-industrial-telemetry-mvp.ps1` |
| Maintenance | `backend/services/Business/Maintenance` 下已存在 Domain/Infrastructure/Web 服务；该服务负责维修工单、计划、检查、停机原因和备件行。它通过公开契约消费 `industrialTelemetry.AlarmRaised`，并发布维修资产可用性契约。 | `scripts/verify-business-maintenance-mvp.ps1` |
| 设备可靠性聚合 | 两个服务都已加入 `backend/Nerv.IIP.sln`、Aspire AppHost 和就绪状态文档。本地端口为 5116 和 5117。 | `scripts/verify-business-equipment-reliability.ps1` |

## 边界决策

1. IndustrialTelemetry 不负责 PLC/DCS/SCADA 控制命令或凭据。
2. Maintenance 不负责设备主数据、Inventory 余额或 MES 工单状态。
3. MES 通过 `Nerv.IIP.Contracts.Maintenance` 消费 Maintenance 资产可用性事件，不得依赖 Maintenance 内部实现。
4. 后续加固中，由报警触发的工单创建必须按报警/来源引用保持幂等。

## 剩余后续事项

这些事项不会阻塞 ERP 第三波，但在全链路验收之前或期间仍需关注：

1. 确认重复报警处理不会创建重复维修工单。
2. 决定是否应将 Maintenance 工单已创建/已完成事件提升为公开契约。
3. 为“报警 -> 维修工单 -> 资产不可用/恢复 -> MES 排程约束”增加验收覆盖。
4. 在专用遥测存储规格形成之前，将高频遥测保留和原始时序数据存储排除在 MVP profile 之外。

## Issue 映射

| Issue | 状态 |
| --- | --- |
| #129 IndustrialTelemetry MVP | 已关闭；已实施。 |
| #130 Maintenance MVP | 已关闭；已实施。 |
| #77 全链路验收 | 必须验证设备到维修再到产能的链路。 |
