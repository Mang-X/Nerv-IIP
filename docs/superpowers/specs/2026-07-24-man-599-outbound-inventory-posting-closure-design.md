# MAN-599 Outbound Inventory Posting Closure Design

## Scope

This design fixes only GitHub #1083 / Linear MAN-599. It closes the ERP delivery
order → WMS outbound order → Inventory posting → ERP receivable/voucher chain.
It does not implement equipment health (#1087), unrelated WMS operations, or a
new inventory ownership model.

## Code-fact baseline

The finished-goods receipt leg posts the run-scoped lot into Inventory with the
key:

`(finished-goods, receiving, producedLotNo, null, unrestricted, production, null)`.

ERP already owns an authoritative sales-order `SiteCode`, and delivery-order
lines already persist SKU, UOM, location, and lot. The broken outbound event
replaces the site with `null`; WMS then replaces it with `default` and changes
the owner to `company/customerCode`. WMS also publishes
`OutboundOrderCompleted` before Inventory accepts the movement, which lets ERP
complete the delivery and create financial facts before stock changes.

## Considered approaches

1. **Gateway-only correlation.** Aggregate the current ERP, WMS, and Inventory
   reads and label contradictions. This would make the defect visible but would
   preserve wrong stock and premature finance, so it is rejected.
2. **Change the MES finished-goods posting key.** Reclassify produced stock as
   company-owned and/or move its site. This expands into MES and historical
   stock migration, contrary to the issue boundary, so it is rejected.
3. **Preserve the producer key and delay business completion until posting.**
   Carry the authoritative ERP site and line location/lot into WMS, consume the
   existing `production/null` finished-goods owner bucket, and publish WMS
   completion only after all current line requests are posted. This is the
   selected approach because it fixes both the accounting key and the financial
   ordering without inventing facts or widening scope.

## Domain and persistence changes

ERP `DeliveryOrder` snapshots `SalesOrder.SiteCode` in a new non-null
`erp.delivery_orders.site_code` column. The migration backfills existing
delivery rows from their source sales orders in the same ERP schema and aborts
with an explicit orphan-row count before the non-null transition when the
authoritative site cannot be recovered. The
delivery write facade accepts `locationCode` and `lotNo`; the read facade
returns header `siteCode` and line SKU, UOM, location, and lot.

WMS adds `InventoryPostingPending` as a new enum value without renumbering the
existing persisted enum values. Pack review records executed quantities and
creates movement requests, but leaves the order pending and emits no completion
event. A posted callback marks its request posted, selects the latest request
per outbound line, and completes the outbound only when every current line is
posted. A rejected callback marks the order failed. Retry moves the order back
to pending; old failed attempts remain auditable and do not block a later
successful current attempt.

Every WMS outbound aggregate mutation advances a persisted optimistic
concurrency token. Concurrent line-posted callbacks therefore cannot both
commit stale all-lines-pending decisions: one conflicts, CAP retries that
event, and the retry reloads the latest requests before deciding completion.
The command and query paths share one latest-attempt selector ordered by
`CreatedAtUtc` and the underlying Guid v7 identifier.

The ERP-created WMS outbound uses:

- `SiteCode`: the delivery snapshot copied from the sales order;
- `LocationCode` and `LotNo`: the explicit delivery-line values;
- `QualityStatus`: `unrestricted` through Inventory normalization;
- `OwnerType` / `OwnerId`: `production` / `null`, matching the existing
  finished-goods receipt bucket.

A missing ERP site fails closed in the WMS consumer by writing the event to the
persistent dead-letter store and returning without a retry exception; it never
falls back to `default`.

## Public failure visibility and recovery

The WMS outbound list returns header site, posting status, completion time, and
line-level SKU/UOM/location/lot/quality/owner plus the current movement request
status, failure code, and failure message. BusinessGateway forwards those
facts without interpreting them.

BusinessGateway exposes the existing WMS outbound posting retry through:

`POST /api/business-console/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry`

using `business.wms.shipments.manage`. The WMS service endpoint changes from
`deferred` to `exposed` in the facade coverage matrix. The BusinessGateway
OpenAPI snapshot and `@nerv-iip/api-client` generated output are refreshed from
the real endpoint.

## Financial closure

ERP continues to project shipment quantities and create the receivable only
from `WmsIntegrationEventTypes.OutboundOrderCompleted`. Because WMS now emits
that event only after Inventory accepts every current outbound movement, an
Inventory rejection leaves:

- WMS outbound: `InventoryPostingFailed` with a public failure reason;
- ERP delivery: not completed;
- account receivable and journal voucher: absent.

A successful retry completes WMS once, then the existing ERP inbox and
idempotency guards complete the delivery and create exactly one receivable and
voucher.

## Verification

Unit and contract tests cover the exact key propagation, missing-site
fail-closed behavior, pending/failed/posted transitions, multi-line last-posted
completion, concurrent posted-callback conflict and retry completion, public
failure fields, retry authorization, and OpenAPI operation/schema fields.

`leader-demo-main-chain` must release the delivery with the exact produced lot and
receiving location, waits for WMS posting success, verifies the finished-goods
balance decreases from 10 to 0, and only then accepts ERP completed,
receivable, and posted voucher facts. The managed full-stack run must supply the
required PostgreSQL and cross-process Redis evidence.
