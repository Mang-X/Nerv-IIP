# Issue 419 Integration Closure Gap Spec

## Context

GitHub issue #419 is the cross-service integration meta issue for the business backend. This revision is based on `origin/main` at 2026-06-18 after the later #409, #410, #414, #416, #417 and #418 related work had been merged.

The governing boundaries remain:

1. Public cross-service events live under `backend/common/Contracts/**` and must follow ADR 0011.
2. Service-local events can exist, but they are not a stable cross-service contract until promoted to public Contracts or exposed through a documented facade.
3. Consumers must use public Contracts, internal HTTP APIs, Gateway facades, or SDK boundaries. They must not reference another service's Domain, Infrastructure, Web implementation, schema, or tables.
4. BusinessGateway and PlatformGateway remain facade layers and must not become process managers or domain orchestrators.

## Current Event Wiring Panorama

### Connected Public Event Paths

| Flow | Publisher evidence | Consumer evidence | Current status |
| --- | --- | --- | --- |
| WMS requests Inventory posting | `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventConverters/WmsIntegrationEventConverters.cs:8` | `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs:11` | Connected through `inventory.InventoryMovementRequested`. |
| Inventory confirms/rejects WMS posting | `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventConverters/InventoryIntegrationEventConverters.cs:6` and `:36` | `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/StockMovementPostedIntegrationEventHandlerForMarkWmsRequestPosted.cs:10` | Connected for posted and failed WMS movement requests. |
| IndustrialTelemetry alarm opens/updates Maintenance work | `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/IntegrationEventConverters/IndustrialTelemetryIntegrationEventConverters.cs:33` and `:61` | `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventHandlers/OpenWorkOrderWhenAlarmRaisedHandler.cs:14` | Connected through `industrialTelemetry.AlarmRaised` and alarm clear handling. |
| Maintenance availability affects MES | `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventConverters/MaintenanceIntegrationEventConverters.cs:52` and `:74` | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/MaintenanceAssetEventHandlers.cs:23` and `:86` | Connected through `maintenance.AssetUnavailable` and `maintenance.AssetRestored`. |
| Maintenance spare-part completion requests Inventory issue | `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventConverters/MaintenanceIntegrationEventConverters.cs` | `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs:11` | Connected through the shared Inventory movement request contract. |
| Scheduling release dispatches MES operation tasks | `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventConverters/SchedulingIntegrationEventConverters.cs:120` | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/SchedulingPlanReleasedIntegrationEventHandler.cs:13` | Connected through `scheduling.SchedulePlanReleased`. |
| MES material issue, material consumption and finished-goods receipt post Inventory movements | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs:13`, `:94`, and `:119` | `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs:11` | Connected through `inventory.InventoryMovementRequested`. |
| Inventory confirms MES finished-goods receipt | `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventConverters/InventoryIntegrationEventConverters.cs:6` | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/StockMovementPostedIntegrationEventHandlerForMarkMesReceiptPosted.cs:11` | Connected for MES receipt completion marking. |
| Quality inspection result updates Inventory quality stock status | `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventConverters/InspectionIntegrationEventConverters.cs:7` and `:20` | `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs:12` | Connected for passed/rejected inspection result stock status transfer. |
| Quality NCR disposition updates MES defect state | `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/IntegrationEventConverters/NonconformanceReportIntegrationEventConverters.cs:41` | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/NcrDispositionDecidedIntegrationEventHandlerForUpdateMesDefect.cs:11` | Connected for MES defect disposition status. |
| ERP purchase receipt posts Inventory receipt | `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpProcurementIntegrationEventConverters.cs:72` | `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/InventoryMovementRequestedIntegrationEventHandlerForPostingMovement.cs:11` | Connected through `inventory.InventoryMovementRequested`. |
| ERP delivery release creates WMS outbound order | `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesFinanceIntegrationEventConverters.cs:24` | `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/WmsOutboundOrderRequestedIntegrationEventHandler.cs:9` | Connected through `wms.OutboundOrderRequested`. |
| Approval completion releases ERP purchase order | `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEventConverters/ApprovalIntegrationEventConverters.cs:110` | `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/ApprovalCompletedIntegrationEventHandlerForReleasePurchaseOrder.cs:10` | Connected for ERP purchase order approval closure. |
| Ops events drive Notification/AppHub projections | `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/OperationTaskCompletedIntegrationEventConverter.cs:7` and `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/OperationTaskFailedIntegrationEventConverter.cs:9` | `backend/services/Notification/src/Nerv.IIP.Notification.Web/Application/IntegrationEventHandlers/OperationTaskFailedIntegrationEventHandlerForNotification.cs:15`, `backend/services/Notification/src/Nerv.IIP.Notification.Web/Application/IntegrationEventHandlers/OpsOperationNotificationIntegrationEventHandlers.cs:16`, and `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Application/IntegrationEventHandlers/OperationTaskCompletedIntegrationEventHandlerForRefreshInstanceState.cs:17` | Connected for platform control-plane notifications/projections. |

### Remaining Published But Unconsumed Or Local-Only Events

| Area | Evidence | Remaining gap |
| --- | --- | --- |
| MasterData change events | Converters start at `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEventConverters/MasterDataIntegrationEventConverters.cs:6`; public contracts are in `backend/common/Contracts/Nerv.IIP.Contracts.MasterData/MasterDataIntegrationEvents.cs:27`. | Public events are produced, but no backend consumer is registered in current code facts. Downstream read models still refresh by HTTP/query paths. |
| ProductEngineering release events | Converters start at `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEventConverters/ProductEngineeringIntegrationEventConverters.cs:6`; public contracts start at `backend/common/Contracts/Nerv.IIP.Contracts.ProductEngineering/ProductEngineeringContracts.cs:64`. | BOM/routing/production-version facts are published, but MES/Scheduling still do not consume these release events as cache invalidation or automatic readiness refresh triggers. |
| DemandPlanning suggestions | `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEventConverters/DemandPlanningIntegrationEventConverters.cs:31`, `:40`, and `:48`. | Suggestions and acceptances remain service-local generic event envelopes, with no ERP/MES event consumers. MES and ERP still expose HTTP/manual creation paths from suggestions. |
| ERP finance events | `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesFinanceIntegrationEventConverters.cs:59`, `:74`, `:90`, and `:105`. | AP/AR/cost/journal events remain ERP-local envelopes with no downstream process consumers or cross-service saga state. |
| WMS completion events | `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventConverters/WmsIntegrationEventConverters.cs:35` and `:51`. | Inbound/outbound completion facts are published, but ERP does not consume outbound completion to close delivery/AR, and ERP does not consume inbound completion as a procurement milestone. |
| BarcodeLabel scan accepted events | `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/IntegrationEventConverters/BarcodeLabelIntegrationEventConverters.cs:81`. | The public `barcode.BarcodeScanAccepted` contract exists, but no backend consumer subscribes to it; existing scan-driven Inventory movement still runs in BarcodeLabel's own command route. |

## Five End-To-End Closure Statuses

1. **Plan-to-produce is now partially connected, not fully broken.** Scheduling release to MES dispatch is connected, and MES material/receipt movements now reuse Inventory's public movement request consumer. Remaining gap: DemandPlanning suggestion acceptance is still not an event-driven ERP/MES firming path, and ProductEngineering release events do not automatically refresh MES/Scheduling readiness facts.
2. **Procure-to-pay stock posting is connected, but AP/triple-match closure remains incomplete.** ERP purchase receipt now emits Inventory movement requests. Remaining gap: `erp.PurchaseReceiptRecorded` and AP creation remain ERP-local process facts with no event consumer that proves three-way match or payable creation from receipt.
3. **Order-to-cash dispatch is connected, but shipment-to-AR closure remains incomplete.** ERP delivery release now emits `wms.OutboundOrderRequested` and WMS consumes it. Remaining gap: WMS outbound completion has no ERP consumer to close delivery, create AR, or reconcile shipment status.
4. **Quality-to-stock is partially connected.** Inspection passed/rejected events now drive Inventory quality status transfer, and NCR disposition updates MES defects. Remaining gap: NCR disposition does not yet prove Inventory scrap movement or MES rework work-order creation as a cross-service event path.
5. **Plan firming remains manually mediated.** DemandPlanning publishes planned purchase/work-order suggestions and acceptance events, but current ERP/MES creation remains HTTP/manual/facade driven rather than an event consumer choreography keyed by `PlanningSuggestionAccepted`.

## Saga, Envelope, And DLQ Governance

The backend still has no saga/process-manager/orchestration implementation for business flows: the current integrated paths are point-to-point choreography with local idempotency/inbox/DLQ patterns. That is acceptable for the current smallest closure path, but procure-to-pay and order-to-cash now have enough connected links that future AP/AR shipment closure should explicitly decide whether to remain choreography or introduce a process manager.

ADR 0011 requires public integration events to implement a common envelope. This PR keeps the #419 governance implementation focused on the public contracts gate:

1. `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventEnvelopeContractTests.cs` discovers referenced public non-generic `*IntegrationEvent` contract types by assembly scan instead of a hand-maintained subset.
2. The test project references the current public event contract assemblies, including Approval, BarcodeLabel, Inventory, IndustrialTelemetry, Maintenance, MasterData, Ops, ProductEngineering, Quality, Scheduling and WMS.
3. MasterData, ProductEngineering and Quality public events now implement `IIntegrationEventEnvelope`; current main already had Approval, BarcodeLabel, Scheduling, WMS, Inventory, Maintenance, IndustrialTelemetry and Ops public contracts on the same interface.

ERP and DemandPlanning still use service-local generic envelopes rather than public Contracts projects. They should either be promoted to `backend/common/Contracts` before new consumers subscribe, or remain explicitly documented as service-local events.

## Acceptance Script Strategy

Current minimal gate:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
```

Cross-service closure gates should evolve in this order:

1. Keep the public contract envelope gate non-Docker and CI-friendly.
2. Add focused consumer tests whenever an event gains a consumer, following existing Inventory/WMS/Maintenance/MES/ERP handler patterns with `IntegrationEventConsumerGuard` and a DLQ store.
3. Extend `scripts/verify-business-full-chain-acceptance.ps1` only after concrete links land, so it verifies behavior rather than encoding expected failures.
4. If a new PowerShell verification script is introduced, it must dot-source `scripts/lib/ScriptAutomation.ps1`, declare script governance metadata, and pass `scripts/check-script-governance.ps1`.
