# DemandPlanning MRP Gap Design

## Goal

Close GitHub issue #409 for the DemandPlanning MRP calculation path by making net requirements account for scheduled receipts, multi-level BOM demand, lead-time release dates, lot sizing, and safety stock without moving ERP, MES, Inventory, ProductEngineering, or MasterData facts into DemandPlanning.

## Current Code Facts

`MrpCalculator` currently nets each demand with `demand.Quantity - available`, emits one planned work order, explodes only first-level MBOM components, emits every component shortage as `planned-purchase`, and sets suggestion `RequiredDate` to the demand due date. `PlanningInputAdapters` can read ProductEngineering production versions and MBOM lines and Inventory availability snapshots, but it does not include scheduled receipts, safety stock, or lead-time planning parameters. ProductEngineering production-version list DTOs already expose `LotSizeMin` and `LotSizeMax`, but DemandPlanning drops them before calculation.

ERP purchase orders expose line-level `SkuCode`, `UomCode`, `OrderedQuantity`, `ReceivedQuantity`, and `PromisedDate`; these can become purchase scheduled receipts. MES work orders expose `SkuId`, `Quantity`, `DueUtc`, and `Status`, but not UOM, so this PR must not fabricate MES scheduled receipt UOM. MES scheduled receipts require a dedicated MES receipt snapshot or a UOM field before live adapter wiring.

Lead time, safety stock, and lot-size multiple have no authoritative production source until MasterData issue #407 adds planning attributes. DemandPlanning keeps `PlanningParameterSnapshot` as the algorithm contract and defaults those values to zero/null in the live upstream provider. ProductEngineering `LotSizeMin` and `LotSizeMax` are available today and flow through production-version snapshots.

## Scope

1. Add internal MRP snapshot records for scheduled receipts and item planning parameters.
2. Feed scheduled receipts into netting by SKU/UOM/site/date bucket.
3. Expand BOM recursively: make items with production versions become planned work orders and continue exploding their components; items without production versions become planned purchases.
4. Apply lead-time offset by calculating `ReleaseDate = RequiredDate - LeadTimeDays`.
5. Apply daily bucket aggregation and lot sizing with L4L plus `LotSizeMin`, `LotSizeMax`, and optional `LotSizeMultiple`.
6. Apply safety stock in netting as protected quantity.
7. Persist and expose `ReleaseDate` on planning suggestions.
8. Wire ProductEngineering `LotSizeMin`/`LotSizeMax` into production-version snapshots.
9. Add ERP purchase-order scheduled receipts to the upstream snapshot provider only from open purchase-order lines with remaining quantity.
10. Document the remaining MES scheduled-receipt adapter limitation.

## Non-Goals

1. No MPS/RCCP implementation; issue #409 includes that as a separate P1 but this slice focuses on the requested net-requirement hardening items.
2. No new MasterData planning-attribute schema; safety stock and lead-time values enter through snapshot parameters and default to zero until MasterData issue #407 provides authoritative fields.
3. No live MES scheduled-receipt adapter until MES exposes a UOM-safe work-order receipt snapshot.
4. No ERP/MES document creation; planning suggestions remain DemandPlanning facts.
5. No cross-schema foreign keys or service-internal project references.

## Design

`MrpCalculationInput` gains `ScheduledReceipts` and `PlanningParameters`. `ScheduledReceiptSnapshot` carries SKU, UOM, site, quantity, expected receipt date, source system, document type, and document id. `PlanningParameterSnapshot` carries SKU, UOM, site, lead time days, safety stock, lot size min/max, and lot size multiple.

The calculator normalizes inputs into daily buckets. For each bucket and SKU/UOM/site, gross demand is aggregated before netting. Safety stock is treated as an on-hand inventory floor, not as repeatable gross demand per bucket. Net requirement is:

`gross demand - max(0, available - safety stock) - scheduled receipts due on or before the required date`

Positive requirements are lot-sized. If an item has a production version, the calculator emits a planned work-order suggestion, pegs the demand and scheduled receipts used, offsets `ReleaseDate`, and creates component demand for the child SKU. If an item has no production version, the calculator emits a planned-purchase suggestion and stops explosion. A visited path guard prevents recursive BOM cycles from hanging the run.

`PlanningSuggestion` gains `ReleaseDate` while retaining `RequiredDate` as the due/need-by date. The list API and integration event payload add `ReleaseDate` as an additive v1 field. The database migration adds `planning_suggestions.release_date` with a backfill default equal to `required_date`.

## Testing

Focused calculator tests cover:

1. scheduled receipts reduce duplicate suggestions,
2. multi-level BOM emits a semi-finished work order and raw-material purchase,
3. lead time offsets release date,
4. daily bucket aggregation plus min/max lot sizing,
5. safety stock creates protected net requirement.

Adapter tests cover ProductEngineering lot-size field preservation and ERP purchase-order scheduled receipts. Existing fixture behavior remains green for the original MVP example.
