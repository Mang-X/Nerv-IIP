# Integration Event Consumption Matrix

> Verified against `main` @ `8de8d51dbce1d6d77aa11f62a444cb39d6d7ef80` on 2026-06-25. Re-verify when adding/removing any public `*IntegrationEvent` contract or `IIntegrationEventHandler`/`CapSubscribe` consumer.

This matrix is the current code-backed decision record for public integration
events and business-service-local integration events. It prevents older
`#419`/`#485` "published but unconsumed" lists from being treated as current
truth without checking source.

## Scope and Evidence

Source facts were collected from:

- Public contract events under `backend/common/Contracts/**`.
- Business service-local integration events under
  `backend/services/Business/**/Application/IntegrationEvents`.
- Active consumers declared with `IntegrationEventConsumer` or `CapSubscribe`
  under service `Application/IntegrationEventHandlers`.
- Existing follow-up issues under the `#485` meta tracker.

ADR 0011 remains the envelope/version/idempotency baseline for any event that is
public or crosses a service boundary. Service-local event records that do not
implement `IIntegrationEventEnvelope` must either stay local and documented here,
or be promoted through a focused contract/reliability issue before another
service consumes them.

Classification values:

- `consumed-internally`: an in-repo service currently has a real handler.
- `needs-business-consumer`: a real business flow is incomplete; the row links
  an existing issue, the `#485` child that owns the follow-up, or explicitly
  marks the gap as awaiting triage under `#485` when no child exists yet.
- `audit-or-external-only`: no in-platform state change is required now; the
  event is for audit, observability, notifications already covered elsewhere, or
  external extension consumers.
- `producer-only-until-feature`: the producer contract is useful, but the repo
  currently uses query/resolve APIs or a future feature boundary instead of a
  consumer.
- `deprecated/covered-by-other-contract`: current code uses a newer or narrower
  contract for the operational handoff.

## Follow-up Issue Map

`#485` is the meta tracker and remains open. Its current children cover:

- `#507`: ERP consumer reliability baseline before adding more ERP consumers.
- `#508`: DemandPlanning accepted purchase suggestion to ERP
  `PurchaseRequisition`.
- `#509`: procure-to-pay AP automation from purchase receipt facts.
- `#510`: order-to-cash AR automation from WMS outbound completion.
- `#512`: saga/process-manager versus choreography/compensation ADR.

This PR does not implement those consumers and does not close `#485`.

## Public Contracts Matrix

