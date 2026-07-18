# MAN-385 #701 Schedule Release Version Governance Design

## Goal

Deliver a complete Scheduling-to-MES release lifecycle in which a newly released schedule explicitly supersedes the previously active release in the same business scope, an active release can be revoked without a successor, and MES retains only current schedule provenance while preserving manual-dispatch locks.

The business scope is the existing Scheduling isolation boundary:

`organizationId + environmentId`

This change is strictly limited to GitHub #701. It does not implement the remaining #717 MES `SkuDisabled` consumer and does not modify AppHub, IndustrialTelemetry, or Connector Host files owned by open PR #952.

## Current Code Facts

1. `SchedulePlan` currently supports only `Generated` and `Released`; `ReleaseSchedulePlanCommand` releases one plan without finding or retiring another released plan in the same scope.
2. `schedule_plans` has only a globally unique `plan_id` index. There is no database invariant limiting active released plans per organization and environment.
3. Scheduling already publishes `scheduling.SchedulePlanReleased` through the CAP outbox, and MES consumes it by upserting real `OperationTask` aggregates.
4. MES operation tasks record `ScheduledAtUtc`, but not the schedule plan ID or a scope release revision. Consequently, MES cannot prove which release owns an assignment before clearing it.
5. MES already protects active manual dispatch through `ManualDispatchRevision` and `HasActiveManualDispatch`; released schedules update schedule timing/provenance without replacing active manual resource selection.
6. Schedule invalidation and MasterData calendar/resource invalidation consumption already exist and must remain intact. Invalidation is not a substitute for release revocation because it does not retire a released plan.
7. ADR 0011 requires public contracts, transactional outbox publication, durable consumer idempotency, controlled replay, and non-poison handling of unrecoverable business input.

## Chosen Architecture

Use database-governed plan lifecycle state plus a monotonic per-scope release revision.

Scheduling serializes release and revoke transitions for one `organizationId + environmentId` with a PostgreSQL transaction-scoped advisory lock implemented behind an Infrastructure interface. This avoids a new release-ledger aggregate while ensuring two service instances cannot both calculate and publish the next revision concurrently. A PostgreSQL partial unique index remains the final persistence invariant: at most one row in a scope may have status `Released`.

Each successful new release performs one transaction:

1. Acquire the scope transaction lock.
2. Reload the target plan and current active released plan in that scope.
3. Validate the target plan's conflicts, unscheduled operations, and existing invalidation markers.
4. If the target is already the active release, return its existing result without publishing another lifecycle fact.
5. If another active plan exists, transition it to `Superseded`, record the successor plan ID, and add a revoke domain event.
6. Allocate the next scope release revision and transition the target to `Released`.
7. Persist both plan changes and both outbox messages atomically.

An explicit revoke performs the same locked transaction. It transitions the current target from `Released` to `Revoked` and emits the same public revoke event without a successor. Repeating a revoke of an already revoked plan is a no-op that returns the original terminal state. A generated plan cannot be revoked because it has never affected MES.

## Scheduling Domain and Persistence Model

`SchedulePlanLifecycleStatus` gains:

- `Superseded`
- `Revoked`

`SchedulePlan` gains nullable lifecycle facts:

- `ReleaseRevision`: monotonic positive `long` assigned only when a plan first becomes released.
- `RevokedAtUtc`: the UTC instant when a released plan was superseded or explicitly revoked.
- `SupersededByPlanId`: the successor plan ID for automatic supersession; null for explicit revoke.
- `RevocationReason`: `Superseded` or `Explicit` for audit and event conversion.

The aggregate exposes idempotent `Release`, `Supersede`, and `Revoke` methods. It never changes `ReleaseRevision` after the first release and never releases a terminal superseded/revoked plan again.

The Scheduling migration will:

