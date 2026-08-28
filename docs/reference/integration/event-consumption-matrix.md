# 集成事件消费矩阵

本页索引当前公开/跨服务事件和关键业务本地事件的**生产者—消费者关系**。它用于回答“当前代码里谁发布、谁消费、这条关系如何分类”，不维护 Issue 完成状态、历轮扫描时间线或项目缺口总账。

分类与复核规则见 [`../../governance/integration/event-consumption.md`](../../governance/integration/event-consumption.md)。M2 拆分前包含逐轮 provenance 与实现说明的完整审计快照保留在 [`../../reports/audits/integration-event-consumption-matrix-2026-08-28.md`](../../reports/audits/integration-event-consumption-matrix-2026-08-28.md)。

## Producer / consumer 证据

- 公开契约：`backend/common/Contracts/**`。
- 业务本地集成事件：`backend/services/Business/**/Application/IntegrationEvents`。
- 活动消费者：各服务 `Application/IntegrationEventHandlers`、`IntegrationEventConsumer` / `IIntegrationEventHandler` / `CapSubscribe` 实现与注册。
- 可靠性与副作用：对应 inbox/outbox、dead-letter、事务/幂等实现和行为测试。
- 跨服务事件信封、版本和幂等基线：ADR 0011。

Reference 与源码冲突时，以当前代码/契约/测试为准并修正本页。下表保留“当前有无活动消费方、交接到谁、如何分类”这一查询能力；重放、锁、revision、历史 Issue、日期化验证过程等实现细节回到源码/测试或冻结 Audit。

## 公开契约矩阵

