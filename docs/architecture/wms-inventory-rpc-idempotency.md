# WMS Inventory RPC Idempotency

This note records the MAN-390 / GitHub #706 implementation details for the two WMS to Inventory synchronous RPC chains that must survive caller-side timeout after Inventory has already committed. The architectural decision is ADR 0019.

## Scope

Covered synchronous chains:

1. WMS picking task creation reserves Inventory stock.
2. WMS count execution creation creates an Inventory count task and freezes the target ledger.

Movement posting remains event-driven through the existing WMS-owned `inventory_movement_requests` and Inventory movement-requested consumer path.

## Decision

Use synchronous RPC with caller-generated stable idempotency keys and retry recovery.

WMS derives keys from durable WMS business identity, not from transient task IDs:

1. Picking reservation key: `wms-pick-res:<hash(organizationId:environmentId:outboundOrderNo:lineNo)>`. This key shape already existed in `CreatePickingTaskCommandHandler` through `WmsInventoryReservationIdempotencyKeys.ForPickingTask`; this PR keeps that implementation and adds cross-boundary retry evidence for it.
2. Count freeze key: `wms-count-freeze:<hash(organizationId:environmentId:countNo)>`.

Inventory persists the key on the committed fact and treats a duplicate key as a recovery query:

1. Same key and same payload return the existing reservation or count task result.
2. Same key and different payload are rejected as an idempotency conflict.
3. Count task code conflicts with a different idempotency key are rejected before a second freeze can be created.

Inventory count-task fallback keys use the `count-code:` namespace so explicit caller keys cannot collide with count-task-code fallback keys in the same unique index.

## Timeout Recovery

If WMS times out after Inventory commits but before WMS persists the public Inventory ID, the operator or caller retries the same WMS command. WMS recomputes the same key and calls Inventory again. Inventory returns the already committed reservation or count task, and WMS persists the returned public ID on the outbound line or count execution.

This keeps the compensation path local and deterministic: retry is the reconciliation query. No extra cross-service table sharing, downstream fake IDs, or best-effort cleanup task is introduced for this slice.

## Verification

`WmsInventoryRpcIdempotencyAcceptanceTests` covers the cross-boundary behavior in two tiers:

1. Fast in-memory WMS and Inventory contexts prove WMS retry recomputes the same key and recovers the same reservation or count task after a simulated post-commit timeout.
2. The opt-in real PostgreSQL test, enabled by `NERV_IIP_TEST_POSTGRES`, runs WMS and Inventory against migrated PostgreSQL databases and verifies count-freeze timeout recovery plus concurrent retry convergence through the Inventory MediatR, UnitOfWork and command-lock pipeline.
