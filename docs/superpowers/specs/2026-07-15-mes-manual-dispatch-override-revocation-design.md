# MES Manual Dispatch Override Revocation Design

**Issue:** #933

**Date:** 2026-07-15

**Status:** Design approved; awaiting written-spec review

## Purpose

PR #923 made a real MES manual device dispatch create a durable Scheduling
operation override. MES can already clear the device through the same dispatch
command, and work-order cancellation cancels its open operation tasks, but neither
transition publishes the inverse fact. Scheduling therefore keeps the old resource
and time window locked indefinitely.

This design closes only that manual-dispatch lifecycle. It does not implement
released-plan supersede or revoke governance from #701, nor automatic cleanup for
completion, timeout, or schedule release.

## Current Failure

`OperationTask.Assign` publishes `OperationTaskManuallyDispatched` only when the
new device is non-null. Clearing the device updates MES persistence without an
event. `OperationTask.Cancel` likewise changes only MES state. Scheduling stores
only active override rows and overlays every row into the next scheduling problem.

Physically deleting the row is insufficient. A delayed old dispatch event can
arrive after deletion and recreate the stale lock. The consumer must retain a
watermark that represents the revocation.

## Decisions

### 1. MES owns a monotonic manual-dispatch revision

Each operation task persists:

- `ManualDispatchRevision`, starting at zero;
- `HasActiveManualDispatch`, starting false.

The revision advances for each manual-device lifecycle fact:

- assigning or reassigning a real device publishes an active dispatch fact;
- changing a real manual device assignment to no device publishes a cleared fact;
- cancelling an operation task with an active manual dispatch publishes a cleared
  fact;
- assignments that never had a manual device and repeated clears do not fabricate
  revocations.

Released schedule assignments are not manual dispatches and must not set
`HasActiveManualDispatch` or advance this revision. This prevents cancellation of a
scheduled device assignment from being misreported as cancellation of a MES manual
override.

The migration cannot safely infer whether an existing device assignment came from
MES manual dispatch or a released schedule, so existing rows are initialized as
revision zero and inactive (`legacy-unknown`). Such a row is not treated as a live
manual lock during ordinary scheduling. If its device is explicitly cleared or the
operation is cancelled, MES conservatively emits revision 1 clear/tombstone facts;
this prevents an already-projected legacy lock from surviving. A later new manual
dispatch then advances to revision 2. No historical row is guessed active during
the upgrade.

The domain events snapshot the revision and the affected resource/window facts.
They must not depend on reading a later mutable aggregate state during conversion.

### 2. Add a separate versioned cleared event

The public MES contract adds:

- event type `mes.OperationTaskManualDispatchCleared`;
- envelope `MesOperationTaskManualDispatchClearedIntegrationEvent`;
- payload `OperationTaskManualDispatchClearedPayload`;
- event version `1` under the existing MES envelope convention.

The payload contains the real work-order ID, operation-task ID, operation sequence,
previous resource ID, work-center ID, previous scheduling window, monotonic dispatch
revision, clear reason, and cleared time. Clear reasons are normalized constants for
at least `device-cleared` and `operation-cancelled`.

The existing dispatched payload gains an optional/backward-compatible dispatch
revision field. Newly published events always populate a positive revision. Legacy
events with no revision remain consumable through the existing source-time fallback.

Every cleared envelope carries:

- the authenticated actor from the command path;
- `OccurredAtUtc` equal to the MES transition time;
- correlation and causation from an HTTP/activity-aware MES integration-event
  context accessor, with generated non-empty fallbacks for background/test paths;
- an idempotency key containing organization, environment, operation, revision,
  and the clear action.

The work-order cancellation endpoint propagates its authenticated actor into the
command/orchestrator. The existing HTTP contract does not change.

### 3. Scheduling keeps an inactive tombstone

`schedule_operation_overrides` adds:

- `is_active`, defaulting existing rows to true;
- nullable `source_revision` for MES lifecycle ordering;
- nullable `cleared_reason_code`;
- nullable `cleared_at_utc`.

A cleared event never hard-deletes the row. If no row exists, Scheduling creates an
inactive tombstone from the real facts carried by the event. If a row exists, the
aggregate applies the event only when it is current according to the rules below.

The overlay queries active rows only. Assemble, preview, and create therefore all
stop fixing the operation as soon as the accepted revocation is persisted.

### 4. Ordering and ownership rules

