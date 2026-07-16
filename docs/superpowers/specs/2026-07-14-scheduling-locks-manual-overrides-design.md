# Scheduling Locks and Manual Overrides Design

## Goal

Implement MAN-384 / #700 so a generated schedule can preserve selected operations,
manual time or resource adjustments survive later replanning, and MES manual dispatch
creates a real Scheduling lock instead of an untracked execution-side assignment.

This work builds on the schedule invalidation behavior merged in PR #904. It does not
replace, clear, or bypass the existing invalidation markers or release gate.

## Scope

This design delivers:

1. Base-plan operation locking by operation ID, with the original assignment's exact
   resource and UTC time window used as a fixed scheduling constraint.
2. Persistent Scheduling-owned operation overrides for a single operation's resource
   and UTC time window.
3. A BusinessGateway-exposed manual override API.
4. A versioned MES manual-dispatch integration event consumed idempotently by
   BusinessScheduling.
5. Schedule KPIs whose optimization-space denominator explicitly excludes locked
   operations.
6. A real cross-service test from a persisted MES operation task through the emitted
   event to a persisted Scheduling override and a subsequent schedule result.

The following remain out of scope:

1. #701 release/revoke or schedule version governance.
2. #717 remaining MasterData consumers.
3. A Gantt drag-and-drop frontend implementation owned by #78.
4. Automatic cleanup of overrides based on schedule release/revoke, operation
   completion, or timeout.
5. Changes to PR #904's schedule invalidation semantics.

## Existing Code Facts

The shared `SchedulingProblemContract` already contains full
`SchedulingLockedAssignmentContract` records. The finite-capacity scheduler reserves
them before open operations, retains their resource and time window, and reports
`InvalidLockedAssignment` when a fixed assignment violates resource, horizon,
calendar, availability, or capacity constraints.

The missing behavior is upstream of that pure scheduling rule:

1. `AssembleSchedulingProblemRequest` accepts caller-provided locked assignments but
   cannot resolve selected operation IDs from a base plan.
2. BusinessScheduling has no durable manual override fact.
3. MES `AssignDispatchTask` changes an `OperationTask` without notifying Scheduling.
4. Released schedules can overwrite queued MES manual assignments.
5. Current tardiness and on-time metrics include locked operations in the apparent
   optimization space.
6. `schedule_problems` currently persists fingerprint and horizon metadata but not
   the normalized problem payload required to validate later manual adjustments.

Scheduling and MES do not have service-local `AGENTS.md` files in the current tree, so
the repository root instructions govern both service areas.

## Ownership and Boundaries

BusinessScheduling owns operation locks and overrides. MES continues to own work
orders, operation tasks, dispatch, and execution state. BusinessGateway only proxies
the exposed override operation and does not store or calculate scheduling facts.

An override is a current scheduling input fact, not a new schedule version. Upserting
an override therefore does not add release/revoke/version lifecycle behavior from
#701.

The Scheduling API is the authoritative manual time/resource adjustment path. MES
manual dispatch produces the same Scheduling fact asynchronously when it has a real
device and valid scheduled window.

## Persistent Override Model

Add `scheduling.schedule_operation_overrides` with a unique business key on:

`organization_id + environment_id + operation_id`.

The row stores:

| Field | Purpose |
| --- | --- |
| `work_order_id` | Real MES/Scheduling work-order public ID. |
| `operation_id` | Real operation/OperationTask public ID. |
| `operation_sequence` | Stable operation ordering fact. |
| `resource_id` | Fixed executable resource. |
| `work_center_id` | Fixed work center associated with the resource. |
| `start_utc` / `end_utc` | Exact locked scheduling window. |
| `lock_reason_code` | Explainable lock origin such as manual Scheduling adjustment or MES dispatch. |
| `source_type` | Scheduling API or MES integration-event origin. |
| `source_event_id` | Event ID for MES-origin audit and replay evidence. |
| `actor` | Request actor or integration-event actor. |
| `source_occurred_at_utc` | Ordering timestamp used to reject stale event overwrites. |
| `updated_at_utc` | Persistence audit timestamp supplied through `TimeProvider`. |

