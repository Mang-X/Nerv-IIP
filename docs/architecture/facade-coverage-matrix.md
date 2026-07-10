# Facade Coverage Matrix

> Machine-readable source of truth: [`facade-coverage-matrix.json`](./facade-coverage-matrix.json).
> This document is the governance narrative + rendered summary. The per-endpoint
> registry lives in the JSON and is enforced by `Nerv.IIP.FacadeCoverage.Tests`.
> Last full re-derivation: MAN-475 / #841, against `main` at the #843 facade backfill.

This matrix is the code-backed decision record for **which business-service HTTP
endpoints are reachable from the frontend through a Gateway facade**, and which
are deliberately not. It exists to close a structural gap surfaced by X1 / #784:
a capability is only usable end-to-end when it is delivered as **two hops** — the
service endpoint *and* a Gateway facade (OpenAPI snapshot → api-client codegen →
stable barrel). Issue acceptance historically watched only the first hop, so a
missing facade turned no gate red and could only be found by a full audit (#784
recovered 11 such gaps, backfilled by #833–#838).

It is the HTTP-facade analogue of
[`integration-event-consumption-matrix.md`](./integration-event-consumption-matrix.md):
the same "producer with no consumer is only a defect when classified as one"
pattern, applied to "service endpoint with no facade". A no-facade endpoint is a
defect only when it should have been `exposed`; `deferred` and `internal` are
legitimate, recorded states.

## Classifications

Every business-service external HTTP endpoint is exactly one of:

- **`exposed`** — reachable through a Gateway facade. The row records the facade
  `gatewayOperationIds`, each **verified by the gate to exist in the Gateway OpenAPI
  snapshot** — so `exposed` always carries machine-checkable facade evidence. The
  capability is present in a snapshot, regenerated into `@nerv-iip/api-client`
  (`types.gen.ts`), and re-exported from a stable barrel (`business-console.ts` /
  `console.ts`). Business endpoints are exposed through **BusinessGateway**.
- **`deferred`** — the service endpoint exists but no Gateway facade is delivered
  yet. This is a *tracked* gap that follows a frontend menu phase or a named
  issue. `deferred` must carry a `followUp` note. It is the honest, visible form
  of "not yet", so it can never again be confused with "forgotten".
- **`internal`** — never exposed through a Gateway by design. These are
  service-to-service contracts, background schedulers, or connector/WCS callback
  endpoints. `internal` must carry a `rationale`. The canonical precedent is
  IIoT `GET /iiot/runtime-hours` (#688), consumed only by Maintenance PM.

## DoD contract (mandatory declaration)

Per AGENTS.md ("Facade Coverage Governance"), **any issue that adds or changes a
business-service HTTP endpoint must declare a consumption-face outcome for each
new/changed endpoint — `exposed`, `deferred`, or `internal` — and update this
matrix in the same PR.** There is no default. PR review cross-checks the
declaration against what actually shipped (facade + codegen + barrel for
`exposed`; matrix `followUp` for `deferred`; matrix `rationale` for `internal`).

## How the gate enforces it

`backend/tests/Nerv.IIP.FacadeCoverage.Tests` runs inside the normal
`dotnet test backend/Nerv.IIP.sln` pass (so it is already wired into CI):

1. **Coverage** — reflects the live `*EndpointContracts.All` registry of every
   business service and asserts every `(service, method, route)` is present in the
   JSON. **A newly added endpoint that is not registered fails the build.**
2. **No stale rows** — every JSON row must map back to a live endpoint, so renamed
   or removed endpoints cannot rot in the registry.
3. **Classification validity** — value ∈ {`exposed`,`deferred`,`internal`} with the
   required companion field: `exposed` → non-empty `gateways` **and** non-empty
   `gatewayOperationIds`; `deferred` → `followUp`; `internal` → `rationale`.
4. **`exposed` truthfulness** — every `exposed` row's `gatewayOperationIds` must
   actually exist in the named Gateway OpenAPI snapshot. An `exposed` row with no
   verifiable facade operationId, or one whose facade is absent from the snapshot,
   fails — the exact #784 failure mode (endpoint claims exposed, no facade shipped).
5. **`deferred`/`internal` are not silently exposed** — a `deferred` or `internal`
   endpoint must **not** appear as a 1:1 facade route in the BusinessGateway
   snapshot. Shipping a facade without flipping the classification fails.
6. **Summary freshness** — the per-service summary table below is asserted against
   the JSON, so the doc cannot drift from the registry.

## Maintaining the matrix

- **New endpoint** → add its row to `facade-coverage-matrix.json` with the chosen
  classification. If `exposed`, deliver the facade and record the facade
  `gatewayOperationIds` (the gate verifies them against the snapshot).
- **Flip `deferred` → `exposed`** when the facade ships: change `classification`,
  add `gateways` + `gatewayOperationIds`, drop `followUp`.
- **New business service** → add its `.Web` project reference and assembly name to
  the gate project (`Nerv.IIP.FacadeCoverage.Tests`) so its endpoints are covered.
- The `exposed` rows are summarised by count here; the full 319-row registry with
  per-endpoint facade operation ids lives in the JSON.

## Summary

<!-- FACADE-COVERAGE-SUMMARY:START (generated from facade-coverage-matrix.json; the FacadeCoverage.Tests gate asserts these counts) -->
| Service | Total | exposed | deferred | internal |
| --- | ---: | ---: | ---: | ---: |
| Approval | 16 | 11 | 4 | 1 |
| BarcodeLabel | 9 | 9 | 0 | 0 |
| DemandPlanning | 15 | 15 | 0 | 0 |
| Erp | 50 | 39 | 10 | 1 |
| IndustrialTelemetry | 20 | 17 | 1 | 2 |
| Inventory | 11 | 5 | 2 | 4 |
| Maintenance | 20 | 15 | 5 | 0 |
| MasterData | 41 | 38 | 0 | 3 |
| Mes | 46 | 43 | 3 | 0 |
| ProductEngineering | 38 | 38 | 0 | 0 |
| Quality | 27 | 16 | 11 | 0 |
| Scheduling | 7 | 6 | 1 | 0 |
| Wms | 24 | 19 | 3 | 2 |
| **Total** | **324** | **271** | **40** | **13** |
<!-- FACADE-COVERAGE-SUMMARY:END -->

The `exposed` rows (271) — each with its verified facade `gatewayOperationIds` — are
enumerated in the JSON registry. The `deferred` and `internal` rows, the actual
governance decisions, are listed in full below.

### Deferred endpoints (facade tracked, not yet exposed)

| Service | Method | Service route | Follow-up |
| --- | --- | --- | --- |
| Approval | POST | `/api/business/v1/approvals/chains/{chainId}/resubmit` | BusinessGateway facade pending; follows the approval-governance (withdraw/resubmit/add-signer/transfer) Business Console menu phase (#488). |
| Approval | POST | `/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/add-signer` | BusinessGateway facade pending; follows the approval-governance Business Console menu phase (#488). |
| Approval | POST | `/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/transfer` | BusinessGateway facade pending; follows the approval-governance Business Console menu phase (#488). |
| Approval | POST | `/api/business/v1/approvals/chains/{chainId}/withdraw` | BusinessGateway facade pending; follows the approval-governance Business Console menu phase (#488). |
| Erp | POST | `/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/changes` | BusinessGateway facade pending; purchase-order amendment approval follows the ERP order-management Business Console menu phase. |
| Erp | POST | `/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/lines/{lineNo}/final-delivery` | BusinessGateway facade pending; final-delivery closure follows the ERP order-management Business Console menu phase. |
| Erp | POST | `/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/cancel` | BusinessGateway facade pending; purchase-order cancellation follows the ERP order-management Business Console menu phase. |
| Erp | POST | `/api/business/v1/erp/sales-orders/{salesOrderNo}/lines/{lineNo}` | BusinessGateway facade pending; sales-order amendment follows the ERP order-management Business Console menu phase. |
| Erp | POST | `/api/business/v1/erp/sales-orders/{salesOrderNo}/cancel` | BusinessGateway facade pending; sales-order cancellation follows the ERP order-management Business Console menu phase. |
| Erp | POST | `/api/business/v1/erp/finance/payables/payment` | BusinessGateway facade pending; follows the ERP finance Business Console menu phase (ERP menu is explicitly phased per readiness). |
| Erp | POST | `/api/business/v1/erp/finance/receivables/collection` | BusinessGateway facade pending; follows the ERP finance Business Console menu phase. |
| Erp | POST | `/api/business/v1/erp/supplier-invoices` | BusinessGateway facade pending; supplier-invoice UI is a known ERP frontend gap (readiness). |
| Erp | POST | `/api/business/v1/erp/supplier-invoices/{invoiceNo}/release-payment-hold` | BusinessGateway facade pending; supplier-invoice payment-hold UI is a known ERP frontend gap. |
| Erp | POST | `/api/business/v1/erp/supplier-invoices/{invoiceNo}/void-payment-hold` | BusinessGateway facade pending; supplier-invoice payment-hold UI is a known ERP frontend gap. |
| IndustrialTelemetry | POST | `/api/business/v1/iiot/tags` | BusinessGateway facade pending; telemetry tag create follows the equipment/telemetry config menu phase (only tag list GET is exposed today). |
| Inventory | POST | `/api/inventory/v1/count-tasks/{countTaskId}/cancel` | BusinessGateway facade pending; count-task create/adjust are exposed, cancel follows the inventory count Business Console menu phase. |
| Inventory | POST | `/api/inventory/v1/locations` | BusinessGateway facade pending; inventory location master-setup UI is a later menu phase. |
| Maintenance | GET | `/api/business/v1/maintenance/downtime-reasons` | BusinessGateway facade pending; downtime-reason catalog config UI is a later Maintenance menu phase. |
| Maintenance | POST | `/api/business/v1/maintenance/downtime-reasons` | BusinessGateway facade pending; downtime-reason catalog config UI is a later Maintenance menu phase. |
| Maintenance | DELETE | `/api/business/v1/maintenance/downtime-reasons/{reasonCode}` | BusinessGateway facade pending; downtime-reason catalog config UI is a later Maintenance menu phase. |
| Maintenance | PUT | `/api/business/v1/maintenance/downtime-reasons/{reasonCode}` | BusinessGateway facade pending; downtime-reason catalog config UI is a later Maintenance menu phase. |
| Maintenance | POST | `/api/business/v1/maintenance/work-orders/{workOrderId}/repair-started` | BusinessGateway facade pending; repair-start action follows the CMMS execution Business Console menu phase. |
| Mes | POST | `/api/business/v1/mes/material-issue-requests/{requestId}/line-side-returns` | BusinessGateway facade pending; line-side return follows the MES material workbench menu phase. |
| Mes | POST | `/api/business/v1/mes/work-orders/{workOrderId}/close` | BusinessGateway facade pending; MES work-order close follows the workbench close-action menu phase (hold/cancel already exposed via #833). |
| Mes | POST | `/api/business/v1/mes/work-orders/{workOrderId}/engineering-change-decisions` | BusinessGateway facade pending; work-order engineering-change-decision follows the ECO-on-workorder menu phase. |
| Quality | POST | `/api/business/v1/quality/capas` | BusinessGateway facade pending; CAPA management facade tracked by #677, unlocks frontend #804. |
| Quality | POST | `/api/business/v1/quality/capas/{correctiveActionId}/actions` | BusinessGateway facade pending; CAPA management facade tracked by #677, unlocks frontend #804. |
| Quality | POST | `/api/business/v1/quality/capas/{correctiveActionId}/actions/{correctiveActionItemId}/complete` | BusinessGateway facade pending; CAPA management facade tracked by #677, unlocks frontend #804. |
| Quality | POST | `/api/business/v1/quality/capas/{correctiveActionId}/close` | BusinessGateway facade pending; CAPA management facade tracked by #677, unlocks frontend #804. |
| Quality | POST | `/api/business/v1/quality/capas/{correctiveActionId}/effectiveness` | BusinessGateway facade pending; CAPA management facade tracked by #677, unlocks frontend #804. |
| Quality | POST | `/api/business/v1/quality/inspection-plans` | BusinessGateway facade pending; inspection-plan create follows the Quality plan-config menu phase (only plan list GET is exposed today). |
| Quality | POST | `/api/business/v1/quality/inspection-plans/{inspectionPlanId}/activate` | BusinessGateway facade pending; inspection-plan activation follows the Quality plan-lifecycle menu phase. |
| Quality | POST | `/api/business/v1/quality/ncrs` | BusinessGateway facade pending; generic NCR create follows the Quality NCR menu phase (only NCR-from-inspection is exposed today via openBusinessConsoleQualityNcrFromInspection). |
| Quality | GET | `/api/business/v1/quality/ncrs/{ncrId}` | BusinessGateway facade pending; NCR list/disposition/close are exposed, single-NCR detail-by-id follows the Quality NCR detail menu phase. |
| Quality | POST | `/api/business/v1/quality/spc/control-chart/evaluate` | BusinessGateway facade pending; SPC control-chart read is exposed, evaluate (write) follows the SPC analysis menu phase (#725). |
| Quality | POST | `/api/business/v1/quality/spc/control-chart/lock` | BusinessGateway facade pending; SPC control-limit lock (write) follows the SPC analysis menu phase (#725). |
| Scheduling | POST | `/api/business/v1/scheduling/problems/assemble` | BusinessGateway facade pending; APS problem-assemble follows the scheduling workbench menu phase (preview/create/gantt/release already exposed). |
| Wms | POST | `/api/business/v1/wms/inbound-orders/{inboundOrderId}/inventory-posting/retry` | BusinessGateway facade pending; WMS inbound posting-retry follows the WMS operations menu phase (MES posting-retry already exposed via #833). |
| Wms | POST | `/api/business/v1/wms/outbound-orders/{outboundOrderId}/cancel` | BusinessGateway facade pending; WMS outbound cancel follows the WMS operations menu phase. |
| Wms | POST | `/api/business/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry` | BusinessGateway facade pending; WMS outbound posting-retry follows the WMS operations menu phase. |

### Internal endpoints (never exposed by design)

| Service | Method | Service route | Rationale |
| --- | --- | --- | --- |
| Approval | POST | `/api/business/v1/approvals/tasks/overdue/check` | Internal server-clock overdue scheduler endpoint invoked by the Approval OverdueCheck background scanner (#488); not a user action. |
| Erp | GET | `/api/business/v1/erp/purchase-receipts/{purchaseReceiptNo}/source-document` | Service-to-service source-document read contract consumed by Quality to validate receipt line SKU/qty/UOM/lot (#77). |
| IndustrialTelemetry | POST | `/api/business/v1/iiot/alarms/escalations/run` | Internal alarm-escalation scheduler endpoint (IndustrialTelemetry:AlarmEscalation opt-in scanner, #686); not a user action. |
| IndustrialTelemetry | GET | `/api/business/v1/iiot/runtime-hours` | By-design internal API consumed by Maintenance PM day-interval generation (#688). Canonical internal precedent - never a Console facade. |
| Inventory | POST | `/api/inventory/v1/reservations` | Service-to-service reservation API consumed by WMS pick-task creation (#412). |
| Inventory | POST | `/api/inventory/v1/reservations/fefo` | Service-to-service FEFO reservation API consumed by WMS (#412). |
| Inventory | POST | `/api/inventory/v1/reservations/{reservationId}/release` | Service-to-service reservation release API consumed by WMS outbound cancel (#412). |
| Inventory | POST | `/api/inventory/v1/status-transfers` | Internal controlled-status transition driven by Quality inspection-result events; not a direct Console action. |
| MasterData | GET | `/api/business/v1/master-data/partners/{customerCode}/credit` | Service-to-service public credit read consumed by ERP sales-order credit check (#436). |
| MasterData | POST | `/api/business/v1/master-data/references/resolve` | Service-to-service batch reference-data resolve consumed by other business services. |
| MasterData | POST | `/api/business/v1/master-data/references/validate` | Service-to-service batch reference-data validate consumed by other business services. |
| Wms | POST | `/api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/complete` | Internal warehouse-task completion endpoint consumed by the WCS adapter/callback boundary (#413). |
| Wms | POST | `/api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/progress` | Internal warehouse-task progress endpoint consumed by the WCS adapter/callback boundary (#413). |

## Relationship to #842 (device-control read-face)

#842 (parallel) adds IIoT device-control **result / history GET** endpoints
service-side and their BusinessGateway facade. This matrix's framework and gate do
not depend on #842. When #842 lands, add its new IIoT endpoints as `exposed`
rows (the gate will otherwise flag them as unregistered). The existing IIoT
`POST /iiot/device-control-commands` dispatch facade (#838) is already recorded as
`exposed`.
