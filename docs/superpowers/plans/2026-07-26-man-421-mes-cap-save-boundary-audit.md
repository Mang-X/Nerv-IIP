# MAN-421 MES CAP Save-Boundary Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce an exhaustive, PostgreSQL-backed audit and fix of every existing mutating BusinessMES CAP handler persistence boundary, excluding the already-fixed MAN-507 `AssetUnavailable` implementation.

**Architecture:** Preserve each existing consumer's architecture. Use its current explicit `SaveChangesAsync`, command UnitOfWork, or persistence scope coordinator as the atomic boundary; add only the missing boundary or business-exception normalization proven by a RED test. Observe durable outcomes exclusively through an independent `ApplicationDbContext`.

**Tech Stack:** .NET 10, EF Core 10, NetCorePal/CleanDDD, DotNetCore.CAP InMemory transport, xUnit, Docker PostgreSQL 18.

## Global Constraints

- Branch is `codex/man-421-mes-cap-save-boundary-audit` at exact stacked base `68dae3c8befabf0957eeb7f4449ea1d2027be332`.
- Pull request base is `codex/man-507-mes-cap-postgres-timeout`; do not merge.
- Do not redo or broaden MAN-507 / #920 `AssetUnavailable`; use it only as a passing baseline.
- Enumerate every current MES `IIntegrationEventHandler<T>` / `ICapSubscribe`, including all handlers sharing one file.
- Distinguish not invoked, invoked and failed, and invoked successfully but not saved.
- Preserve provider neutrality outside Infrastructure; do not add raw SQL or provider APIs to Domain, Application, Endpoint, or SDK.
- Do not add cross-schema foreign keys, endpoints, facades, OpenAPI changes, generated clients, schema changes, or migrations unless a proven requirement forces the full governance path.
- Use a self-started disposable Docker PostgreSQL 18 container named `nerv-man421-pg18-4da4` and label it `nerv.iip.owner=man-421-4da4`; inject the discovered connection string only into targeted tests and precisely remove that container at the end.
- Do not rely on a user-provided or pre-existing `NERV_IIP_TEST_POSTGRES`.
- Do not start `dotnet test backend/Nerv.IIP.sln` until the coordinating task explicitly releases the serialized full-gate slot.
- Critical and Important independent-review findings require a RED test, minimal fix, targeted verification, and scoped re-review.

---

### Task 1: Audit, fix, prove, and document all MES CAP save boundaries

**Files:**