| 契约域 | 事件 | 当前 producer | 当前内部 consumer / 交接 | 分类 |
| --- | --- | --- | --- | --- |
| Approval | `ApprovalStartedIntegrationEvent` | BusinessApproval | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| Approval | `ApprovalStepResolvedIntegrationEvent` | BusinessApproval | Notification | `consumed-internally` |
| Approval | `ApprovalStepOverdueIntegrationEvent` | BusinessApproval | Notification | `consumed-internally` |
| Approval | `ApprovalActionRecordedIntegrationEvent` | BusinessApproval | Notification | `consumed-internally` |
| Approval | `ApprovalCompletedIntegrationEvent`（approved/rejected/returned） | BusinessApproval | ERP、Inventory；Notification 消费相关结果/拒绝通知 | `consumed-internally` |
| BarcodeLabel | `BarcodeScanAcceptedIntegrationEvent` | BarcodeLabel | 当前库存副作用通过 `InventoryMovementRequestedIntegrationEvent` 交接；本事件没有强制状态消费者 | `producer-only-until-feature` |
| DemandPlanning | `MrpRunCompletedPayload` / `DemandPlanningIntegrationEvent` | DemandPlanning | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| DemandPlanning | `PlanningSuggestionPayload` / `PlannedPurchaseSuggested` | DemandPlanning | ERP 实际采购交接使用已接受建议事件 | `deprecated/covered-by-other-contract` |
| DemandPlanning | `PlanningSuggestionPayload` / `PlannedWorkOrderSuggested` | DemandPlanning | MES 实际工单交接使用已接受建议事件 | `deprecated/covered-by-other-contract` |
| DemandPlanning | `PlanningSuggestionAcceptedIntegrationEvent` | DemandPlanning | MES；指向采购申请的建议由 ERP 消费 | `consumed-internally` |
| ERP | `PurchaseRequisitionCreatedPayload` | ERP | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| ERP | `PurchaseOrderReleasedPayload` | ERP | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| ERP | `PurchaseReceiptRecordedIntegrationEvent` | ERP | ERP GR/IR 处理；Quality 来料检验 | `consumed-internally` |
| ERP | `SalesReturnAuthorizedIntegrationEvent` | ERP | WMS | `consumed-internally` |
| ERP | `SalesOrderReleased` / `SalesOrderChanged` / `SalesOrderCancelled` | ERP | DemandPlanning 销售订单需求投影 | `consumed-internally` |
| ERP | `DeliveryOrderReleasedPayload` | ERP | 实际仓储交接使用公开 `wms.OutboundOrderRequested` | `deprecated/covered-by-other-contract` |
| ERP | `AccountPayableCreatedPayload` | ERP | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| ERP | `AccountReceivableCreatedPayload` | ERP | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| ERP | `CostCandidateCreatedPayload` | ERP | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| ERP | `JournalVoucherPostedPayload` | ERP | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| ERP | `WorkOrderCostCapitalizedIntegrationEvent` | ERP | MES | `consumed-internally` |
| IndustrialTelemetry | `DeviceStateChangedIntegrationEvent` | IndustrialTelemetry | Scheduling；MES 消费规范化运行时可用性查询而非直接解释本事件 | `consumed-internally` |
| IndustrialTelemetry | `AlarmRaisedIntegrationEvent` | IndustrialTelemetry | Maintenance、Notification | `consumed-internally` |
| IndustrialTelemetry | `AlarmClearedIntegrationEvent` | IndustrialTelemetry | Maintenance、Notification | `consumed-internally` |
| IndustrialTelemetry | `AlarmEscalatedIntegrationEvent` | IndustrialTelemetry | Notification | `consumed-internally` |
| IndustrialTelemetry | `TelemetryProductionCountDeltaIntegrationEvent` | IndustrialTelemetry | MES | `consumed-internally` |
| Inventory | `InventoryMovementRequestedIntegrationEvent` | MES、WMS、BarcodeLabel、Maintenance、ERP 邻接流程 | Inventory | `consumed-internally` |
| Inventory | `InventoryReservationReleaseRequestedIntegrationEvent` | MES | Inventory | `consumed-internally` |
| Inventory | `InventoryReservationExpiredIntegrationEvent` | Inventory | WMS、MES | `consumed-internally` |
| Inventory | `StockMovementPostedIntegrationEvent` | Inventory | WMS、MES、ERP | `consumed-internally` |
| Inventory | `StockMovementPostingFailedIntegrationEvent` | Inventory | WMS | `consumed-internally` |
| Inventory | `StockCountVarianceConfirmedIntegrationEvent` | Inventory | 当前无必须改变平台状态的活动消费者 | `producer-only-until-feature` |
| Inventory | `StockAvailabilityChangedIntegrationEvent` | Inventory | Scheduling | `consumed-internally` |
| Maintenance | `AssetUnavailableIntegrationEvent` | Maintenance | MES、Scheduling | `consumed-internally` |
| Maintenance | `AssetRestoredIntegrationEvent` | Maintenance | MES、Scheduling | `consumed-internally` |
| MasterData | `SkuChangedIntegrationEvent` | MasterData | 当前下游主要使用 API/快照；无活动状态消费者 | `producer-only-until-feature` |
| MasterData | `SkuDisabledIntegrationEvent` | MasterData | MES | `consumed-internally` |
| MasterData | `UnitOfMeasureChangedIntegrationEvent` | MasterData | 当前下游主要使用 API/快照；无活动状态消费者 | `producer-only-until-feature` |
| MasterData | `BusinessPartnerChangedIntegrationEvent` | MasterData | ERP | `consumed-internally` |
| MasterData | `ResourceChangedIntegrationEvent` | MasterData | Scheduling | `consumed-internally` |
| MasterData | `WorkCalendarChangedIntegrationEvent` | MasterData | Scheduling | `consumed-internally` |
| MasterData | `DeviceAssetChangedIntegrationEvent` | MasterData | Maintenance | `consumed-internally` |
| MasterData | `ReferenceDataCodeChangedIntegrationEvent` | MasterData | 当前无必须改变平台状态的活动消费者 | `producer-only-until-feature` |
| Ops | `OperationTaskCompletedIntegrationEvent` | Ops | AppHub、Notification、IndustrialTelemetry | `consumed-internally` |
| Ops | `OperationTaskFailedIntegrationEvent` | Ops | AppHub、Notification、IndustrialTelemetry | `consumed-internally` |
| Ops | `OperationTaskRequestedIntegrationEvent` | Ops | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| Ops | `OperationApprovalRequestedIntegrationEvent` | Ops | Notification | `consumed-internally` |
| Ops | `OperationApprovalApprovedIntegrationEvent` | Ops | Notification | `consumed-internally` |
| Ops | `OperationApprovalRejectedIntegrationEvent` | Ops | Notification、IndustrialTelemetry | `consumed-internally` |
| Ops | `OperationTaskClaimedIntegrationEvent` | Ops | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| Ops | `AuditRecordedIntegrationEvent` | Ops | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| ProductEngineering | `BomReleasedIntegrationEvent` | ProductEngineering | 当前下游主要通过查询/解析边界使用 | `producer-only-until-feature` |
| ProductEngineering | `RoutingReleasedIntegrationEvent` | ProductEngineering | 当前下游主要通过查询/解析边界使用 | `producer-only-until-feature` |
| ProductEngineering | `ProductionVersionCreatedIntegrationEvent` | ProductEngineering | MES | `consumed-internally` |
| ProductEngineering | `EngineeringChangeReleasedIntegrationEvent` | ProductEngineering | 当前下游主要通过查询/解析边界使用 | `producer-only-until-feature` |
| Quality | `DefectRaisedIntegrationEvent` | MES / Quality 公共契约生产路径 | Quality | `consumed-internally` |
| Quality | `NcrOpenedIntegrationEvent` | Quality | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| Quality | `NcrDispositionDecidedIntegrationEvent` | Quality | MES | `consumed-internally` |
| Quality | `NcrClosedIntegrationEvent` | Quality | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| Quality | `InspectionResultIntegrationEvent`（passed/conditional/rejected） | Quality | Inventory、MES、Scheduling；RMA 场景下 ERP 处理相应财务结果 | `consumed-internally` |
| Quality | `InspectionTaskOverdueIntegrationEvent` | Quality | Notification | `consumed-internally` |
| Quality | `MeasuringDeviceCalibrationDueIntegrationEvent` | Quality | Notification | `consumed-internally` |
| MES | `WorkOrderReleasedIntegrationEvent` | MES | Scheduling、Quality | `consumed-internally` |
| MES | `WorkOrderCompletedIntegrationEvent` | MES | ERP | `consumed-internally` |
| MES | `WorkOrderClosedIntegrationEvent` | MES | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| MES | `MesOperationTaskCompletedIntegrationEvent` | MES | Quality | `consumed-internally` |
| MES | `MesOperationActualTimeSettledIntegrationEvent` | MES | ERP | `consumed-internally` |
| MES | `MesOperationActualTimeSettlementVoidedIntegrationEvent` | MES | ERP | `consumed-internally` |

