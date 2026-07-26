# MAN-421 MES CAP Save-Boundary Audit Design

## Objective

Audit every current BusinessMES `IIntegrationEventHandler<T>` / `ICapSubscribe`
consumer that can mutate MES state, prove its trigger and persistence behavior,
and close every save-boundary gap without redoing MAN-507 / #920
`AssetUnavailable`.

The delivery is stacked on
`codex/man-507-mes-cap-postgres-timeout` so the MAN-421 pull request contains
only the remaining audit, regression coverage, fixes, and documentation.

## Evidence model

Every consumer is classified with separate evidence for:

1. subscription and scheduling: whether CAP can invoke the consumer;
2. handler outcome: returned successfully, threw, or was rejected by the
   envelope guard;
3. persistence outcome: inbox, domain/projection mutations, dead letter, and
   replay result as observed from an independent `ApplicationDbContext`;
4. boundary: explicit `SaveChangesAsync`, command UnitOfWork, or an existing
   scope coordinator that saves and commits;
5. failure behavior: save failure or uniqueness conflict must not leave partial
   business facts or an acknowledged poison message.

This prevents “not invoked”, “invoked and failed”, and “invoked successfully but
not saved” from being collapsed into the same timeout symptom.

## Chosen approach

Keep the current consumer and persistence architecture. Add the narrowest
explicit save to consumers or valid branches that mutate the scoped MES
`ApplicationDbContext` but have no equivalent command/UnitOfWork/coordinator
boundary. Existing coordinator-backed consumers remain unchanged and receive
regression evidence instead of duplicate saves. Business divergences that can
be represented as terminal dead letters are persisted without leaking
`ArgumentException`, `InvalidOperationException`, or `KnownException` into CAP
retry.

Alternatives rejected:

- Rewriting all consumers to commands would widen the change across handler,
  mediator, and command contracts without improving the current issue's
  observable boundary.
- Adding a generic CAP auto-save interceptor would obscure which consumers own
  atomicity and risk committing read-only/no-op handlers or unrelated tracked
  changes.
- Testing only EF InMemory or the handler's original `DbContext` would preserve
  the false-positive pattern that allowed this defect class to survive.

## Test design

Start one disposable PostgreSQL 18 container named
`nerv-man421-pg18-4da4` with label `nerv.iip.owner=man-421-4da4`, discover its
random host port, and inject its temporary connection string only into the
targeted test process. Never rely on a pre-existing
`NERV_IIP_TEST_POSTGRES`.

Write RED tests before production edits. At minimum:

- `AssetRestored`: a CAP delivery closes the open unavailability, persists the
  reschedule result and business inbox, and replay creates no duplicate fact;
- `SchedulePlanReleasedForDispatch`: CAP delivery persists task assignment and
  inbox through the existing coordinator, and replay remains idempotent;
- every additional audit-confirmed gap: observe the intended mutation and inbox
  from an independent `ApplicationDbContext`, then replay the same event;
- representative save/unique-conflict failure: no partial inbox/domain fact and
  no silent poison-message acknowledgement.

The existing MAN-507 `AssetUnavailable` regression is run as a baseline only.

## Documentation and governance

Add a durable MES CAP handler audit matrix under `docs/architecture/` with one
row per consumer and the evidence fields above. Update
`implementation-readiness.md` with the delivered MAN-421 boundary. Update
`integration-event-consumption-matrix.md` only if consumer semantics or its
classification changes.

No HTTP endpoint, facade, OpenAPI contract, generated client, schema, or
migration change is intended. The pull request must state facade and migration
governance are not applicable, and product documentation has no impact.
