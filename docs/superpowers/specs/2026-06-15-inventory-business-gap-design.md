# Inventory Business Gap Design

## Goal

Close GitHub issue #412 by hardening Inventory as the stock fact source for stock status, reservation/allocation, moving-average valuation, count snapshot safety, and Quality inspection release integration.

## Scope

Inventory owns the new behavior. WMS, MES, ERP, and Quality keep their private workflow models. Cross-service integration uses public contracts under `backend/common/Contracts` and Inventory HTTP/event boundaries only.

## Design

1. Stock status becomes a controlled Inventory value with canonical statuses `unrestricted`, `quality`, and `blocked`. Existing `qualified` input is accepted as a compatibility alias for `unrestricted`; persisted new facts use canonical values.
2. Inventory adds service-local reservations. A reservation references a source service/document/line and a ledger dimension, increments `StockLedger.ReservedQuantity`, can be released, and can be allocated by outbound movement. Available quantity is always `onHand - reserved`.
3. Inventory adds moving-average valuation fields. Positive movements may carry `UnitCost`; ledger value and moving average update on receipt. Negative movements consume current moving average unless a command supplies a cost override. This provides Inventory value facts without implementing ERP GL posting.
4. Count tasks freeze the target ledger while open and capture `ExpectedLedgerVersion`. Confirmation posts the count adjustment and releases the freeze; cancellation explicitly releases the freeze without posting an adjustment. Version drift is represented by a structured recount-required domain exception instead of message-text control flow.
5. Quality inspection result events are consumed through `Nerv.IIP.Contracts.Quality` and the shared CAP consumer guard/DLQ path. Passed inspections transfer quantity from `quality` to `unrestricted`; rejected inspections transfer from `quality` to `blocked`. Because the current Quality event payload does not carry location/UOM/lot/owner, this integration only auto-releases when Inventory can resolve a single matching `quality` ledger for the SKU and quantity. This is a temporary single-batch/unique-ledger limitation, not the long-term multi-lot closure model; ambiguous releases fail visibly instead of guessing.

## API And Events

New Inventory internal endpoints:

- `POST /api/inventory/v1/reservations`
- `POST /api/inventory/v1/reservations/{reservationId}/release`
- `POST /api/inventory/v1/status-transfers`

Existing movement and availability contracts gain optional valuation fields. OpenAPI/api-client regeneration is required if Gateway exposes these fields later; this slice updates service-local endpoint contracts and backend tests.

## Persistence

Inventory schema adds:

- `stock_reservations`
- `stock_ledgers.is_frozen_for_count`
- `stock_ledgers.moving_average_unit_cost`
- `stock_ledgers.inventory_value`
- `stock_movements.unit_cost`
- `stock_movements.movement_amount`

Schema comments and convention tests must cover the new table and columns.

## Testing

Focused tests must prove:

- invalid stock statuses are rejected and aliases normalize to canonical status;
- reservations reduce availability, cannot exceed available quantity, release idempotently, and outbound allocation consumes reserved quantity;
- moving-average cost and inventory value update on inbound/outbound movements;
- open count tasks freeze movements, confirmation or cancellation releases the freeze, and stale ledger versions require recount;
- Quality inspection passed/rejected events create status-transfer movements through public contracts.
