# ADR 0019: WMS to Inventory RPC Idempotency

- Status: Accepted
- Date: 2026-07-07

## Context

WMS has two synchronous Inventory RPC chains that can time out after Inventory commits and before WMS stores the returned Inventory identifier:

1. picking task creation reserves Inventory stock;
2. count execution creation creates an Inventory count task and freezes the target ledger.

Without a stable recovery key, a caller retry can either create a second Inventory side effect or leave WMS without the committed Inventory identifier. MAN-390 / GitHub #706 requires these chains to converge after timeout and retry without shared database writes or fake downstream ids.

## Decision

Use synchronous RPC with caller-generated stable idempotency keys and query-based retry recovery.

WMS derives keys from durable business identity:

1. picking reservation: `wms-pick-res:<hash(organizationId:environmentId:outboundOrderNo:lineNo)>`;
2. count freeze: `wms-count-freeze:<hash(organizationId:environmentId:countNo)>`.

Inventory persists the key on the committed fact. A retry with the same key is a recovery query:

1. same key and same payload return the existing reservation or count task result;
2. same key and different payload are rejected as an idempotency conflict;
3. count task code conflicts with a different idempotency key are rejected before a second freeze can be created.

Inventory fallback keys for count tasks are also namespaced as `count-code:<countTaskCode>` so caller-provided keys cannot collide with the legacy count-code fallback space.

For concurrent count-freeze retries inside the service process, Inventory serializes `CreateStockCountTaskCommand` by organization, environment and resolved idempotency key through the existing command lock behavior. The database unique index remains the durable backstop for the committed fact.

## Alternatives Considered

1. **Event-driven freeze with callback receipt**: rejected for this slice because the caller needs a synchronous answer to create the WMS count execution and picking task. Adding an event receipt table would widen the surface and still need a query path for operator retry.
2. **Best-effort cleanup after timeout**: rejected because WMS cannot know whether Inventory committed before the timeout. Cleanup risks releasing a valid reservation or count freeze that a later retry should recover.
3. **Shared reconciliation table**: rejected because ADR 0003 and ADR 0012 keep service data ownership isolated; WMS must not read or write Inventory schema.

## Consequences

The compensation path is deterministic: retry the same WMS command. WMS recomputes the same key, Inventory returns the committed fact, and WMS persists the returned Inventory id locally.

This keeps timeout recovery local to the two owning services and does not introduce a cross-service process manager. Operators still need normal retry or DLQ tooling to re-drive the WMS command after a transport timeout.

Verification must include cross-boundary WMS and Inventory behavior. Fast in-memory tests may cover command flow, but real PostgreSQL profile tests must cover the unique-index and retry/concurrency behavior when `NERV_IIP_TEST_POSTGRES` is available.