| Contract source | Event type or record | Current producer fact | Current consumer fact | Classification | Decision / follow-up |
|---|---|---|---|---|---|
| `Nerv.IIP.Contracts.Approval` | `businessApproval.ApprovalStarted` / `ApprovalStartedIntegrationEvent` | BusinessApproval converter publishes approval start facts. | No active handler found. | `audit-or-external-only` | Start facts are useful for audit/workbench timelines; no mandatory state-changing business consumer is required now. |
| `Nerv.IIP.Contracts.Approval` | `businessApproval.StepResolved` / `ApprovalStepResolvedIntegrationEvent` | BusinessApproval step-resolution converter. | Notification consumes `ApprovalStepResolvedIntegrationEvent`. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Approval` | `businessApproval.StepOverdue` / `ApprovalStepOverdueIntegrationEvent` | BusinessApproval overdue converter. | Notification consumes `ApprovalStepOverdueIntegrationEvent`. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Approval` | `businessApproval.ActionRecorded` / `ApprovalActionRecordedIntegrationEvent` | BusinessApproval action-record converter. | Notification consumes `ApprovalActionRecordedIntegrationEvent`. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Approval` | `businessApproval.ApprovalApproved`, `businessApproval.ApprovalRejected`, `businessApproval.ApprovalReturned` / `ApprovalCompletedIntegrationEvent` | BusinessApproval completed converter emits the completed event with the decision type. | ERP consumes `ApprovalCompletedIntegrationEvent` for purchase-order release/cancel decisions. | `consumed-internally` | `#507` may harden ERP reliability, but the business consumer exists. |
| `Nerv.IIP.Contracts.BarcodeLabel` | `barcode.BarcodeScanAccepted` / `BarcodeScanAcceptedIntegrationEvent` | BarcodeLabel publishes accepted scan facts. | No active handler found for this public scan event. Supported inventory scan workflows also publish `inventory.InventoryMovementRequested`, which Inventory consumes. | `producer-only-until-feature` | Keep as public scan/traceability fact for future MES/WMS/Quality scan consumers under `#485`; inventory posting is already covered by the Inventory request contract. |
| `Nerv.IIP.Contracts.DemandPlanning` | `demandPlanning.MrpRunCompleted` / `DemandPlanningIntegrationEvent<MrpRunCompletedPayload>` | DemandPlanning MRP run converter. | No active handler found. | `audit-or-external-only` | Run completion is a planning/audit fact unless a later analytics or notification feature needs a consumer. |
| `Nerv.IIP.Contracts.DemandPlanning` | `demandPlanning.PlannedPurchaseSuggested` / `DemandPlanningIntegrationEvent<PlanningSuggestionPayload>` | DemandPlanning planned-purchase suggestion converter. | No active ERP handler found. | `needs-business-consumer` | Owned by `#508` after `#507`; ERP PR creation is still manual today. |
| `Nerv.IIP.Contracts.DemandPlanning` | `demandPlanning.PlannedWorkOrderSuggested` / `DemandPlanningIntegrationEvent<PlanningSuggestionPayload>` | DemandPlanning planned-work-order suggestion converter. | MES does not consume this pre-acceptance event. MES consumes `PlanningSuggestionAcceptedIntegrationEvent`. | `deprecated/covered-by-other-contract` | Current MES handoff is the accepted-suggestion contract from `#461`/`#503`; keep the suggestion fact for planning/audit, not as the MES command trigger. |
| `Nerv.IIP.Contracts.DemandPlanning` | `demandPlanning.PlanningSuggestionAccepted` / `PlanningSuggestionAcceptedIntegrationEvent` | DemandPlanning acceptance converter. | MES consumes accepted work-order suggestions. ERP purchase-requisition route is not implemented. | `needs-business-consumer` | Partially consumed. ERP PR creation is owned by `#508`; MES side is already connected. |
| `Nerv.IIP.Contracts.IndustrialTelemetry` | `industrialTelemetry.DeviceStateChanged` / `DeviceStateChangedIntegrationEvent` | IndustrialTelemetry state converter. | No active handler found. | `producer-only-until-feature` | Current runtime views can query service facts directly. Future scheduling/MES projection consumers remain under `#485` if needed. |
| `Nerv.IIP.Contracts.IndustrialTelemetry` | `industrialTelemetry.AlarmRaised` / `AlarmRaisedIntegrationEvent` | IndustrialTelemetry alarm converter. | Maintenance consumes `AlarmRaisedIntegrationEvent` to open work-order context. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.IndustrialTelemetry` | `industrialTelemetry.AlarmCleared` / `AlarmClearedIntegrationEvent` | IndustrialTelemetry alarm-clear converter. | Maintenance consumes `AlarmClearedIntegrationEvent` to mark alarm-cleared work-order state. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Inventory` | `inventory.InventoryMovementRequested` / `InventoryMovementRequestedIntegrationEvent` | MES, WMS, BarcodeLabel, Maintenance and ERP-adjacent flows can request stock movements through this public contract. | Inventory consumes `InventoryMovementRequestedIntegrationEvent`. External requests are limited to `inbound`, `outbound`, `transfer` and `adjustment`; internal `count-adjustment` and `status-transfer-*` movements remain dedicated Inventory transactions. OwnerType is normalized through the Inventory domain whitelist before ledger dimensions are read or created. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Inventory` | `inventory.StockMovementPosted` / `StockMovementPostedIntegrationEvent` | Inventory publishes successful postings. | WMS and MES consume `StockMovementPostedIntegrationEvent`. | `consumed-internally` | AP automation may also observe receipt facts later, but the event is not unconsumed. |
| `Nerv.IIP.Contracts.Inventory` | `inventory.StockMovementPostingFailed` / `StockMovementPostingFailedIntegrationEvent` | Inventory publishes business-rejected movement requests. | WMS consumes `StockMovementPostingFailedIntegrationEvent`. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Inventory` | `inventory.StockCountVarianceConfirmed` / `StockCountVarianceConfirmedIntegrationEvent` | Inventory count variance converter. | No active handler found. | `producer-only-until-feature` | Keep as a public inventory governance fact. Future finance, notification, or analytics consumers remain under `#485` if required. |
| `Nerv.IIP.Contracts.Inventory` | `inventory.StockAvailabilityChanged` / `StockAvailabilityChangedIntegrationEvent` | Inventory ledger changes publish availability changes. | No active handler found. | `needs-business-consumer` | Awaiting triage under `#485`; no concrete child issue currently owns DemandPlanning/MES readiness refresh or equivalent availability projection. |
| `Nerv.IIP.Contracts.Maintenance` | `maintenance.AssetUnavailable` / `AssetUnavailableIntegrationEvent` | Maintenance publishes asset unavailability. | MES consumes `AssetUnavailableIntegrationEvent` for reschedule/capacity impact. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Maintenance` | `maintenance.AssetRestored` / `AssetRestoredIntegrationEvent` | Maintenance publishes asset restoration. | MES consumes `AssetRestoredIntegrationEvent` for reschedule/capacity impact. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.MasterData` | `masterData.SkuChanged` / `SkuChangedIntegrationEvent` | MasterData SKU converter. | No active handler found. | `producer-only-until-feature` | Current services resolve/snapshot SKU facts through APIs. Add consumers only when a downstream cache/projection feature exists; track via `#485`. |
| `Nerv.IIP.Contracts.MasterData` | `masterData.SkuDisabled` / `SkuDisabledIntegrationEvent` | MasterData SKU-disable converter. | No active handler found. | `producer-only-until-feature` | Same as SKU changed; future cache invalidation is under `#485`. |
| `Nerv.IIP.Contracts.MasterData` | `masterData.UnitOfMeasureChanged` / `UnitOfMeasureChangedIntegrationEvent` | MasterData UOM converter. | No active handler found. | `producer-only-until-feature` | Current downstream behavior relies on snapshots/resolve APIs. |
| `Nerv.IIP.Contracts.MasterData` | `masterData.BusinessPartnerChanged` / `BusinessPartnerChangedIntegrationEvent` | MasterData partner converter. | No active handler found. | `producer-only-until-feature` | ERP and other services should consume only when they add durable partner projections. |
| `Nerv.IIP.Contracts.MasterData` | `masterData.ResourceChanged` / `ResourceChangedIntegrationEvent` | MasterData resource converter. | No active handler found. | `producer-only-until-feature` | Scheduling/MES can add projections later under `#485`; no current code path depends on a consumer. |
| `Nerv.IIP.Contracts.MasterData` | `masterData.WorkCalendarChanged` / `WorkCalendarChangedIntegrationEvent` | MasterData calendar converter. | No active handler found. | `producer-only-until-feature` | Future scheduling/calendar projection follow-up belongs under `#485`. |
| `Nerv.IIP.Contracts.MasterData` | `masterData.DeviceAssetChanged` / `DeviceAssetChangedIntegrationEvent` | MasterData device converter. | No active handler found. | `producer-only-until-feature` | IndustrialTelemetry/Maintenance projections can consume later when implemented. |
| `Nerv.IIP.Contracts.MasterData` | `masterData.ReferenceDataCodeChanged` / `ReferenceDataCodeChangedIntegrationEvent` | MasterData reference-data converter. | No active handler found. | `producer-only-until-feature` | Reference-code invalidation is a future projection/cache concern, not a current missing business side effect. |
| `Nerv.IIP.Contracts.Ops` | `OperationTaskCompletedIntegrationEvent` | Ops completed converter. | AppHub and Notification consume operation completion. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Ops` | `OperationTaskFailedIntegrationEvent` | Ops failed converter. | AppHub and Notification consume operation failure. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Ops` | `OperationTaskRequestedIntegrationEvent` | Ops request converter. | No active handler found. | `audit-or-external-only` | Ops owns the task lifecycle; request facts are audit/external-extension facts. |
| `Nerv.IIP.Contracts.Ops` | `OperationApprovalRequestedIntegrationEvent` | Ops approval-request converter. | Notification consumes approval-request events. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Ops` | `OperationApprovalApprovedIntegrationEvent` | Ops approval-approved converter. | Notification consumes approved events. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Ops` | `OperationApprovalRejectedIntegrationEvent` | Ops approval-rejected converter. | Notification consumes rejected events. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Ops` | `OperationTaskClaimedIntegrationEvent` | Ops claimed converter. | No active handler found. | `audit-or-external-only` | Claim/lease state is owned by Ops; no separate platform state mutation is required. |
| `Nerv.IIP.Contracts.Ops` | `AuditRecordedIntegrationEvent` | Ops audit converter. | No active handler found. | `audit-or-external-only` | The event is already the audit publication surface; consumers are external/observability only unless a future reporting feature is built. |
| `Nerv.IIP.Contracts.ProductEngineering` | `productEngineering.BomReleased` / `BomReleasedIntegrationEvent` | ProductEngineering EBOM/MBOM release converters. | No active handler found. | `producer-only-until-feature` | DemandPlanning/MES/ERP currently use query/resolve facts. Add projection consumers only under `#485` or a focused downstream issue. |
| `Nerv.IIP.Contracts.ProductEngineering` | `productEngineering.RoutingReleased` / `RoutingReleasedIntegrationEvent` | ProductEngineering routing release converter. | No active handler found. | `producer-only-until-feature` | Same decision as BOM released. |
| `Nerv.IIP.Contracts.ProductEngineering` | `productEngineering.ProductionVersionCreated` / `ProductionVersionCreatedIntegrationEvent` | ProductEngineering production-version converter. | No active handler found. | `producer-only-until-feature` | Current MES/readiness paths resolve production versions directly; event projection is future work. |
| `Nerv.IIP.Contracts.ProductEngineering` | `productEngineering.EngineeringChangeReleased` / `EngineeringChangeReleasedIntegrationEvent` | ProductEngineering ECO/ECN release converter. | No active handler found. | `producer-only-until-feature` | Future invalidation/supersede consumers remain under `#485` or a ProductEngineering hardening issue. |
| `Nerv.IIP.Contracts.Quality` | `quality.DefectRaised` / `DefectRaisedIntegrationEvent` | MES can publish defects through the public Quality contract. | Quality consumes `DefectRaisedIntegrationEvent` to open NCR. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Quality` | `quality.NcrOpened` / `NcrOpenedIntegrationEvent` | Quality NCR-open converter. | No active handler found. | `audit-or-external-only` | NCR-open is a quality/audit notification candidate; no current state-changing consumer is required. |
| `Nerv.IIP.Contracts.Quality` | `quality.DispositionDecided` / `NcrDispositionDecidedIntegrationEvent` | Quality disposition converter. | MES consumes disposition decisions to update MES defect state. | `consumed-internally` | Inventory return/rework extensions can be separate future issues if prioritized. |
| `Nerv.IIP.Contracts.Quality` | `quality.NcrClosed` / `NcrClosedIntegrationEvent` | Quality NCR-close converter. | No active handler found. | `audit-or-external-only` | Closure fact is audit/external notification surface for now. |
| `Nerv.IIP.Contracts.Quality` | `quality.InspectionPassed`, `quality.InspectionConditionalReleased`, `quality.InspectionRejected` / `InspectionResultIntegrationEvent` | Quality inspection converters publish result variants through one event record. | Inventory and MES consume `InspectionResultIntegrationEvent`. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Scheduling` | `scheduling.SchedulePlanGenerated` / `SchedulingIntegrationEvent<...>` | Scheduling generated-plan converter. | No active handler found. | `producer-only-until-feature` | Generated plans remain Scheduling-owned until a workbench/notification/projection feature consumes them; track via `#485`. |
| `Nerv.IIP.Contracts.Scheduling` | `scheduling.ScheduleConflictDetected` / `SchedulingIntegrationEvent<...>` | Scheduling conflict converter. | No active handler found. | `producer-only-until-feature` | Conflict notification/workbench routing is future work under `#485`. |
| `Nerv.IIP.Contracts.Scheduling` | `scheduling.SchedulePlanReleased` / `SchedulePlanReleasedIntegrationEvent` | Scheduling plan-release converter. | MES consumes `SchedulePlanReleasedIntegrationEvent` for dispatch/work-order scheduling. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Wms` | `wms.InboundOrderCompleted` / `WmsIntegrationEvent` | WMS inbound completion converter, including source document type/id and completed line facts. | ERP consumes purchase-order-sourced inbound completion to record the ERP purchase receipt; purchase-receipt-sourced inbound completion is ignored to avoid duplicate ERP receipt/GR-IR accrual. | `consumed-internally` | WMS warehouse receipt can now project into ERP purchase receipt and then reuse ERP GR/IR + supplier-invoice AP flow; AP is still created only from matched supplier invoice or held-release, not directly from WMS completion. |
| `Nerv.IIP.Contracts.Wms` | `wms.OutboundOrderCompleted` / `WmsIntegrationEvent` | WMS outbound completion converter. | No active ERP handler found. | `needs-business-consumer` | AR automation is owned by `#510` after `#507`. |
| `Nerv.IIP.Contracts.Wms` | `wms.OutboundOrderRequested` / `WmsOutboundOrderRequestedIntegrationEvent` | ERP delivery release publishes WMS outbound requests. | WMS consumes `WmsOutboundOrderRequestedIntegrationEvent`. | `consumed-internally` | No dangling action. |
| `Nerv.IIP.Contracts.Wms` | `wms.CountExecutionCompleted` / `WmsIntegrationEvent` | WMS count completion converter. | No active handler found. | `producer-only-until-feature` | Future Inventory count reconciliation can be split from `#485` if required. |
| `Nerv.IIP.Contracts.Wms` | `wms.WcsTaskDispatched` / `WmsIntegrationEvent` | WMS WCS dispatch converter. | No active handler found. | `audit-or-external-only` | WCS task telemetry is operational/audit surface; no platform state consumer is required now. |
| `Nerv.IIP.Contracts.Wms` | `wms.WcsTaskFailed` / `WmsIntegrationEvent` | WMS WCS failure converter. | No active handler found. | `audit-or-external-only` | Failure is visible through WMS task state; add Notification only as a future product issue. |
| `Nerv.IIP.Contracts.Wms` | `wms.WcsTaskCompleted` / `WmsIntegrationEvent` | WMS WCS completion converter. | No active handler found. | `audit-or-external-only` | Completion is WMS/WCS telemetry unless a downstream feature is added. |

