# BusinessScheduling APS Lite Design

## Goal

Implement #206 as a backend-owned APS lite boundary that accepts a versioned `SchedulingProblem`, runs a deterministic finite-capacity scheduling heuristic, and returns a stable `SchedulePlan` for MES dispatch decisions and #78 Gantt rendering.

The first slice optimizes for reproducibility, explainability and testability. It does not attempt global optimality.

## Current State

`BusinessScheduling` does not exist yet. The architecture already reserves `backend/services/Business/Scheduling` and the `scheduling` schema for APS lite. MES currently has `RuleScheduler` and `ScheduleResult` as a transition path, but that code is MES-owned and does not provide a canonical scheduling contract.

Issue #206 is the backend execution entry for APS. #78 consumes the output DTO for Gantt and scheduling views. #207 provides equipment runtime facts later; #191, #194 and #195 provide planning, MES work-order release and readiness facts.

## Boundary

BusinessScheduling owns:

1. `SchedulingProblem` input snapshots and their fingerprint.
2. `SchedulePlan` output versions.
3. Operation assignments, resource load, conflicts, unscheduled operations and change summary.
4. Algorithm version metadata and deterministic replay evidence.
5. Schedule plan lifecycle: preview, generated and released.

BusinessScheduling does not own:

1. MRP demand, planned purchase suggestions or planned work-order suggestions.
2. MES work orders, operation tasks, reports or execution state transitions.
3. Inventory balance, WMS staging, quality inspection records, device master data, alarms or maintenance work orders.
4. PLC/DCS/SCADA control commands.
5. Browser-side scheduling calculations.

## Contract Shape

All contract times are UTC `DateTimeOffset`. Local plant calendars are represented as explicit shift windows and exception windows in the problem, not by relying on machine-local time.

### SchedulingProblem

| Field | Meaning |
| --- | --- |
| `problemId` | Caller-provided or service-generated public ID for replay and traceability. |
| `contractVersion` | Initial value `1`; required for future compatible evolution. |
| `organizationId` / `environmentId` | IAM context passed through the business boundary. |
| `horizonStartUtc` / `horizonEndUtc` | Scheduling window. Operations outside the window are returned as conflict/unscheduled results. |
| `orders` | Work-order candidates with due date, priority, quantity, source references and operation sequence. |
| `resources` | Work centers and devices with capabilities, capacity units, calendar references and deterministic sort keys. |
| `calendars` | Shift windows and exceptions that constrain available production time. |
| `unavailabilityWindows` | Maintenance, active alarm, downtime, inspection or manual block windows by resource/work center. |
| `materialReadiness` | Earliest material-ready time and blocking reasons by order or operation. |
| `qualityBlocks` | Quality or inspection blocks by order, operation, SKU, route or resource. |
| `lockedAssignments` | Existing or user-locked assignments that must reserve capacity before scheduling the open queue. |

### Operation Candidate

Each operation carries:

1. `orderId`, `operationId`, `operationSequence` and optional `predecessorOperationIds`.
2. `durationMinutes`, `quantity`, `requiredCapabilityCode` and eligible `resourceIds`.
3. `primaryResourceId` when the route has a preferred work center/device.
4. `earliestStartUtc`, `dueUtc`, `priority` and `isRush`.
5. `splitPolicy`, with P0 supporting only `nonSplittable`.
6. Optional `materialReadyUtc`, quality block reason and source references to DemandPlanning/MES/ProductEngineering.

### SchedulePlan

| Field | Meaning |
| --- | --- |
| `planId` | Public plan ID; preview responses may use a transient ID. |
| `problemId` / `problemFingerprint` | Replay and idempotency evidence. |
| `contractVersion` / `algorithmVersion` | DTO and algorithm version. |
| `status` | `preview`, `generated` or `released`. |
| `generatedAtUtc` | Service-layer timestamp, not produced by the pure algorithm. |
| `assignments` | Operation-to-resource schedule with start/end, source order, route refs and explanation code. |
| `resourceLoads` | Resource/day or resource/window load and utilization. |
| `conflicts` | Due-date, capacity, calendar, material, quality or equipment conflicts. |
| `unscheduledOperations` | Operations that cannot be scheduled in the horizon with reason codes. |
| `changeSummary` | Added, moved, delayed, preserved and blocked operation references compared with a previous plan or locked assignments. |
| `ganttItems` | Stable read DTO for #78, derived from assignments/conflicts without browser-side scheduling. |

## Algorithm V1

The P0 algorithm is a deterministic finite-capacity heuristic:

1. Validate the problem and normalize resources, calendars, operations and windows.
2. Reserve locked or in-progress assignments first. Invalid locks are preserved in output but also reported as conflicts.
3. Sort open operations by `isRush` descending, `priority` descending, `dueUtc`, `orderId`, `operationSequence`, then `operationId`.
4. Enforce operation precedence by making each operation's earliest start at least the latest scheduled predecessor end.
5. Enforce material readiness and quality blocks by moving earliest start or marking unscheduled when the block is open-ended.
6. For each eligible resource, find the earliest slot that fits duration, capacity, shift calendar and unavailability windows.
7. Choose the earliest feasible slot; ties prefer primary resource, then lower deterministic resource sort key, then resource ID.
8. If the operation cannot fit before `horizonEndUtc`, return it in `unscheduledOperations` with a reason code instead of dropping it.
9. If an assignment ends after `dueUtc`, keep the assignment and add a due-date conflict.
10. Compute resource load from actual assigned minutes over explicit calendar capacity.