- Create: `docs/architecture/mes-cap-handler-save-boundary-audit.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify only if semantics/classification changes: `docs/architecture/integration-event-consumption-matrix.md`
- Modify: proven-gap handlers under `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/`
- Create or modify: focused tests under `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/`

**Interfaces:**

- Consumes: current CAP topics, `IntegrationEventConsumerGuard<T>`, `MesProcessedIntegrationEventInbox`, `ApplicationDbContext`, persistent dead-letter store, command UnitOfWork and existing MES scope coordinators.
- Produces: one auditable row per current consumer; atomic durable MES state/inbox/dead-letter behavior; PostgreSQL CAP delivery and replay evidence for `AssetRestored`, `SchedulePlanReleasedForDispatch`, and every proven gap.

- [ ] **Step 1: Complete the source audit before editing production code**

  Enumerate all classes discovered by:

  ```bash
  rg -n --glob '*.cs' 'IIntegrationEventHandler<|ICapSubscribe|\[IntegrationEventConsumer' \
    backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers
  ```

  For each class, inspect mutations, save/UoW/coordinator boundary, inbox
  ordering, persistent dead letter, business exceptions, cancellation flow,
  duplicate/replay behavior, and current tests. Record the pre-change
  classification in the audit document. Treat coordinator-owned
  `SaveChangesAsync` as an equivalent boundary; do not add redundant saves.

- [ ] **Step 2: Start the disposable PostgreSQL 18 test database**

  Ensure no existing container owns the exact task name, then start:

  ```bash
  docker run -d --name nerv-man421-pg18-4da4 \
    --label nerv.iip.owner=man-421-4da4 \
    -e POSTGRES_USER=nerv \
    -e POSTGRES_PASSWORD=nerv-man421-local \
    -e POSTGRES_DB=nerv_iip \
    -P postgres:18
  ```

  Wait on `pg_isready`, discover the mapped `5432/tcp` port with
  `docker port`, and construct the temporary connection string in the current
  shell only.

- [ ] **Step 3: Write and run RED PostgreSQL/CAP regressions**

  Add focused tests that publish real events through CAP InMemory transport or
  invoke the real CAP subscriber boundary where existing harness structure
  makes that distinction explicit. Migrate the real MES schema, then use a new
  DI scope / independent `ApplicationDbContext` for every durable assertion.

  Required cases:

  - `AssetRestored` persists the closed window, optional reschedule result, and
    consumer inbox; replay does not create another result or inbox row.
  - `SchedulePlanReleasedForDispatch` persists assignment provenance and inbox
    through its scope coordinator; replay is idempotent.
  - Every additional audit-confirmed missing boundary persists its domain or
    projection mutation plus inbox and remains idempotent.
  - At least one save/unique-conflict case proves atomic rollback and
    non-poison-message behavior.

  Run the exact new tests before production edits and preserve output showing
  expected assertion failures caused by missing durable facts, not compilation
  or setup errors.

- [ ] **Step 4: Implement the minimal GREEN boundaries**

  Add the narrowest save/UoW boundary after all mutations for each RED-proven
  gap. Ensure inbox and the handler's domain/projection mutations commit in the
  same EF transaction. Normalize `ArgumentException`, `InvalidOperationException`
  or `KnownException` business divergence into an existing persistent
  dead-letter pattern when the audit proves it can escape and poison CAP.

  Keep `CancellationToken` on every async database/dead-letter call. Do not
  change `AssetUnavailable` beyond test references.

- [ ] **Step 5: Run focused GREEN and replay evidence**

  With only the temporary test process receiving
  `NERV_IIP_TEST_POSTGRES=<temporary-connection-string>`, run:

  ```bash
  dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj \
    --no-restore --nologo \
    --filter 'FullyQualifiedName~MesCapSaveBoundaryPostgresTests|FullyQualifiedName~MesCapSubscriptionTests.PostgreSQL_cap_with_inmemory_messaging_delivers_asset_unavailable_event_to_mes_consumer'
  ```

  If the final test class differs, use the exact equivalent filter and record
  every selected test name, passed/failed/skipped count, and exit code.

- [ ] **Step 6: Run the complete MES targeted gate**

  Run the full MES Web test project with the temporary PostgreSQL connection
  so all env-gated MES PostgreSQL cases execute. Classify any failure against
  the stacked base before changing code outside MAN-421.

  Do not run the full backend solution gate in this step.

- [ ] **Step 7: Finalize the audit matrix and readiness record**

  The matrix must include: handler/consumer, topic, mutations, boundary,
  inbox, business exception/dead-letter behavior, replay/duplicate semantics,
  independent-DbContext PostgreSQL evidence, and final verdict. Explicitly mark
  `AssetUnavailable` as MAN-507 baseline, not MAN-421 implementation.

  State whether endpoint/facade/OpenAPI, schema/migration, public contract, and
  product docs are unchanged. Update the integration consumption matrix only
  if handler semantics/classification actually changed.

- [ ] **Step 8: Clean the exact PostgreSQL container**

  Remove only `nerv-man421-pg18-4da4`, then verify no container with
  `label=nerv.iip.owner=man-421-4da4` remains. Do not remove other containers,
  volumes, networks, or user resources.

- [ ] **Step 9: Self-review and create focused commits**

  Inspect:

  ```bash
  git diff --check
  git status --short
  git diff --stat 68dae3c8befabf0957eeb7f4449ea1d2027be332..HEAD
  ```

  Commit only MAN-421 files with focused messages. Report the RED output, GREEN
  output, full MES gate output, exact cleanup evidence, commit IDs, and any
  concern in the SDD report file.
