# WMS Business Gap #413 Design

## Goal

Close the WMS execution loops that currently leave warehouse work in an indeterminate state, without letting WMS read or mutate Inventory internals.

## Current Code Facts

1. WMS creates `InventoryMovementRequest` facts and publishes `inventory.InventoryMovementRequested` through `Nerv.IIP.Contracts.Inventory`.
2. Inventory posts stock movements from that public event. A business rejection, such as negative on-hand, currently throws during the consumer and does not publish a public failure fact.
3. WMS already has `InventoryMovementRequest.MarkFailed(...)`, but no consumer calls it.
4. `WarehouseTask.RecordProgress(...)` exists in the domain model, but there is no command or endpoint for task execution.
5. WCS callback commands find by `ExternalTaskId` only, despite WCS tasks carrying organization and environment.
6. After #412, Inventory owns public reserve/release/allocation commands and can allocate a reservation during outbound stock posting when the movement request carries a reservation id.
7. Inventory availability facts still do not carry expiry or receipt timestamp fields needed for true FEFO/FIFO allocation.
8. WMS inbound lines carry received quantity only. They do not carry ASN expected quantity or directed-putaway capacity/location strategy facts.

## Delivered Scope

### Inventory Posting Failure Compensation

Inventory will publish a public `inventory.StockMovementPostingFailed` event when a valid `InventoryMovementRequested` envelope cannot be posted for business reasons. The event carries the same source document, line, idempotency key and stock dimensions as the request, plus a failure code and message.

WMS will consume `inventory.StockMovementPostingFailed` and mark the matching `InventoryMovementRequest` as `Failed`. For inbound and outbound movement requests, WMS also moves the owning order into `InventoryPostingFailed` so list/read surfaces no longer show a completed order with a pending stock posting.

### Warehouse Task Execution

WMS will expose task progress and completion commands for existing `WarehouseTask.RecordProgress(...)`. The first slice does not add operator assignment; it provides a deterministic execution path that PDA, BusinessGateway or WCS adapters can call.

### Picking Reservation Allocation

WMS will reserve Inventory stock when creating an outbound picking task through the public Inventory reservation API. WMS stores only the public reservation id on the outbound line, then carries that id on the `inventory.InventoryMovementRequested` payload created by pack review completion. Inventory remains the owner of stock balances and allocates the reservation during outbound posting.

### WCS Callback Scoping

WCS complete/fail callbacks will match by `organizationId + environmentId + externalTaskId`, preventing callbacks in one tenant context from mutating another context with the same external task id.

## Deferred Contracts

These issue items remain larger than the current code facts allow without inventing private coupling:

1. Reservation release/cancel compensation: the current slice reserves at picking and allocates during outbound posting; releasing reservations for cancelled or rolled-back WMS work remains a follow-up public-contract workflow.
2. FEFO/FIFO: requires Inventory availability lines to expose allocation sort facts such as expiry date, receipt time or lot aging source.
3. ASN and directed putaway: requires inbound expected quantity and Location/putaway strategy facts. WMS can own expected/received variance, but location capacity and mix rules need a WMS location model or MasterData location extension.
4. LPN/HU: requires a new HandlingUnit aggregate and task/movement references.

## Testing

1. Inventory consumer tests prove negative stock publishes `StockMovementPostingFailed` and does not leave the failure invisible.
2. Inventory integration-event tests prove the new public contract uses ADR 0011 envelope shape.
3. WMS consumer tests prove failed events mark matching requests failed and ignore non-WMS sources.
4. WMS endpoint/command tests prove task progress/completion APIs exist and WCS callbacks are tenant scoped.
5. WMS and Inventory focused tests prove picking reservation ids are persisted on WMS outbound flow, propagated through movement-requested, and allocated by Inventory posting.