MES lifecycle events for the same operation use `ManualDispatchRevision` as their
primary order. A higher revision wins regardless of delivery order or equal client
timestamps. A lower revision is recorded by the consumer inbox but cannot mutate
the current projection. Replaying the same event is an idempotent success.

For legacy revision-less dispatch events, `SourceOccurredAtUtc` remains the fallback
ordering watermark. An inactive tombstone with a newer source time cannot be
reactivated by an older legacy dispatch.

An MES cleared event may deactivate only a current MES-dispatch lineage or advance
an existing MES tombstone. It must not deactivate an override whose current source
is `scheduling-api`. A later Scheduling API adjustment therefore remains protected
from delayed MES cancellation. A later positive MES dispatch may replace the
current projection under the existing positive-event semantics and starts a new MES
lineage.

`create -> clear -> reassign` converges as follows:

1. dispatch revision 1 creates an active override;
2. clear revision 2 makes the same row inactive;
3. reassign revision 3 reactivates it with the new resource/window;
4. delayed revisions 1 or 2 cannot alter revision 3;
5. clear revision 2 arriving before dispatch revision 1 creates a tombstone, and
   revision 1 cannot revive it.

Equal source timestamps are therefore deterministic for new events because the
revision, not arrival order, decides the result.

### 5. Consumer failure behavior

The cleared consumer uses the existing `IntegrationEventConsumerGuard`, Scheduling
processed-event inbox, dead-letter store, and save-race retry pattern.

- Invalid envelopes, unsupported versions, missing identities, invalid revisions,
  or invalid prior windows go to the Scheduling DLQ once.
- A valid clear for an absent override creates a tombstone and succeeds.
- A duplicate, stale, or lineage-mismatched clear is a successful no-op and never a
  poison message.
- Insert/update races clear and reload the EF tracker, re-record the inbox entry as
  needed, and reapply the same deterministic revision rule.

## Persistence and Documentation

Formal PostgreSQL migrations update MES operation tasks and Scheduling operation
overrides. Both migrations include table/column comments and update model snapshots.
Schema convention tests and `database-schema-catalog.md` are updated.

`integration-event-consumption-matrix.md` gains the new MES cleared event and its
Scheduling consumer, including tombstone, idempotency, ordering, and DLQ semantics.
`implementation-readiness.md` records the delivered #933 lifecycle while preserving
the explicit #701 boundary. The #700 design is amended only where it previously
listed all automatic removal as out of scope.

No HTTP endpoint is added or changed, so facade coverage and OpenAPI/code generation
are not affected.

## Test Strategy

Implementation follows red-green-refactor in these layers:

1. **MES domain and converter tests** prove real-device assignment, device clear,
   work-order cancellation, no fabricated repeated clear, revision increments,
   actor, lineage, idempotency, and equal-time distinction.
2. **Scheduling domain tests** prove active-to-inactive transition, tombstone-first
   delivery, stale dispatch rejection, reactivation by a higher revision, protection
   of Scheduling API overrides, and deterministic equal-time ordering.
3. **Scheduling consumer tests** prove duplicate, invalid, stale, out-of-order, and
   two-context concurrent delivery behavior, including DLQ and inbox counts.
4. **Overlay/scheduler tests** prove inactive rows are excluded and the next run has
   `IsLocked = false`, zero locked operations, and one optimizable operation without
   retaining the old resource/window.
5. **Cross-boundary acceptance** extends the existing canonical MES-to-Scheduling
   test through real MES command, domain event, converter, Scheduling consumers,
   overlay, and scheduler for establish-lock, revoke-lock, and reassign flows.
6. **PostgreSQL-gated coverage**, where the repository test profile is available,
   exercises the real unique index and optimistic-concurrency retry. The always-on
   acceptance test remains deterministic and does not require Docker.

Targeted MES, Scheduling domain/Web, contracts, business acceptance, schema, and
full backend solution gates are run before PR creation. Environment-gated PostgreSQL
tests are reported separately if the required test database is unavailable.

## Acceptance Mapping

- Clearing the device or cancelling its operation after a MES manual dispatch
  persists a Scheduling tombstone and removes the lock from the next run.
- The next run is free to optimize instead of retaining the old resource/window.
- Duplicate, delayed, equal-time, and out-of-order events converge by revision and
  cannot remove a newer adjustment or re-dispatch.
- The cross-boundary acceptance test proves establish lock, revoke lock, and resume
  optimization through real MES and Scheduling code paths.