1. Add the lifecycle columns with table and column comments.
2. Deterministically normalize any historical scope containing multiple `Released` rows. The latest row is selected by `released_at_utc DESC NULLS LAST`, then `generated_at_utc DESC`, then `plan_id DESC`; older rows become `Superseded` and point to the selected active plan.
3. Backfill positive per-scope release revisions using the same deterministic chronological ordering.
4. Add a partial unique index on `(organization_id, environment_id)` where `status = 'Released'`.
5. Add a scope/revision unique index so two released-history rows cannot share a revision.

The advisory lock is provider-specific and therefore lives only in Scheduling Infrastructure. Application and Domain code depend on a narrow scope-lock abstraction and contain no raw SQL or Npgsql APIs. In-memory tests use a deterministic test implementation; PostgreSQL behavior is verified separately against a real database.

## Integration Event Contracts

Add `scheduling.SchedulePlanRevoked` v1 with a dedicated public contract in `Nerv.IIP.Contracts.Scheduling`.

The revoke payload contains:

- `planId`: the real revoked Scheduling plan ID.
- `releaseRevision`: the plan's per-scope release revision.
- `reason`: `superseded` or `explicit`.
- `supersededByPlanId`: the real successor plan ID for automatic supersession, otherwise null.
- Existing plan identity and algorithm facts needed for audit.
- The real affected operation assignments owned by the revoked plan.

The existing v1 released lifecycle payload gains an optional `releaseRevision`. ADR 0011 permits additive optional fields without a version bump. New Scheduling releases always set it; old archived v1 events remain deserializable.

Revoke event idempotency is based on scope, revoked plan ID, release revision, and revocation reason. Automatic supersession and explicit revoke therefore cannot accidentally share an idempotency key. The event converter uses the original domain facts and does not invent MES identifiers.

`integration-event-consumption-matrix.md` will declare Scheduling as producer and MES as the internal consumer, including inbox, ordering, replay, and DLQ behavior.

## MES Projection Semantics

MES `operation_tasks` gains:

- `schedule_plan_id`: nullable real Scheduling plan ID.
- `schedule_release_revision`: nullable positive scope release revision.

`ApplyScheduleAssignment` receives plan ID and release revision. For new governed events it applies only when the incoming revision is not older than the task's current revision. It records schedule provenance even when an active manual dispatch lock prevents the released schedule from replacing the manually selected resource.

The revoke consumer uses the durable MES integration-event inbox. For each affected real operation ID:

1. If the task does not exist, safely ignore it; revoke does not manufacture downstream tasks.
2. If the task's current plan ID and release revision do not match the revoked plan, safely ignore it. This prevents a late old revoke from clearing a newer release.
3. If the task matches, clear schedule plan provenance and `ScheduledAtUtc` and mark the queued task schedule-invalidated with a revoke reason.
4. Preserve active manual dispatch fields and their revision/tombstone semantics.
5. Preserve in-progress, paused, completed, and cancelled execution facts; revocation cannot erase execution history.

This ordering rule gives the desired result even when the new release event is delivered before the old revoke event:

- Operations present in the new plan already point to the new plan, so the old revoke becomes a no-op for them.
- Operations omitted from the new plan still point to the old plan, so the old revoke clears and invalidates them.

The released consumer will convert invalid windows and other unrecoverable business payload failures into persistent DLQ records instead of throwing retryable business exceptions. The revoke consumer similarly uses DLQ or safe no-op outcomes and does not create a poison-message retry loop.

Legacy released events without `releaseRevision` remain compatible: they can populate a task only when no governed schedule revision is already present. They can never overwrite a task that already carries a positive governed revision.

## HTTP and Facade Contract

Add an authenticated Scheduling endpoint:

`POST /api/business/v1/scheduling/plans/{planId}/revoke`

The request carries organization/environment scope. The response returns plan ID, terminal status, release revision, revoke timestamp, reason, and optional successor ID.

