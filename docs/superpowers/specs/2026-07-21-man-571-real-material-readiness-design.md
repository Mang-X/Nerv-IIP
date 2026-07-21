# MAN-571 Real Material Readiness Design

## Scope

Close GitHub #1035 / Linear MAN-571 only. The managed `leader-demo-main-chain` scenario must establish the run-scoped raw material through public BusinessGateway HTTP and real cross-process service facts before MES release. Existing shortage enforcement stays unchanged.

## Approaches considered

1. Post an Inventory movement directly from the scenario. Rejected because it bypasses procurement and warehouse facts and is the behavior this issue replaces.
2. Create an ERP purchase order and record an ERP purchase receipt directly. Rejected because it skips the WMS receiving/putaway boundary.
3. Create and approve an ERP purchase order, create the linked WMS inbound order and putaway task, complete the inbound order, then poll public Inventory availability. Selected because it reuses existing public contracts and preserves the actual ERP -> WMS -> Inventory event path.

## Design

The scenario creates a run-scoped supplier and an `erp-purchase-order-release` approval template assigned to the authenticated principal. It creates the raw-material purchase order with a stable idempotency key, approves the generated chain through the public approval facade, and waits until ERP exposes the released order.

The scenario then creates a WMS inbound order sourced from that purchase order, creates a run-scoped putaway task, and completes the inbound order with a stable receipt idempotency key. WMS publishes its existing Inventory movement request; Inventory posts the real stock; ERP consumes the WMS completion to project the purchase receipt. The scenario polls both ERP received quantity and Inventory availability for the exact organization, environment, SKU, UOM, site, location, lot, quality status, and owner type.

Only after availability equals the ordered quantity does the scenario create/accept the Planning work order. MES therefore captures the already-established Inventory quantity in its existing material requirement snapshot and its unchanged release guard can enter the releasable path.

## Replay behavior

The scenario deliberately repeats purchase-order creation, inbound-order creation, putaway-task creation, and inbound completion with the same stable keys. WMS command handlers return the existing same-key facts rather than inserting duplicates. Conflicting later completion keys remain rejected by the existing immutable inbound-order rule. Inventory and ERP consumers retain their existing idempotent event handling.

## Contract and schema impact

No endpoint, OpenAPI, generated client, facade classification, database schema, or migration changes are required. Existing exposed BusinessGateway operations are reused. WMS handler behavior and tests change, but route shapes and facade coverage remain unchanged.

## Verification

- Focused WMS red/green tests prove same-key replay returns the same ids and does not add rows.
- Business Console contract tests prove the scenario removed direct Inventory writes and orders the public ERP/WMS/Inventory chain before Planning acceptance.
- Existing MES tests continue proving insufficient inventory remains a release blocker.
- `fullstack run -Scenario leader-demo-main-chain` must produce runtime-confirmed `mes-work-order-schedule-plan` evidence and clean up all managed resources.

