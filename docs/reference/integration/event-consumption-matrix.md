# 集成事件消费矩阵

本页索引当前公开/跨服务事件和关键业务本地事件的**生产者—消费者关系**。它用于回答“当前代码里谁发布、谁消费、这条关系如何分类”，不维护 Issue 完成状态、历轮扫描时间线或项目缺口总账。

分类与复核规则见 [`../../governance/integration/event-consumption.md`](../../governance/integration/event-consumption.md)。M2 拆分前的逐行审计快照保留在 [`../../reports/audits/integration-event-consumption-matrix-2026-08-28.md`](../../reports/audits/integration-event-consumption-matrix-2026-08-28.md)。

## Producer / consumer 证据

- 公开契约：`backend/common/Contracts/**`。
- 业务本地集成事件：`backend/services/Business/**/Application/IntegrationEvents`。
- 活动消费者：各服务 `Application/IntegrationEventHandlers`、`IntegrationEventConsumer` / `IIntegrationEventHandler` / `CapSubscribe` 实现与注册。
- 可靠性与副作用：对应 inbox/outbox、dead-letter、事务/幂等实现和行为测试。
- 跨服务事件信封、版本和幂等基线：ADR 0011。

Reference 与源码冲突时，以当前代码/契约/测试为准并修正本页。

## 当前关键跨服务关系

下表聚焦会改变其它服务状态或形成明确交接的当前关系；没有列出的 audit-only / producer-only 事件仍需按源码判断，不能因为未列在本页就推导“事件不存在”。

| 契约 / 事件 | 当前 producer | 当前内部 consumer / 交接 | 分类 |
| --- | --- | --- | --- |
| Approval `StepResolved` / `StepOverdue` / `ActionRecorded` | BusinessApproval | Notification | `consumed-internally` |
| Approval `ApprovalCompleted`（approved/rejected/returned） | BusinessApproval | ERP、Inventory；拒绝/结果通知由 Notification 消费 | `consumed-internally` |
| BarcodeLabel `BarcodeScanAccepted` | BarcodeLabel | 当前库存过账使用更窄的 Inventory movement 请求契约；本事件没有当前强制状态消费者 | `producer-only-until-feature` |
| DemandPlanning `PlanningSuggestionAccepted` | DemandPlanning | MES 消费已接受工单建议；ERP 消费指向采购申请的已接受采购建议 | `consumed-internally` |
| DemandPlanning `PlannedPurchaseSuggested` / `PlannedWorkOrderSuggested` | DemandPlanning | 下游实际交接使用 `PlanningSuggestionAccepted` | `deprecated/covered-by-other-contract` |
| ERP `PurchaseReceiptRecorded` | ERP | ERP GR/IR 处理、Quality 来料检验任务 | `consumed-internally` |
| ERP `SalesReturnAuthorized` | ERP | WMS 创建受质量门禁的客户退货入库单 | `consumed-internally` |
| ERP `SalesOrderReleased` / `SalesOrderChanged` / `SalesOrderCancelled` | ERP | DemandPlanning 维护销售订单需求投影 | `consumed-internally` |
| ERP `DeliveryOrderReleased` | ERP | 仓储交接使用公开 `wms.OutboundOrderRequested`，不直接消费该 ERP 事件 | `deprecated/covered-by-other-contract` |
| MasterData `SkuDisabled` | MasterData | MES 维护 SKU 可用性投影并门禁新计划/工单路径 | `consumed-internally` |
| MasterData `BusinessPartnerChanged` | MasterData | ERP 维护业务伙伴可用性投影 | `consumed-internally` |
| MasterData `ResourceChanged` | MasterData | Scheduling 使引用已变更工作中心资源的生成计划失效 | `consumed-internally` |
| MasterData `WorkCalendarChanged` | MasterData | Scheduling 按日历引用使相关生成计划失效 | `consumed-internally` |
| MasterData `DeviceAssetChanged` | MasterData | Maintenance 维护设备状态投影并暂停受影响 PM 计划 | `consumed-internally` |
| MasterData `ReferenceDataCodeChanged` | MasterData | 当前没有必须改变其它服务状态的活动消费者 | `producer-only-until-feature` |
| Ops `OperationTaskCompleted` / `OperationTaskFailed` | Ops | AppHub、Notification；IndustrialTelemetry 同步设备控制命令台账终态 | `consumed-internally` |
| Ops `OperationApprovalRequested` / `Approved` / `Rejected` | Ops | Notification；拒绝结果还推进 IndustrialTelemetry 控制命令台账 | `consumed-internally` |
| ProductEngineering `ProductionVersionCreated` | ProductEngineering | MES 为匹配且缺少生产版本的已创建工单建立绑定 | `consumed-internally` |
| ProductEngineering `BomReleased` / `RoutingReleased` / `EngineeringChangeReleased` | ProductEngineering | 当前主要由查询/解析边界消费，未形成必须的内部投影消费者 | `producer-only-until-feature` |
| Quality `DefectRaised` | MES / Quality 公共契约生产路径 | Quality 开启 NCR | `consumed-internally` |
| Quality `NcrDispositionDecided` | Quality | MES 更新缺陷处置状态 | `consumed-internally` |
| Quality `InspectionResult`（pass/conditional/reject） | Quality | Inventory、MES、Scheduling；RMA 场景下 ERP 处理退货财务结果 | `consumed-internally` |
| Quality `InspectionTaskOverdue` / `MeasuringDeviceCalibrationDue` | Quality | Notification | `consumed-internally` |
| MES `WorkOrderReleased` | MES | Scheduling 失效受影响计划；Quality 冻结周期巡检运行上下文 | `consumed-internally` |
| MES `WorkOrderCompleted` | MES | ERP 完成实际工单成本闭合/资本化等待逻辑 | `consumed-internally` |
| MES `OperationTaskCompleted` | MES | Quality 创建/闭合匹配的过程检验与周期巡检上下文 | `consumed-internally` |
| MES `OperationActualTimeSettled` | MES | ERP 按工序实绩结算人工成本并按 revision 幂等 | `consumed-internally` |
| WMS `WcsTaskRetryExhausted` | WMS | Notification 创建 critical WCS 任务通知 | `consumed-internally` |
| WMS `WcsTaskCancelled` | WMS | WMS WCS adapter 边界执行幂等取消交接 | `consumed-internally` |

## Audit / external-only 与 producer-only 的读法

以下情形**不能自动视为业务缺口**：

- 事件自身就是生命周期/审计事实，当前业务查询面已经拥有最终状态；
- 当前只需要外部扩展、分析或可观测性消费；
- 更窄的交接契约已经承担实际业务副作用；
- producer 暂时只为未来功能保留稳定事实，而当前下游使用查询/API/解析边界。

遇到没有消费者的事件时，先按 Governance 重新核对“是否真的需要内部副作用”，再决定是否建立 Issue。

## 业务服务本地事件

服务本地事件必须保持服务边界：没有公共信封和跨服务契约的本地事件不能因为出现在本矩阵就被其它服务直接依赖。当前本地事件查询从各服务 `Application/IntegrationEvents` 开始；若未来出现跨服务消费需求，先升级到受治理的公开契约，再登记跨服务 consumer。

## 更新要求

新增/删除/替换公开事件或活动消费者时，在同一变更中：

1. 核对 producer、consumer 注册与副作用实现；
2. 按 [`../../governance/integration/event-consumption.md`](../../governance/integration/event-consumption.md) 重新分类；
3. 更新本页受影响行，而不是追加日期化“已验证”时间线；
4. 需要保留一次性审计证据时新建冻结 Report，不把运行 ID/Issue 状态写入当前矩阵。
