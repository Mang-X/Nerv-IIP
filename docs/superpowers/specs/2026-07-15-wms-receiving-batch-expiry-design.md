# WMS receiving batch and expiry capture design

## Scope

This change closes GitHub issues #926 and #927 in one delivery. It exposes the
production and expiry dates already stored on WMS inbound lines and allows the
existing inbound-completion operation to persist line-level batch facts captured
at the receiving station.

The PDA interaction work tracked by #813 is not included. This change delivers
the generated contract that #813 can consume. No database migration is required:
the WMS aggregate and schema already contain `lot_no`, `production_date`, and
`expiry_date`.

## API design

The existing WMS and BusinessGateway create-inbound line models expose optional
`productionDate` and `expiryDate`. The service already accepts these fields, so
the missing Gateway hop is added without changing create semantics.

The existing complete-inbound request gains an optional `lines` collection. Each
capture contains:

- `lineNo`, the stable inbound-line business key;
- optional `lotNo`;
- optional `productionDate`;
- optional `expiryDate`.

An omitted or empty collection preserves the existing completion contract. A
separate receive-line endpoint is intentionally not introduced because the
capture and completion must be atomic and use the existing completion
`idempotencyKey`.

The receiving-quality-gate item adds optional `productionDate` and `expiryDate`.
`daysUntilExpiry` is not returned because it is time-dependent presentation data
and the frontend already owns the shared expiry-tone calculation.

All changed WMS endpoints remain declared `exposed`. Their existing rows in
`facade-coverage-matrix.json` remain authoritative; the coverage gate, rather
than a no-op JSON edit, verifies the declaration.

## Domain behavior

Before an open inbound order completes, the aggregate applies the supplied
captures by `lineNo`, then creates Inventory movement requests from the final
line facts in the same transaction.

The aggregate rejects:

- duplicate captured `lineNo` values;
- a captured line that does not exist on the inbound order;
- an expiry date earlier than its production date;
- capture attempts after the inbound order has left the open state.

Captured nullable values are authoritative for the submitted line. Retrying the
same request with the same idempotency key remains the caller's responsibility;
the existing immutable completed-order behavior prevents a later request from
silently rewriting completed receipt facts.

## Data flow

1. An upstream caller may prefill batch dates when creating an inbound order.
2. A receiving client may send final line captures with complete-inbound.
3. WMS applies captures and completes the order atomically.
4. Inventory movement requests carry the final lot and date values.
5. The quality-gate query returns the persisted dates through BusinessGateway.
6. BusinessGateway OpenAPI and the generated API client expose all new fields.

## Verification

Test-driven changes cover domain capture, unknown and duplicate lines, invalid
date order, backward-compatible completion without captures, Inventory movement
propagation, service endpoint binding, Gateway proxying, quality-gate projection,
OpenAPI generation, generated-client exports, and facade coverage.

Focused WMS and BusinessGateway tests run during implementation. Before delivery,
the affected backend solution gates and frontend typecheck/test/build gates are
run according to `AGENTS.md`.