## Business Service-Local Events

| Service | Event type or record | Current producer fact | Current consumer fact | Classification | Decision / follow-up |
|---|---|---|---|---|---|
| BarcodeLabel | `barcode.LabelPrintBatchCreated` / `LabelPrintBatchCreatedIntegrationEvent` | BarcodeLabel local print-batch converter. | No cross-service handler found. | `audit-or-external-only` | Service-local print lifecycle fact; do not treat as missing business consumer. |
| BarcodeLabel | `barcode.LabelPrintBatchCompleted` / `LabelPrintBatchCompletedIntegrationEvent` | BarcodeLabel local print-batch converter. | No cross-service handler found. | `audit-or-external-only` | Service-local print lifecycle fact. |
| BarcodeLabel | `barcode.LabelScanned` / `LabelScannedIntegrationEvent` | BarcodeLabel local scan converter. | No cross-service handler found. | `deprecated/covered-by-other-contract` | Public scan handoff is `barcode.BarcodeScanAccepted`; inventory side effects use `inventory.InventoryMovementRequested`. |
| BarcodeLabel | `barcode.ScanRejected` / `ScanRejectedIntegrationEvent` | BarcodeLabel local rejected-scan converter. | No cross-service handler found. | `audit-or-external-only` | Rejection is a local traceability/diagnostic fact. |
| ERP | `erp.PurchaseRequisitionCreated` / `ErpIntegrationEvent<TPayload>` | ERP procurement converter. | No cross-service handler found. | `audit-or-external-only` | PR creation is ERP-owned; use public/query surfaces unless an external integration feature is added. `#507` decides local envelope/reliability posture before widening ERP consumers. |
| ERP | `erp.PurchaseOrderReleased` / `ErpIntegrationEvent<TPayload>` | ERP procurement converter. | No cross-service handler found. | `audit-or-external-only` | Supplier/procurement external integration candidate; no current in-platform consumer. |
| ERP | `erp.PurchaseReceiptRecorded` / `PurchaseReceiptRecordedIntegrationEvent` | ERP receipt converter. | ERP consumes the receipt fact to post one GR/IR accrual voucher through the processed-integration-event inbox. | `consumed-internally` | Supplier invoice matching or held-release clears GR/IR and creates AP; receipt itself does not create AP. |
| ERP | `erp.DeliveryOrderReleased` / `ErpIntegrationEvent<TPayload>` | ERP sales converter. | WMS does not consume this local ERP event directly. | `deprecated/covered-by-other-contract` | Warehouse handoff is the public `wms.OutboundOrderRequested` contract emitted from ERP delivery release. |
| ERP | `erp.AccountPayableCreated` / `ErpIntegrationEvent<TPayload>` | ERP finance converter. | No cross-service handler found. | `audit-or-external-only` | Finance-owned audit/external integration fact. |
| ERP | `erp.AccountReceivableCreated` / `ErpIntegrationEvent<TPayload>` | ERP finance converter. | No cross-service handler found. | `audit-or-external-only` | Finance-owned audit/external integration fact. |
| ERP | `erp.CostCandidateCreated` / `ErpIntegrationEvent<TPayload>` | ERP costing converter. | No cross-service handler found. | `audit-or-external-only` | Costing audit/analytics fact. |
| ERP | `erp.JournalVoucherPosted` / `ErpIntegrationEvent<TPayload>` | ERP finance converter. | No cross-service handler found. | `audit-or-external-only` | Accounting ledger publication for audit/external systems. |
| Maintenance | `maintenance.WorkOrderOpened` / `MaintenanceWorkOrderOpenedIntegrationEvent` | Maintenance local work-order converter. | No cross-service handler found. | `audit-or-external-only` | Public capacity impact uses `maintenance.AssetUnavailable`; local work-order open is not a required cross-service command. |
| Maintenance | `maintenance.WorkOrderCompleted` / `MaintenanceWorkOrderCompletedIntegrationEvent` | Maintenance local work-order converter. | No cross-service handler found. | `deprecated/covered-by-other-contract` | Public restoration/capacity impact uses `maintenance.AssetRestored`; spare-part issue uses `inventory.InventoryMovementRequested` where needed. |

## Readiness Notes

- The required contract gate remains
  `dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore`.
- That gate discovers referenced public non-generic `*IntegrationEvent`
  contracts and validates ADR 0011 envelope shape. It does not prove that every
  event type has a business consumer.
- Consumer readiness must be reviewed against this matrix plus active handler
  annotations. A no-consumer event is only a defect when classified
  `needs-business-consumer`.
- `needs-business-consumer` rows in this matrix deliberately map to existing
  issues (`#508`, `#509`, `#510`), their enabling child (`#507`), or a clearly
  marked `#485` triage gap where no concrete child exists yet. This document
  does not create additional GitHub issues.
