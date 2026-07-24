# MAN-580 Leader Scheduling Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the leader-demo planner loop from bulk MES work-order selection through authoritative APS generation, one shared editable draft, locked revision preview, invalidation impact and plan comparison, to explicit release of a new governed version.

**Architecture:** BusinessScheduling remains the owner of problem assembly, APS execution, persisted generated plans, invalidation facts and release governance. Two narrow service endpoints compose existing MES work-order facts, ProductEngineering routing snapshots, `SchedulingProblemProducer`, lock/override overlays and `FiniteCapacityScheduler`; BusinessGateway exposes them through the existing scheduling permission boundary. Business Console owns only `WorkingScheduleDraft` interaction state and renders existing `@nerv-iip/scheduling` Gantt/resource components; it never computes an authoritative schedule, conflict, unscheduled result or KPI.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, xUnit, Vue 3.5 Composition API, Pinia Colada, Vitest, `@nerv-iip/scheduling`, NvUI.

---

## File map

- `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs`: revision impact/comparison response contracts shared across Scheduling and Gateway.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingWorkbenchSourceProvider.cs`: authoritative MES work-order and ProductEngineering production-version composition.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/SchedulingWorkbenchCommands.cs`: bulk work-order generation plus base snapshot + included orders + explicit draft locks to persisted revision, impact and comparison.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints/Scheduling/SchedulingEndpoints.cs`: internal service HTTP contracts and endpoint registry.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Program.cs`: typed MES source client registration.
- `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingWorkbenchTests.cs`: source validation, 100-order batch, lock preservation, impact and comparison tests.
- `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Scheduling/BusinessConsoleSchedulingEndpoints.cs`: exposed facades.
- `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`: downstream client methods.
- `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`: public request models.
- `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/{BusinessGatewayAuthorizationTests,BusinessGatewayOpenApiTests,BusinessGatewayProxyTests}.cs`: auth, scope and wire-shape tests.
- `docs/architecture/facade-coverage-matrix.json`: both new business endpoints classified `exposed`.
- `frontend/packages/api-client/openapi/business-console.v1.json` and generated files: refreshed mechanically from Gateway OpenAPI.
- `frontend/packages/api-client/src/business-console.ts`: stable exports for generated workbench operations and contracts.
- `frontend/apps/business-console/src/composables/useWorkingScheduleDraft.ts`: single source of truth, undo/redo and draft mutations.
- `frontend/apps/business-console/src/composables/useSchedulingWorkbench.ts`: MES candidate query plus Scheduling generate/revision/release mutations.
- `frontend/apps/business-console/src/components/scheduling/SchedulingOrderPool.vue`: filter/select/bulk include/priority/rush interaction.
- `frontend/apps/business-console/src/components/scheduling/SchedulingDraftBoard.vue`: existing Gantt/resource board plus table editor bound to the same draft.
- `frontend/apps/business-console/src/components/scheduling/ScheduleRevisionReview.vue`: invalidation impact and backend comparison.
- `frontend/apps/business-console/src/pages/scheduling.vue`: thin route-level composition and permission/read-only gates.
- `frontend/apps/business-console/src/**/*.test.ts`: black-box draft, page and component tests.
- `docs/architecture/scheduling-workbench-module-product-design.md`: current delivered workflow and explicit MAN-582/583/MAN-588 exclusions.
- `docs/architecture/implementation-readiness.md`: delivered MAN-580 slice and verification evidence.

### Task 1: Backend authoritative workbench orchestration

**Files:** backend contracts, Scheduling source provider, commands, endpoint registry, DI and focused tests listed above.

- [x] **Step 1: Write failing Scheduling tests**

Add tests that express these public behaviors:

```csharp
[Fact]
public async Task GeneratePlan_accepts_one_request_with_100_distinct_work_orders()
{
    var request = WorkbenchRequestFactory.Create(orderCount: 100);
    var result = await handler.Handle(request, CancellationToken.None);
    Assert.Equal(100, result.Assignments.Select(x => x.OrderId).Distinct().Count());
}

[Fact]
public async Task Revision_preserves_explicit_locked_assignment_and_compares_with_base()
{
    var result = await handler.Handle(requestWithMovedLockedAssignment, CancellationToken.None);
    Assert.Contains(result.Candidate.Assignments, x => x.OperationId == "OP-10" && x.IsLocked && x.StartUtc == movedStart);
    Assert.Equal(1, result.Comparison.MovedOperationCount);
}
```