The entity validates required identifiers and requires `end_utc > start_utc`. A newer
manual API write may replace an existing override. An MES event replaces an existing
override only when its source occurrence time is not older than the stored fact.
Duplicate delivery remains idempotent through the consumer inbox and the unique
business key.

Add a required JSON `problem_json` column to `scheduling.schedule_problems`. New plan
creation stores the normalized `SchedulingProblemContract` used for fingerprinting.
This payload is the service-owned validation source for later override requests and
base-plan operation locks; callers are not asked to resend or reconstruct historical
operation/resource identities. Existing rows are migrated with a valid empty JSON
object default for schema compatibility, and commands that require historical problem
details return an explicit business error for such legacy rows instead of guessing.

The migration must configure table and column comments, update the EF model snapshot,
and be reflected in `database-schema-catalog.md`.

## HTTP and Gateway Contract

Add the exposed service endpoint:

`PUT /api/business/v1/scheduling/plans/{planId}/operations/{operationId}/override`

The request contains organization/environment scope, the desired `resourceId`,
`startUtc`, and `endUtc`. It does not accept caller-provided work-order ID, operation
sequence, work-center ID, assignment ID, or other downstream identities.

The handler loads the scoped plan and its persisted normalized Scheduling problem JSON,
finds the requested operation, and derives:

1. work-order ID and operation sequence from the problem,
2. work-center ID from the requested resource,
3. the override's stable operation identity from the route.

Unknown plans, operations, or resources; invalid windows; and cross-tenant access are
explicit business errors. A resource must be eligible for the operation in the
persisted problem. Calendar, availability, or finite-capacity conflicts do not reject
the override: the fixed fact is retained and the scheduler reports an explainable
locked-assignment conflict.

BusinessGateway exposes the equivalent business-console facade. The endpoint is
registered as `exposed` in `facade-coverage-matrix.json`; governed OpenAPI export and
`pnpm -C frontend generate:api` refresh the generated client. Generated snapshots and
client files are never edited by hand.

## Base-Plan Locks and Problem Assembly

Extend assembly input with optional `BasePlanId` and `LockedOperationIds`.

When supplied, the command loads the scoped base plan and converts each selected
assignment into a full `SchedulingLockedAssignmentContract`, preserving its exact
resource, work center, start, end, order, and sequence. A requested operation absent
from the base plan is a business error rather than a silently ignored lock.

Problem input is merged in this order:

1. resolved base-plan locked assignments,
2. any existing compatible explicit locked assignments retained for backward
   compatibility,
3. persisted overrides, which replace a lock for the same operation.

Persisted overrides are also overlaid before preview/create scheduling so callers
cannot bypass them by posting a previously assembled problem. Overrides whose
operation is not present in the current problem are ignored for that run but remain
durable for a future compatible problem. The pure finite-capacity scheduler remains
database- and clock-free.

## MES Manual Dispatch Closure

After `AssignDispatchTask` successfully assigns a queued or otherwise assignable real
MES `OperationTask`, MES emits a versioned integration event containing:

1. organization and environment,
2. real `WorkOrderId` and `OperationTaskId`,
3. operation sequence,
4. real `DeviceAssetId` and work center,
5. the task's current `EarliestStartUtc` and `EarliestStartUtc + Duration`,
6. assigned timestamp, the authenticated dispatching principal (not the request-body
   assignee), and normal ADR 0011 envelope identifiers.

MES emits the lock event only when a non-empty device ID and a valid positive-duration
window exist. A user-only or shift-only dispatch still succeeds in MES but does not
invent a Scheduling resource or time window.

BusinessScheduling consumes the event with its existing consumer guard and processed
event inbox. Structurally invalid events are dead-lettered and return normally so a
business validation error does not become a poison message. Valid events upsert the
same Scheduling override model used by the HTTP endpoint. A stale event does not
replace a newer override.