The algorithm must not call databases, HTTP services, clocks, random number generators or static local-time APIs.

## Shock Absorber Fixture

Use this fixture as the cross-worker regression case:

| Fact | Fixture |
| --- | --- |
| Products | `FG-FRONT-SHOCK`, `FG-REAR-SHOCK`. |
| Work centers | `WC-TUBE-WELD`, `WC-ROD-ASSEMBLY`, `WC-OIL-SEAL`, `WC-DAMPING-TEST`. |
| Devices | `DEV-WELD-01`, `DEV-ROD-01`, `DEV-OIL-01`, `DEV-TEST-01`. |
| Shift | 2026-06-01 08:00-16:00 UTC and 2026-06-02 08:00-16:00 UTC. |
| Route | Weld tube -> assemble rod -> oil/seal -> damping test/pack. |
| Maintenance | `DEV-OIL-01` unavailable on 2026-06-01 10:00-12:00 UTC. |
| Rush order | `WO-RUSH-REAR-001`, higher priority, due before the normal front order. |
| Normal order | `WO-FRONT-001`, lower priority, same oil/seal bottleneck. |

Expected evidence:

1. Every repeated run returns assignments in the same order and with the same timestamps.
2. Operation sequence is preserved for both work orders.
3. Oil/seal operations do not overlap the maintenance window.
4. The rush order is placed before the normal order on the shared bottleneck when both are feasible.
5. Any normal-order delay caused by the rush insertion appears in `changeSummary` or `conflicts`.
6. `ganttItems` contain work order, operation, resource, start/end, status and conflict marker fields without requiring frontend schedule calculation.

## API Surface

| API | Purpose | Permission |
| --- | --- | --- |
| `POST /api/business/v1/scheduling/plans/preview` | Run the algorithm without persisting a released plan. | `business.scheduling.plans.manage` |
| `POST /api/business/v1/scheduling/plans` | Persist a generated plan from a problem snapshot. | `business.scheduling.plans.manage` |
| `GET /api/business/v1/scheduling/plans` | List generated/released plans. | `business.scheduling.plans.read` |
| `GET /api/business/v1/scheduling/plans/{planId}` | Read a full plan. | `business.scheduling.plans.read` |
| `GET /api/business/v1/scheduling/plans/{planId}/gantt` | Read the stable Gantt DTO. | `business.scheduling.plans.read` |
| `POST /api/business/v1/scheduling/plans/{planId}/release` | Mark a plan released and emit release intent/event for MES consumption. | `business.scheduling.plans.release` |

BusinessGateway may expose equivalent `/api/business-console/v1/scheduling/**` page facades after the service API exists. Gateway must not persist scheduling facts or implement scheduling logic.

## Events

BusinessScheduling publishes ADR 0011 envelope events:

1. `scheduling.SchedulePlanGenerated`
2. `scheduling.ScheduleConflictDetected`
3. `scheduling.SchedulePlanReleased`

Event payloads carry public IDs, contract version, algorithm version, problem fingerprint, plan status, affected work-order references and conflict reason codes. They do not carry full browser DTO payloads, database row IDs, credentials, PLC commands or raw high-frequency telemetry.

## Persistence

Default schema: `scheduling`.

Required P0 tables:

1. `schedule_problems`
2. `schedule_plans`
3. `schedule_plan_assignments`
4. `schedule_plan_resource_loads`
5. `schedule_plan_conflicts`
6. `schedule_plan_unscheduled_operations`

Each table and business column requires schema comments. PostgreSQL migrations history must use `scheduling.__EFMigrationsHistory`.

## Tests

Acceptance requires:

1. Pure algorithm tests for deterministic repeat runs, precedence, capacity conflicts, calendar shifts, maintenance windows, locked assignments, rush insertion, due-date conflict and unscheduled reason codes.
2. Contract serialization tests for `SchedulingProblem`, `SchedulePlan`, `GanttScheduleItem` and reason-code enums.
3. Domain tests for plan lifecycle and released-plan immutability.
4. Web contract tests for route shape, operation IDs, authorization policy and request/response fields.
5. Schema convention tests using `Nerv.IIP.Testing`.
6. Event converter tests for the three scheduling event names.
7. BusinessGateway facade tests after the service API is registered.

## Out of Scope

1. Global solver optimization, genetic algorithms, MILP/CP-SAT, simulation and automatic rescheduling.
2. Recursive multi-level capacity planning across all upstream services.
3. Direct PLC/DCS/SCADA control.
4. Browser-side official scheduling.
5. High-frequency historian storage.
