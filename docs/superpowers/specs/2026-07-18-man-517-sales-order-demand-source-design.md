# MAN-517 Sales Order Demand Source Bridge Design

## Scope

Deliver only Linear MAN-517 / GitHub #958: publish ERP sales-order lifecycle facts and project them into DemandPlanning demand sources. MAN-518 fulfillment overview and MAN-524 whole-chain walkthrough remain out of scope.

## Code Facts

- ERP creates a sales order directly in `released`, except credit-limit failures create `credit-held`; approval later calls `ReleaseCreditHold`.
- Sales orders already carry a monotonic business `Version`. Line changes and cancellation advance it, but credit-hold release currently does not.
- Sales orders may contain multiple lines. DemandPlanning currently has one uniqueness boundary per `(organization, environment, demandType, sourceReference)`, so it cannot represent a multi-line order without a schema change.
- DemandPlanning has no CAP consumer or persistent inbox/dead-letter tables. CAP handlers do not receive the HTTP command unit-of-work behavior, so consumer persistence must call `SaveChangesAsync` explicitly.
- Planning already displays `sourceReference` and `sales-order`; the missing UI behavior is a drill-through to the existing ERP sales-order page.

## Considered Approaches

1. **Three lifecycle facts with full snapshots (chosen).** Publish concrete `SalesOrderReleasedIntegrationEvent`, `SalesOrderChangedIntegrationEvent`, and `SalesOrderCancelledIntegrationEvent`. Each carries the complete order-line snapshot plus a monotonic `orderVersion`. This makes event meaning explicit and lets a consumer converge after duplicates, missing intermediate versions, or out-of-order delivery.
2. **One lifecycle event with an action discriminator.** This reduces types but weakens contract discoverability and makes incompatible action semantics easier to mix.
3. **Line-level delta events.** This minimizes payload size but requires every intermediate event to arrive and substantially complicates replay, removed-line detection, and cancellation convergence.

The full snapshot is bounded by the existing sales-order aggregate and is the safest recovery contract.

## ERP Contract and State Semantics

All three contracts are concrete non-generic records implementing the ADR 0011 envelope. They use event version 1 and include organization/environment in the envelope. The payload contains sales-order ID and number, customer, site, business order version, lifecycle status, and ordered line snapshots containing line reference, SKU, quantity, UOM, required date, and cancellation state.

- Creating a non-credit-held order publishes `SalesOrderReleased` at version 1.
- Releasing a credit-held order advances the order version and publishes `SalesOrderReleased` with the current full snapshot. Changes made while credit-held do not publish downstream demand changes before the first release.
- Changing a released line publishes `SalesOrderChanged` after advancing the version.
- Cancelling a released or credit-held order publishes `SalesOrderCancelled` after advancing the version. The cancellation fact acts as a tombstone even if a release was never observed.

Event idempotency keys are stable per order lifecycle version: `erp:sales-order:<organization>:<environment>:<order-number>:v<version>:<fact>`. Only letters, digits, colons, hyphens, and the order's already-governed code are used; no `+` or CLR generic/nested type name is used as a converter/subscription key.

ERP persists `SiteCode` on the sales order because the current model has no authoritative site fact. The existing create-sales-order HTTP/facade contract is extended to require it. This is an `exposed` endpoint change and therefore requires OpenAPI export, api-client generation, stable barrel verification, frontend form updates, ERP migration, schema catalog/comments, and convention tests.

## DemandPlanning Projection

DemandPlanning adds:

- order projection/watermark keyed by organization, environment, and sales-order ID, retaining order number, customer, site, latest order version/status, event ID, and occurrence time;
- demand-source fields for source document ID, source line reference, customer, source version, and source status;
- a uniqueness boundary over organization, environment, demand type, source reference, and source line reference.

For a valid event, the consumer executes one explicit database transaction:

1. validate envelope source/type/version, payload identity, positive version, and line facts;
2. record the persistent inbox key;
3. compare the order watermark; an equal/older version is a successful no-op;
4. upsert every snapshot line as `demandType=sales-order` and `sourceReference=<sales-order-number>`;
5. mark omitted/cancelled lines as `cancelled` with quantity zero; on full cancellation cancel every order demand;
6. advance the watermark and explicitly save.

Malformed or non-recoverable business payloads are persisted to the DemandPlanning dead-letter store and acknowledged, avoiding poison-message retry loops. Transient database/infrastructure failures still escape for bounded CAP retry.

MRP reads only active positive-quantity demands. Pegging and suggestions continue to retain the existing source reference, so a production suggestion generated from `SO-DEMO-001` remains traceable without copying ERP facts.

## UI and Documentation

Planning renders a sales-order `sourceReference` as a router link to `/erp/sales/orders?keyword=<order-number>`. The sales-order page initializes its existing keyword filter from the route query. No fulfillment timeline or duplicated order detail is added.

Update the integration-event consumption matrix, the sales-to-planning product flow, schema catalog, and reusable demo prerequisites for creating/releasing `SO-DEMO-001` with a real site and customer credit setup.

## Verification

- Contract and converter tests cover all lifecycle facts, versions, envelope fields, stable keys, and credit-hold release.
- DemandPlanning consumer tests cover duplicate, out-of-order, changed, cancelled, invalid payload/dead-letter, multi-line projection, and explicit persistence.
- Real PostgreSQL + Redis acceptance starts ERP and DemandPlanning as separate processes and proves release, duplicate replay, out-of-order change, and cancellation convergence.
- MRP/pegging tests prove production suggestion traceability to `SO-DEMO-001`.
- Frontend tests prove the Planning drill-through and sales-page query filter.
- Required backend, frontend, schema, OpenAPI/codegen, script-governance, and formatting gates run before the ready PR is opened.
