# Inventory Business Gap Design

## Goal

Close GitHub issue #412 by hardening Inventory as the stock fact source for stock status, reservation/allocation, moving-average valuation, count snapshot safety, and Quality inspection release integration.

## Scope

Inventory owns the new behavior. WMS, MES, ERP, and Quality keep their private workflow models. Cross-service integration uses public contracts under `backend/common/Contracts` and Inventory HTTP/event boundaries only.

## Design

1. Stock status becomes a controlled Inventory value with canonical statuses `unrestricted`, `quality`, and `blocked`. Existing `qualified` input is accepted as a compatibility alias for `unrestricted`; persisted new facts use canonical values.
2. Inventory adds service-local reservations. A reservation references a source service/document/line and a ledger dimension, increments `StockLedger.ReservedQuantity`, can be released, and can be allocated by outbound movement. Available quantity is always `onHand - reserved`.
3. Inventory adds moving-average valuation fields. Positive movements may carry `UnitCost`; ledger value and moving average update on receipt. Negative movements consume current moving average unless a command supplies a cost override. This provides Inventory value facts without implementing ERP GL posting.
4. Count tasks freeze the target ledger while open and capture `ExpectedLedgerVersion`. Variances at or below the configured quantity and moving-average amount thresholds post immediately and release the freeze. Above-threshold variances persist as `pending-approval`, start the established `COUNT-VARIANCE` BusinessApproval chain, and do not create a movement or change the ledger until `ApprovalCompleted`. Approval posts the movement and releases the freeze; rejection or return voids the adjustment, releases the freeze, and marks the task `recount-required` so a new count can be started. Cancellation explicitly releases the freeze without posting an adjustment. Version drift is represented by a structured recount-required domain exception instead of message-text control flow.
5. Quality inspection result events are consumed through `Nerv.IIP.Contracts.Quality` and the shared CAP consumer guard/DLQ path. Passed inspections transfer quantity from `quality` to `unrestricted`; rejected inspections transfer from `quality` to `blocked`; conditional releases transfer from `quality` to `restricted`. `InspectionResultPayload` now carries optional top-level stock locator fields (`LotNo`, `SerialNo`, `SiteCode`, `LocationCode`, `OwnerType`, `OwnerId`, `UomCode`) and retains the nested `StockRelease` shape for compatibility. Inventory uses these dimensions to post status-transfer out/in movement pairs against the exact ledger; if an older event lacks locator dimensions, Inventory keeps the legacy single matching `quality` ledger fallback and fails visibly when it cannot resolve exactly one source ledger.

## API And Events

New Inventory internal endpoints:

- `POST /api/inventory/v1/reservations`
- `POST /api/inventory/v1/reservations/{reservationId}/release`
- `POST /api/inventory/v1/status-transfers`

Existing movement and availability contracts gain optional valuation fields. OpenAPI/api-client regeneration is required if Gateway exposes these fields later; this slice updates service-local endpoint contracts and backend tests.

The existing count-adjustment facade returns the adjustment lifecycle status and optional approval-chain ID. The Business Console must show a high-variance adjustment as pending approval rather than as posted stock.

## Persistence

Inventory schema adds:

- `stock_reservations`
- `stock_ledgers.is_frozen_for_count`
- `stock_ledgers.moving_average_unit_cost`
- `stock_ledgers.inventory_value`
- `stock_movements.unit_cost`
- `stock_movements.movement_amount`
- `stock_count_adjustments.approval_chain_id`
- `stock_count_adjustments.variance_amount`
- `stock_count_adjustments.status`

Schema comments and convention tests must cover the new table and columns.

## Testing

Focused tests must prove:

- invalid stock statuses are rejected and aliases normalize to canonical status;
- reservations reduce availability, cannot exceed available quantity, release idempotently, and outbound allocation consumes reserved quantity;
- moving-average cost and inventory value update on inbound/outbound movements;
- open count tasks freeze movements, confirmation or cancellation releases the freeze, and stale ledger versions require recount;
- above-threshold count adjustments leave the ledger unchanged until an approved `ApprovalCompleted` event posts the movement; rejection/return voids the adjustment and requires recount;
- Quality inspection passed/rejected events create status-transfer movements through public contracts.

## Business Console expiry presentation (MAN-449 / #803)

The Business Console inventory lots and availability views consume the existing
BusinessGateway `GET /api/business-console/v1/inventory/expiry-alerts` read
facade for a real 30-day near-expiry view. Shared frontend expiry presentation
uses UTC calendar-day comparison and the common thresholds `>90` days normal,
`30–90` days near, `<30` days critical, and negative days expired. The pages
show the returned `expiryDate`/`daysUntilExpiry` facts with a text status badge;
missing production date, shelf-life and expiry-source fields remain explicitly
unknown. The FEFO explanation is attached to the expiry column header.

The current facade response contains `items` only: it has no `total`, paging
window, production/shelf-life/source details, or operation block reason. The
Console therefore does not invent a server total, pagination, operation disable
state, or detail panel. A follow-up facade/schema change is required before a
true paged total card and full batch-detail/source presentation can be claimed.
