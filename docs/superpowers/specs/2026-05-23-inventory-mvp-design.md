# Inventory MVP Design

## Goal

Build Inventory as the single business fact source for stock movements, stock ledger balances, stock availability and count adjustments.

## Current State

Inventory has no service directory. BusinessMasterData is available as the Layer 0 reference source for SKU, UOM, site, production line, work center, business partner and reference data validation. WMS, ERP, MES and DemandPlanning do not have stable Inventory contracts yet.

## Owned Facts

Inventory owns these facts:

1. StockLocation: warehouse, zone, bin or logical stock location code and status.
2. StockLedger: current quantity by organization, environment, SKU, UOM, site, location, lot, serial, quality status and ownership reference.
3. StockMovement: append-only stock movement record with movement type, source document reference, idempotency key and signed quantity.
4. StockCountTask: count execution header and counted lines for variance confirmation.
5. StockCountAdjustment: movement generated from an approved count variance.

Inventory does not own:

1. WMS inbound, outbound, picking, putaway or WCS task state.
2. ERP purchase, sales, invoice, payable, receivable or valuation facts.
3. MES work order, operation, report or material issue execution state.
4. MasterData SKU, UOM, partner, site or equipment facts.
5. Quality inspection decisions beyond the quality status stored on stock facts.

## MVP Commands And Queries

| API | Purpose | Notes |
| --- | --- | --- |
| `POST /api/inventory/v1/locations` | Create or update a stock location. | Idempotent by organization, environment and location code. |
| `POST /api/inventory/v1/movements` | Post an inbound, outbound, transfer, adjustment or count movement. | Requires `idempotencyKey`; rejects duplicate key with conflicting payload. |
| `GET /api/inventory/v1/availability` | Query available quantity by SKU, UOM, site, location and optional lot/serial. | Returns on-hand, reserved and available. Reserved is `0` in MVP until reservation is introduced. |
| `POST /api/inventory/v1/count-tasks` | Create a stock count task. | Count task records scope and expected ledger snapshot version. |
| `POST /api/inventory/v1/count-tasks/{countTaskId}/adjustments` | Confirm count variance and post adjustment movements. | Creates StockCountAdjustment facts and StockMovement records. |

## Movement Rules

1. StockMovement is append-only.
2. Negative on-hand is rejected unless movement type is configured as an explicit correction.
3. Movement idempotency key is unique per organization, environment, source service and source document reference.
4. Quantity is stored with decimal precision suitable for process manufacturing.
5. UOM conversion is resolved through MasterData contracts or stored snapshot fields; Inventory does not own conversion rules.
6. Lot and serial values are optional but must follow the SKU traceability policy once that policy is exposed by MasterData.

## Availability Rules

1. On-hand is the current StockLedger quantity.
2. Reserved is `0` for the MVP.
3. Available is on-hand minus reserved.
4. Query results include the ledger dimensions used for aggregation so WMS, ERP and DemandPlanning can avoid ambiguous totals.

## Events

Inventory publishes ADR 0011 envelope events:

1. `inventory.StockMovementPosted`
2. `inventory.StockCountVarianceConfirmed`
3. `inventory.StockAvailabilityChanged`

Events must carry public document references, SKU/UOM/location dimensions, movement quantity and correlation IDs. Events must not carry database row internals or cross-service foreign keys.

## Permissions

Initial permission codes:

1. `business.inventory.locations.manage`
2. `business.inventory.movements.create`
3. `business.inventory.availability.read`
4. `business.inventory.counts.manage`

## Persistence

Default schema: `inventory`.

Required tables:

1. `stock_locations`
2. `stock_ledgers`
3. `stock_movements`
4. `stock_count_tasks`
5. `stock_count_adjustments`

Each table and business column requires schema comments. PostgreSQL migrations history must use `inventory.__EFMigrationsHistory`.

## Testing

Acceptance requires:

1. Domain tests for movement posting, duplicate idempotency keys, negative stock rejection and count adjustment generation.
2. Web tests for internal authorization, request validation, route shape and stable operation IDs.
3. Query tests for availability aggregation.
4. Schema convention tests using `Nerv.IIP.Testing`.
5. Event converter tests for ADR 0011-compatible event names and payloads.