MES 工序工时结算事件按 ADR 0011 并行发布：上述 V1 契约继续供当前 ERP 消费，且禁止携带机器事实；独立的 `MesOperationActualTimeSettledV2IntegrationEvent` 与 `MesOperationActualTimeSettlementVoidedV2IntegrationEvent` topic 携带满足状态组合约束的完整冻结机器工时事实，当前无仓库内消费者。V2 的 `available` 可携带真实零值，且当前唯一 basis 为 `single-device-active-minus-explicit-pause-v1`；无设备或执行中设备变化为 `unavailable` 且不写零，`notApplicable` 仅来自显式业务判定，未知状态字符串失败关闭。V1 在 ERP 完成升级且 replay/DLQ 处置完成前不得退役；本项不扩展 ERP 费率或机器成本。
| MES | `MesOperationTaskManuallyDispatchedIntegrationEvent` | MES | Scheduling | `consumed-internally` |
| MES | `MesOperationTaskManualDispatchClearedIntegrationEvent` | MES | Scheduling | `consumed-internally` |
| MES | `ProductionReportRecordedIntegrationEvent` | MES | IndustrialTelemetry、ERP、Quality | `consumed-internally` |
| MES | `MesMaterialIssueRequestedIntegrationEvent` | MES | WMS | `consumed-internally` |
| MES | `FinishedGoodsReceiptRequestedIntegrationEvent` | MES | Quality | `consumed-internally` |
| Scheduling | `SchedulePlanGenerated` / `SchedulingIntegrationEvent` | Scheduling | 当前无必须改变平台状态的活动消费者 | `producer-only-until-feature` |
| Scheduling | `ScheduleConflictDetectedIntegrationEvent` | Scheduling | Notification | `consumed-internally` |
| Scheduling | `SchedulePlanReleasedIntegrationEvent` | Scheduling | MES | `consumed-internally` |
| Scheduling | `SchedulePlanRevokedIntegrationEvent` | Scheduling | MES | `consumed-internally` |
| Scheduling | `SchedulePlanInvalidatedIntegrationEvent` | Scheduling | MES、Notification | `consumed-internally` |
| WMS | `InboundOrderCompleted` / `WmsIntegrationEvent` | WMS | ERP、Quality | `consumed-internally` |
| WMS | `OutboundOrderCompleted` / `WmsIntegrationEvent` | WMS | ERP | `consumed-internally` |
| WMS | `OutboundOrderCancelled` / `WmsIntegrationEvent` | WMS | ERP | `consumed-internally` |
| WMS | `WmsOutboundOrderRequestedIntegrationEvent` | ERP | WMS | `consumed-internally` |
| WMS | `WmsMaterialIssueOutboundPreparedIntegrationEvent` | WMS | MES | `consumed-internally` |
| WMS | `CountExecutionCompleted` / `WmsIntegrationEvent` | WMS | 当前无必须改变平台状态的活动消费者 | `producer-only-until-feature` |
| WMS | `WcsTaskDispatched` / `WmsIntegrationEvent` | WMS | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| WMS | `WcsTaskFailed` / `WmsIntegrationEvent` | WMS | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| WMS | `WcsTaskRetryExhausted` / `WmsIntegrationEvent` | WMS | Notification | `consumed-internally` |
| WMS | `WcsTaskCompleted` / `WmsIntegrationEvent` | WMS | 当前无必须改变平台状态的活动消费者 | `audit-or-external-only` |
| WMS | `WcsTaskCancelled` / `WmsIntegrationEvent` | WMS | WMS WCS adapter 边界 | `consumed-internally` |