Also cover missing/duplicate/terminal work orders, absent production version, foreign organization/environment, unknown lock operation, invalid lock resource/time and latest invalidation affected-operation grouping.

- [x] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~SchedulingWorkbench
```

Expected: compilation/test failures because the workbench contracts and commands do not exist.

- [x] **Step 3: Implement minimum orchestration**

Use exact request/response responsibilities:

```csharp
public sealed record SchedulingWorkbenchOrderSelection(
    string WorkOrderId,
    int Priority,
    bool IsRush);

public sealed record CreateSchedulingWorkbenchPlanRequest(
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<SchedulingWorkbenchOrderSelection> Orders);

public sealed record CreateSchedulePlanRevisionRequest(
    string OrganizationId,
    string EnvironmentId,
    IReadOnlyCollection<string> IncludedOrderIds,
    IReadOnlyCollection<SchedulingLockedAssignmentContract> LockedAssignments);

public sealed record SchedulePlanRevisionContract(
    SchedulePlanContract Candidate,
    SchedulePlanImpactContract Impact,
    SchedulePlanComparisonContract Comparison);
```

The source provider must fetch at most 500 MES work orders using the managed internal token, require an exact match for every requested ID, reject terminal/unsourced orders, resolve each distinct production version through ProductEngineering, and pass only server-derived SKU, quantity, due, earliest start and routing version into `SchedulingProblemProducer`. The initial command uses a unique problem ID and `CreateSchedulePlanCommand`; the revision command deserializes the persisted normalized problem, filters included orders, validates explicit locks against the base assignment and eligible resources, assigns a unique problem ID, persists a generated candidate through the same create path, groups the latest invalidation facts and builds comparison counts from the two authoritative plans.

- [x] **Step 4: Run focused tests and verify GREEN**

Run the command from Step 2. Expected: all `SchedulingWorkbench*` tests pass.

### Task 2: Expose the two-hop Gateway contract

**Files:** BusinessGateway endpoints/client/models/tests, facade matrix, OpenAPI/client generation.

- [x] **Step 1: Write failing Gateway tests**

Cover operation IDs `createBusinessConsoleSchedulingWorkbenchPlan` and `createBusinessConsoleSchedulingPlanRevision`, `plans.manage` authorization, organization/environment scope forwarding, exact locked assignment wire shape, downstream invalid response handling and response pass-through.

- [x] **Step 2: Verify RED**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~SchedulingWorkbench
```

- [x] **Step 3: Implement facade and governance**

Add:

```text
POST /api/business-console/v1/scheduling/workbench/plans
POST /api/business-console/v1/scheduling/plans/{planId}/revisions
```

Both are `exposed` in `facade-coverage-matrix.json`, both require `business.scheduling.plans.manage`, and both forward a managed internal bearer token to BusinessScheduling. Do not add a new engine, schema or raw SQL.