Declare the endpoint `exposed` in `facade-coverage-matrix.json`. Deliver the BusinessGateway facade in the same PR, export the governed OpenAPI snapshots, run `pnpm -C frontend generate:api`, and use only generated client artifacts. Existing release responses are extended compatibly with release revision and terminal lifecycle status values.

No new UI page is required by #701. Product documentation will describe the release/revoke lifecycle exposed to scheduling clients.

## Error Handling and Concurrency

1. Scope mismatch and missing plan remain known request errors at the synchronous command boundary.
2. Releasing a plan with error conflicts, unscheduled operations, or invalidation markers remains rejected before any old active plan is superseded.
3. The PostgreSQL scope lock serializes concurrent release/revoke commands across service instances.
4. The partial unique index protects the invariant even if the coordination layer regresses.
5. A unique-constraint violation is translated to a stable concurrency error rather than an unhandled 500.
6. CAP outbox publication remains in the same database transaction as lifecycle state changes.
7. Consumer replay retains the original event ID and is absorbed by the inbox. A distinct late event is constrained by plan ID and release revision checks.

## Verification Strategy

### TDD unit and service integration coverage

1. Domain tests first define release, supersede, explicit revoke, terminal-state, and idempotency behavior.
2. Scheduling handler tests prove validation happens before supersession and that sequential releases produce old-revoke/new-release facts.
3. Contract serialization tests prove stable event type, v1 payload fields, optional released revision compatibility, and revoke idempotency keys.
4. MES aggregate and consumer tests prove governed release ordering, exact-plan revoke matching, manual-dispatch preservation, terminal execution preservation, missing-task no-op, DLQ behavior, and replay idempotency.
5. A cross-service behavior test uses real Scheduling commands/domain events/converters and real MES handlers/entities. It releases plan A, releases plan B, consumes the resulting facts, and proves all affected MES tasks either point to plan B or have old scheduling facts cleared. It then explicitly revokes plan B and replays events to prove idempotency. Assertions inspect persisted business facts, not event metadata alone.

### Real PostgreSQL coverage

Against `NERV_IIP_TEST_POSTGRES`:

1. Apply the migration to seeded duplicate historical released rows and verify deterministic normalization and DateTimeOffset ordering.
2. Verify the partial unique index rejects two active released rows in one scope while allowing independent scopes.
3. Run concurrent release commands from separate DbContexts/connections and prove they serialize to one active release with monotonic revisions and correct supersession.
4. Verify the exact database predicates used for released status and UTC ordering translate and execute on PostgreSQL.

SQLite and InMemory tests are supplementary only and do not count as evidence for these invariants.

### Gates

Run, at minimum:

- Scheduling Domain and Web test projects.
- MES Domain and Web test projects.
- Scheduling/MES cross-service acceptance coverage.
- PostgreSQL profile tests with `NERV_IIP_TEST_POSTGRES` configured.
- `scripts/verify-business-scheduling-aps-lite.ps1` when its prerequisites are available.
- `dotnet test backend/Nerv.IIP.sln` as the backend solution gate.
- Facade coverage tests.
- Governed OpenAPI export and `pnpm -C frontend generate:api` if the exposed endpoint is delivered as designed.

## Documentation and Schema Impact

Update in the same PR:

- `docs/architecture/integration-event-consumption-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/facade-coverage-matrix.json`
- `docs/architecture/facade-coverage-matrix.md` if its narrative requires a new row or status note
- Relevant Scheduling product/API documentation under `frontend/apps/docs`
- Scheduling and MES migrations, model snapshots, table/column comments, and schema convention tests

## Out of Scope

- MES consumption of MasterData `SkuDisabled` or any other remaining #717 item.
- Changes to AppHub, IndustrialTelemetry, or Connector Host files from PR #952.
- New scheduling UI workflows beyond exposing the governed API contract.
- Changing existing schedule invalidation sources or manual-dispatch lock ownership.
- Merging the PR after creation.
