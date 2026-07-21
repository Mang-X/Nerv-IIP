# MAN-571 Real Material Readiness Design

## Scope

Close GitHub #1035 / Linear MAN-571 only. The managed `leader-demo-main-chain` scenario must establish the run-scoped raw material through public BusinessGateway HTTP and real cross-process service facts before MES release. Existing shortage enforcement stays unchanged.

## Approaches considered

1. Post an Inventory movement directly from the scenario. Rejected because it bypasses procurement and warehouse facts and is the behavior this issue replaces.
2. Create an ERP purchase order and record an ERP purchase receipt directly. Rejected because it skips the WMS receiving/putaway boundary.
3. Create and approve an ERP purchase order, create the linked WMS inbound order and putaway task, complete the inbound order, then poll public Inventory availability. Selected because it reuses existing public contracts and preserves the actual ERP -> WMS -> Inventory event path.

## Design

The scenario creates a run-scoped supplier and an `erp-purchase-order-release` approval template assigned to the authenticated principal. It creates the raw-material purchase order with a stable idempotency key, approves the generated chain through the public approval facade, and waits until ERP exposes the released order.

ERP's purchase-order approval client is wired to the Aspire `business-approval` endpoint through an endpoint expression, `WithReference`, and `WaitFor`. This keeps the cross-service bridge valid when managed full-stack runs allocate an ephemeral Approval port instead of the local fixed-port fallback.

The Approval-completed and WMS-inbound-completed ERP consumers persist their projected aggregate changes and processed-event inbox in the handler invocation. Tests no longer call `SaveChangesAsync` on behalf of production consumers; this keeps a successful CAP delivery from being acknowledged while its purchase-order release or receipt projection remains only tracked in memory. If receipt recording is rejected after the inbox row was staged, the handler clears all unsaved tracked state before the persistent dead-letter store saves, so the rejected delivery remains replayable instead of being silently marked processed.

The scenario then creates a WMS inbound order sourced from that purchase order, creates a run-scoped putaway task, and completes the inbound order with a stable receipt idempotency key. WMS publishes its existing Inventory movement request; Inventory posts the real stock; ERP consumes the WMS completion to project the purchase receipt. The material is received into the managed MES Inventory configuration's canonical `production` site (MES currently queries only its configured `warehouse` / `production` sites), while the SKU, lot, purchase order, inbound order, task, organization, and environment remain run-scoped. The scenario polls both ERP received quantity and Inventory availability for that exact organization, environment, SKU, UOM, site, location, lot, quality status, and owner type.

Only after availability equals the ordered quantity does the scenario create/accept the Planning work order. MES therefore captures the already-established Inventory quantity in its existing material requirement snapshot and its unchanged release guard can enter the releasable path.

## Replay behavior

The scenario deliberately repeats purchase-order creation, inbound-order creation, putaway-task creation, and inbound completion with the same stable keys. WMS command handlers return the existing same-key facts rather than inserting duplicates. Inbound-completion replay derives the exact per-line persisted keys (including the hashed long-key form), compares the existing movement requests and supplied lot/date captures against the completed inbound facts, returns a canonical line-ordered request id, and rejects same-key conflicts. BusinessGateway preserves that request id in its public completion response so the managed scenario can prove the first call and replay identify the same persisted movement request. Conflicting later completion keys remain rejected by the existing immutable inbound-order rule. Inventory and ERP consumers retain their existing idempotent event handling.

## Contract and schema impact

No endpoint, facade classification, database schema, or migration changes are required. Existing exposed BusinessGateway operations are reused. The existing WMS completion facade response adds the optional `requestId` already returned by the WMS service; the BusinessGateway OpenAPI snapshot and generated client are refreshed. WMS handler behavior, ERP consumer persistence, and the ERP-to-Approval AppHost bridge change, while route shapes and facade coverage remain unchanged.

## Verification

- Focused WMS red/green tests prove same-key replay returns the same ids and does not add rows.
- Business Console contract tests prove the scenario removed direct Inventory writes and orders the public ERP/WMS/Inventory chain before Planning acceptance.
- Existing MES tests continue proving insufficient inventory remains a release blocker.
- Managed review run `nerv-827e-008191` produced runtime-confirmed raw-material supply, identical first/replay WMS request ids, exact Inventory availability 10, MES readiness `Ready`, and a successful MES release with an operation task. The overall strict main-chain test then stopped at the independently tracked Scheduling gap below. Cleanup left no session-labeled containers, volumes, or networks and reported no cleanup errors.
- The next independent breakpoint is Scheduling's `mes.materialReadinessSourceUnavailable` after MES release; it is tracked separately by GitHub #1037 / Linear MAN-572 and is not part of this change.