- [x] **Step 4: Verify Gateway tests, export OpenAPI and regenerate client**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore -m:1 --filter FullyQualifiedName~SchedulingWorkbench
pnpm -C frontend generate:api
pnpm -C frontend --filter @nerv-iip/api-client typecheck
```

Expected: tests pass and generated drift is limited to the two new operations/contracts.

### Task 3: One WorkingScheduleDraft for pool, drag, table and locks

**Files:** new Business Console composables/components and tests. Do not modify `frontend/packages/scheduling/**`.

- [x] **Step 1: Write failing draft tests**

```ts
it('applies drag and table edits to the same task and history', () => {
  const draft = useWorkingScheduleDraft(toModel(plan))
  draft.moveTask({ taskId: 'a-1', startUtc: movedStart, endUtc: movedEnd, resourceId: 'R-2' })
  draft.updateTask('a-1', { startUtc: tableStart })
  expect(draft.model.value.tasks.find(x => x.id === 'a-1')).toMatchObject({ startUtc: tableStart, resourceId: 'R-2' })
  draft.undo()
  expect(draft.model.value.tasks.find(x => x.id === 'a-1')?.startUtc).toBe(movedStart)
})
```

Cover bulk select 100, include/remove, priority/rush edits, lock/unlock, locked-assignment serialization, undo/redo, base reset and read-only mutation rejection.

- [x] **Step 2: Verify RED**

```powershell
pnpm -C frontend/apps/business-console exec vitest run src/composables/useWorkingScheduleDraft.test.ts --maxWorkers=1
```

- [x] **Step 3: Implement draft and black-box components**

`useWorkingScheduleDraft` is the only mutable draft state. `SchedulingOrderPool` emits bulk/order mutations; `SchedulingDraftBoard` renders exported `GanttChart` and `ResourceSchedulerBoard`, forwards `taskDragEnd` to the same draft action, and binds table fields to that action; both use props-down/events-up. No component imports a deep `@nerv-iip/scheduling` path and no authoritative KPI/conflict calculation is added.

- [x] **Step 4: Verify GREEN**

Run the focused composable and component tests; expected: all pass.

### Task 4: Route-level workflow, invalidation review and permission gates

**Files:** `useSchedulingWorkbench.ts`, scheduling page, revision review and page tests.

- [x] **Step 1: Write failing page tests**

Test user-visible behavior: 100 candidates selected in one bulk action; generated plan opens editable draft; drag and table show the same values; locked revision calls the generated facade with exact included orders/locks; invalidated plan displays affected orders/operations; backend comparison displays on-time rate, tardiness, utilization, moved/locked/unscheduled; release targets the candidate plan; read-only principals and superseded/revoked/released history cannot mutate.

- [x] **Step 2: Verify RED**

```powershell
pnpm -C frontend/apps/business-console exec vitest run src/pages/scheduling.test.ts src/composables/useSchedulingWorkbench.test.ts --maxWorkers=1
```

- [x] **Step 3: Implement the thin route composition**

The route metadata requires only `plans.read`. `canManage` and `canRelease` derive separately from the authenticated principal and terminal status. Mutations use Pinia Colada generated operations, invalidate plan/detail queries after success and show toast success/failure. The comparison view consumes only backend response fields.

- [x] **Step 4: Verify focused frontend tests and touched-file formatting**

```powershell
pnpm -C frontend/apps/business-console exec vitest run src/pages/scheduling.test.ts src/composables/useWorkingScheduleDraft.test.ts src/composables/useSchedulingWorkbench.test.ts --maxWorkers=1
pnpm -C frontend exec vp fmt --check <each-touched-frontend-file>
```

### Task 5: Documentation, full verification and ready PR

- [x] **Step 1: Update product/readiness documentation**

Document the delivered planner loop, exact permissions, backend-owned data flow, scale evidence reuse and explicit exclusions: no MAN-582 actual-deviation predictor, no MAN-583 split/transfer-batch or parallel-machine modeling, no MAN-588 unattended candidate engine, no change to the legacy scheduling visualization package.

- [x] **Step 2: Run fresh completion gates**

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore -m:1
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore -m:1
dotnet test backend/Nerv.IIP.sln --no-restore -m:1
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
pwsh scripts/verify-business-scheduling-aps-lite.ps1 -SkipRestore
git diff --check
```

If shared-machine resource pressure blocks a full gate, preserve the exact output, rerun focused gates serially and report the environmental blocker without claiming the full gate passed.

- [x] **Step 3: Self-review scope and facts**

Confirm `git diff -- frontend/packages/scheduling` is empty; generated changes correspond to the new Gateway contract; no MAN-582/583/588 behavior appears; every acceptance line maps to a test or backend contract.

- [x] **Step 4: Commit, push and create a non-draft PR**

PR body must include `Fixes #1049`, Linear `MAN-580`, product-doc impact, endpoint facade declarations, verification evidence, risks and excluded follow-ups. Verify `isDraft=false`, base `main`, expected head branch and live checks, then stop without merging.