## 业务服务本地事件

没有公共信封和跨服务契约的本地事件不能因为出现在本矩阵就被其它服务直接依赖。

| 服务 | 本地事件 | 当前跨服务消费情况 | 分类 |
| --- | --- | --- | --- |
| BarcodeLabel | `LabelPrintBatchCreatedIntegrationEvent` | 无跨服务状态消费者 | `audit-or-external-only` |
| BarcodeLabel | `LabelPrintBatchCompletedIntegrationEvent` | 无跨服务状态消费者 | `audit-or-external-only` |
| BarcodeLabel | `LabelScannedIntegrationEvent` | 公开扫描交接使用 `BarcodeScanAcceptedIntegrationEvent`，库存副作用使用 `InventoryMovementRequestedIntegrationEvent` | `deprecated/covered-by-other-contract` |
| BarcodeLabel | `ScanRejectedIntegrationEvent` | 无跨服务状态消费者 | `audit-or-external-only` |
| Maintenance | `MaintenanceWorkOrderOpenedIntegrationEvent` | 公开产能影响使用 `AssetUnavailableIntegrationEvent` | `audit-or-external-only` |
| Maintenance | `MaintenanceWorkOrderCompletedIntegrationEvent` | 公开恢复/产能影响使用 `AssetRestoredIntegrationEvent` | `deprecated/covered-by-other-contract` |

若本地事件未来需要跨服务消费，先升级到受治理的公开契约，再登记跨服务 consumer。

## 更新要求

新增/删除/替换公开事件或活动消费者时，在同一变更中：

1. 核对 producer、consumer 注册与副作用实现；
2. 按 [`../../governance/integration/event-consumption.md`](../../governance/integration/event-consumption.md) 重新分类；
3. 更新本页受影响行，而不是追加日期化“已验证”时间线；
4. 需要保留一次性审计证据时新建冻结 Report，不把运行 ID/Issue 状态写入当前矩阵。

本矩阵不替代契约测试。公开事件的信封/版本/幂等性继续由 ADR 0011 及相应契约测试证明；“存在契约”也不自动证明有业务消费者。