The public event contract and producer/consumer relationship are recorded in
`integration-event-consumption-matrix.md`.

PR #904 remains authoritative for invalidation. Creating or updating an override does
not clear MES `ScheduleInvalidated`, does not reset Scheduling invalidation records,
and does not weaken the release gate.

## KPI Semantics

Plan-wide operational metrics remain plan-wide:

1. scheduled and unscheduled operation counts,
2. assigned minutes,
3. makespan,
4. resource load and average resource utilization.

Add `LockedOperationCount` and `OptimizableOperationCount` to the public metrics
contract and persisted plan metrics. Optimization outcome metrics are calculated only
from non-locked scheduled assignments:

1. `TotalTardinessMinutes`,
2. `LateOperationCount`,
3. `OnTimeRate`.

When there are no optimizable scheduled assignments, `OnTimeRate` is `1.0`. This makes
the denominator explicit and prevents fixed historical/manual facts from appearing as
algorithm-controlled optimization opportunity.

## Error Handling and Concurrency

HTTP validation failures use the established business-error path and do not produce
500 responses. A locked assignment that is structurally valid but infeasible is
preserved and reported as a schedule conflict.

The MES consumer follows these rules:

1. envelope/version failures use the existing dead-letter guard,
2. missing required payload values or invalid windows are dead-lettered and return,
3. duplicate events are ignored by the inbox,
4. stale events return without changing the override,
5. unique-key save races converge on the existing row and must not poison the CAP
   subscription.

All repository and handler database operations are asynchronous and accept a
`CancellationToken`.

## Verification

### Unit and contract tests

1. Locked assignments keep resource and time unchanged after replanning.
2. Locked assignments still report infeasible-lock conflicts.
3. Tardiness, late count, and on-time rate ignore locked assignments while plan-wide
   metrics remain complete.
4. The metrics and MES integration event serialize with stable camel-case contract
   values.
5. Endpoint contracts, authorization, operation IDs, and Gateway facade shapes are
   governed.

### Persistence tests

1. HTTP override creation and replacement persist the derived real identities.
2. A stale MES event cannot overwrite a newer override.
3. Duplicate MES event delivery is idempotent.
4. A later problem assembly and direct preview/create both merge the persisted
   override.
5. Scheduling and MES schema convention tests cover new columns/comments.

### Cross-service acceptance test

The acceptance test creates a real MES work order and operation task, executes the MES
manual dispatch command, converts its real domain event to the public integration
event, and invokes the real Scheduling consumer against Scheduling persistence. It
then generates a subsequent plan from a problem containing the same operation and
asserts that resource, start, and end match the manual dispatch lock.

The test does not synthesize a downstream operation ID or assert only metadata. It
uses the public IDs stored by MES and the persisted Scheduling override consumed by
the next scheduling run.

### Required gates

Run the Scheduling, MES, scheduling-contract, BusinessGateway, facade-coverage,
schema-convention, and cross-service focused tests. Then run `dotnet test
backend/Nerv.IIP.sln`. Export OpenAPI through the governed script and run `pnpm -C
frontend generate:api`. Run targeted frontend typecheck/tests affected by generated
client or docs changes. Full-stack smoke is required only if the endpoint cannot be
adequately validated through service/Gateway integration tests; if needed, use only
`.\nerv.ps1 fullstack run -Scenario smoke`.

## Documentation and PR Contract

Update:

1. `docs/architecture/integration-event-consumption-matrix.md`,
2. `docs/architecture/facade-coverage-matrix.json`,
3. `docs/architecture/database-schema-catalog.md`,
4. `docs/architecture/implementation-readiness.md`,
5. the planner product-doc role page to distinguish the delivered backend/facade
   override path from the still-open #78 Gantt interaction.

The PR declares the new endpoint `exposed`, lists OpenAPI/schema impact, contains Fix,
Tests, Risk, and product-documentation impact sections, and ends with `Fixes #700`.
