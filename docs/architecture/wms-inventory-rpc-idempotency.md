# WMS Inventory RPC Idempotency

This note records the MAN-390 / GitHub #706 decision for the two WMS to Inventory synchronous RPC chains that must survive caller-side timeout after Inventory has already committed.

## Scope

Covered synchronous chains:

1. WMS picking task creation reserves Inventory stock.
2. WMS count execution creation creates an Inventory count task and freezes the target ledger.

Movement posting remains event-driven through the existing WMS-owned `inventory_movement_requests` and Inventory movement-requested consumer path.

## Decision

Use synchronous RPC with caller-generated stable idempotency keys and retry recovery.

WMS derives keys from durable WMS business identity, not from transient task IDs:

1. Picking reservation key: `wms-pick-res:<hash(organizationId:environmentId:outboundOrderNo:lineNo)>`.
2. Count freeze key: `wms-count-freeze:<hash(organizationId:environmentId:countNo)>`.

Inventory persists the key on the committed fact and treats a duplicate key as a recovery query:

1. Same key and same payload returns the existing reservation or count task result.
2. Same key and different payload is rejected as an idempotency conflict.
3. Count task code conflicts with a different idempotency key are rejected before a second freeze can be created.

## Timeout Recovery

If WMS times out after Inventory commits but before WMS persists the public Inventory ID, the operator or caller retries the same WMS command. WMS recomputes the same key and calls Inventory again. Inventory returns the already committed reservation or count task, and WMS persists the returned public ID on the outbound line or count execution.

This keeps the compensation path local and deterministic: retry is the reconciliation query. No extra cross-service table sharing, downstream fake IDs, or best-effort cleanup task is introduced for this slice.

## Verification

`WmsInventoryRpcIdempotencyAcceptanceTests` covers the real cross-boundary behavior with in-memory WMS and Inventory contexts:

1. Inventory commits a reservation, the simulated RPC then times out, and WMS retry recovers the same reservation without duplicating Inventory state.
2. Inventory commits a count freeze, the simulated RPC then times out, and WMS retry recovers the same count task without a second freeze.
