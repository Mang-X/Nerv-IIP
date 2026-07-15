# Scheduling Locks and Manual Overrides Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver MAN-384 / #700 with persistent Scheduling overrides, base-plan operation locks, an exposed manual-adjustment API, a real MES dispatch-to-Scheduling event loop, and optimization KPIs that exclude locked operations.

**Architecture:** BusinessScheduling owns a durable operation-override projection and overlays it onto every assembled/previewed/created problem as `SchedulingLockedAssignmentContract`. MES publishes a versioned event after a real device dispatch; Scheduling consumes it idempotently into the same projection. Existing PR #904 invalidation and release gates remain unchanged.

**Tech Stack:** .NET 10, CleanDDD/NetCorePal, EF Core PostgreSQL, FastEndpoints, CAP integration events, xUnit, BusinessGateway, governed OpenAPI export, Hey API/pnpm.

---

## File Map

### Contracts and scheduling core

- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs` — explicit locked/optimizable KPI counts.
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Mes/MesIntegrationEvents.cs` — MES manual-dispatch event contract.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/FiniteCapacityScheduler.cs` — optimization KPI scope.
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingOperationOverrideOverlay.cs` — one merge policy used by assemble, preview, and create.

### Scheduling persistence and API

- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/ScheduleOperationOverrideAggregate/ScheduleOperationOverride.cs` — override aggregate and stale-update rule.
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/ScheduleOperationOverrideEntityTypeConfiguration.cs` — table/comments/index.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/SchedulePlanAggregate/SchedulePlan.cs` — persisted KPI fields and problem JSON snapshot field.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ApplicationDbContext.cs` — override DbSet.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/ScheduleProblemSnapshotEntityTypeConfiguration.cs` — `problem_json` mapping.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/SchedulePlanEntityTypeConfiguration.cs` — KPI mappings/comments.
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/UpsertScheduleOperationOverrideCommand.cs` — API command/validation.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/AssembleSchedulingProblemCommand.cs` — resolve base-plan IDs and overlay overrides.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/PreviewSchedulePlanCommand.cs` — overlay overrides before adapters/scheduler.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/CreateSchedulePlanCommand.cs` — overlay before fingerprint, persist normalized JSON.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingProblemProducer.cs` — `BasePlanId` / `LockedOperationIds` request shape.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints/Scheduling/SchedulingEndpoints.cs` — PUT support, endpoint, validator, registry.
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventHandlers/MesOperationTaskManuallyDispatchedIntegrationEventHandler.cs` — idempotent consumer.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Program.cs` — consumer registration.
- Create via EF tooling in `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/`: migration class `AddSchedulingOperationOverrides` and its generated designer — formal migration.
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` — generated model snapshot.

### MES producer and real cross-boundary test

- Modify: `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Domain/DomainEvents/MesDomainEvents.cs` — dispatch domain event.
- Modify: `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs` — raise only with a real device/window.
- Modify: `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs` — public event converter.
- Create: `backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/ManualDispatchSchedulingLockEventTests.cs` — producer behavior.
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/MesDispatchSchedulingOverrideAcceptanceTests.cs` — real MES IDs through Scheduling persistence and replanning.

### Gateway, generated contract, docs, and governance

- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Scheduling/BusinessConsoleSchedulingEndpoints.cs` — exposed PUT facade.
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs` — Scheduling service proxy call.
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs` — request/response facade DTO.
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs` — operation ID/OpenAPI contract.
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs` — real forwarding shape.
- Modify: `docs/architecture/facade-coverage-matrix.json` — `exposed` declaration.
- Modify: `docs/architecture/integration-event-consumption-matrix.md` — MES producer/Scheduling consumer row.
- Modify: `docs/architecture/database-schema-catalog.md` — new table and columns.
- Modify: `docs/architecture/implementation-readiness.md` — delivered #700 facts only.
- Modify: `frontend/apps/docs/docs/roles/planner.md` — backend/facade delivered, #78 interaction still pending.
- Generated by governed commands: BusinessGateway OpenAPI snapshot and `frontend/packages/api-client/src/generated/business-console/**`.

## Task 1: Contract and KPI Red Tests

**Files:**
- Modify: `backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/SchedulingContractSerializationTests.cs`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs`
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/FiniteCapacitySchedulerTests.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/FiniteCapacityScheduler.cs`

- [ ] **Step 1: Add a failing metrics serialization test**

Construct `SchedulePlanMetricsContract` with named arguments including:

```csharp
LockedOperationCount: 1,
OptimizableOperationCount: 2
```

Round-trip with `SchedulingJson.Options` and assert both values. This must initially fail to compile.

- [ ] **Step 2: Add a failing scheduler KPI test**

Create one locked late assignment and one unlocked on-time operation, schedule the problem, then assert:

```csharp
Assert.Equal(1, plan.Metrics.LockedOperationCount);
Assert.Equal(1, plan.Metrics.OptimizableOperationCount);
Assert.Equal(0, plan.Metrics.TotalTardinessMinutes);
Assert.Equal(0, plan.Metrics.LateOperationCount);
Assert.Equal(1m, plan.Metrics.OnTimeRate);
Assert.Equal(2, plan.Metrics.ScheduledOperationCount);
```

- [ ] **Step 3: Run the red tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj --no-restore
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore --filter FullyQualifiedName~FiniteCapacitySchedulerTests
```

Expected: compile/test failure because the two metric properties do not exist or locked tardiness is still counted.

- [ ] **Step 4: Implement the minimal contract and scheduler calculation**

Add the two integer fields after `UnscheduledOperationCount`. In `BuildMetrics`, compute:

```csharp
var lockedOperationCount = orderedAssignments.Count(x => x.IsLocked);
var optimizableAssignments = orderedAssignments.Where(x => !x.IsLocked).ToArray();
```

Use `optimizableAssignments` for tardiness, late count, and on-time rate; keep plan-wide counts, assigned minutes, makespan, and utilization unchanged.

- [ ] **Step 5: Propagate the new metrics through domain snapshots/mappers and rerun tests**

Update every constructor/mapping compile error under Scheduling domain/web/tests. Expected: both commands pass.

- [ ] **Step 6: Commit**

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.Scheduling backend/tests/Nerv.IIP.Contracts.Scheduling.Tests backend/services/Business/Scheduling
git commit -m "feat(scheduling): exclude locked operations from optimization KPIs"
```

## Task 2: Override Aggregate, Problem JSON, and Migration

**Files:**
- Create/modify the Scheduling domain, infrastructure, schema test, and migration files listed in the file map.

- [ ] **Step 1: Write failing domain tests**

Add `ScheduleOperationOverrideTests.cs` covering create, newer replacement, and stale rejection. The key assertion is:

```csharp
Assert.False(overrideFact.TryReplace(
    "DEV-OLD", "WC-01", start.AddHours(-1), end.AddHours(-1),
    "mes.manual-dispatch", "evt-old", "user:old", occurredAt.AddMinutes(-1), updatedAt));
Assert.Equal("DEV-NEW", overrideFact.ResourceId);
```

- [ ] **Step 2: Write failing persistence/schema tests**

Extend `SchedulingPersistenceTests` and `SchedulingSchemaConventionTests` to require:

```csharp
Assert.NotNull(dbContext.Model.FindEntityType(typeof(ScheduleOperationOverride)));
Assert.Equal("jsonb", problemJsonProperty.GetColumnType());
```

Also assert table/column comments and the unique scope+operation index.

- [ ] **Step 3: Run red tests**

Run the Scheduling domain and web test projects with filters for the new tests. Expected: missing type/property/table failures.

- [ ] **Step 4: Implement the aggregate and mappings**

Use a Guid v7 strongly typed ID, required normalized identifiers, `EndUtc > StartUtc`, and:

```csharp
public bool TryReplace(
    string resourceId,
    string workCenterId,
    DateTimeOffset startUtc,
    DateTimeOffset endUtc,
    string lockReasonCode,
    string? sourceEventId,
    string actor,
    DateTimeOffset sourceOccurredAtUtc,
    DateTimeOffset updatedAtUtc)
{
    if (sourceOccurredAtUtc < SourceOccurredAtUtc) return false;
    // validate then replace every mutable fact atomically
    return true;
}
```

Add `ProblemJson` to `ScheduleProblemSnapshot`; serialize the normalized problem with `SchedulingJson.Options` during create.

- [ ] **Step 5: Generate the formal migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddSchedulingOperationOverrides `
  --project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure `
  --startup-project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web
```

Inspect generated SQL shape: new override table, comments, unique index, KPI columns, and non-null `problem_json` with a valid legacy default.

- [ ] **Step 6: Rerun domain, persistence, and schema tests**

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add backend/services/Business/Scheduling
git commit -m "feat(scheduling): persist operation overrides and problem snapshots"
```

## Task 3: Overlay Overrides and Resolve Base-Plan Locks

**Files:**
- Create: `.../Application/Scheduling/SchedulingOperationOverrideOverlay.cs`
- Modify: Scheduling assemble, preview, create, producer, and tests.

- [ ] **Step 1: Add failing overlay tests**

Cover:

1. base-plan `LockedOperationIds` preserve the selected assignment exactly,
2. missing selected operation is a `KnownException`,
3. persisted override wins over a base-plan lock for the same operation,
4. preview and create both honor persisted overrides,
5. override for an absent operation is ignored for that run.

Use exact assertions:

```csharp
Assert.Equal("DEV-MANUAL", locked.ResourceId);
Assert.Equal(manualStart, locked.StartUtc);
Assert.Equal(manualEnd, locked.EndUtc);
Assert.Equal("manual-override", locked.LockReasonCode);
```

- [ ] **Step 2: Run red tests**

Run `SchedulingProblemProducerTests`, new overlay tests, and create/preview handler tests. Expected: request fields and overlay service missing.

- [ ] **Step 3: Implement one overlay service**

Define:

```csharp
public interface ISchedulingOperationOverrideOverlay
{
    Task<SchedulingProblemContract> ApplyAsync(SchedulingProblemContract problem, CancellationToken cancellationToken);
}
```

Query overrides by organization/environment and operation IDs in the incoming problem, map them to locked assignments, and merge by `(OrderId, OperationId)` with override precedence.

- [ ] **Step 4: Implement base-plan resolution**

Add nullable `BasePlanId` and `LockedOperationIds` to assembly request. Load the scoped plan with assignments; reject a requested ID not found. Resolve exact assignment facts without accepting caller-supplied downstream IDs.

- [ ] **Step 5: Apply the overlay before preview/create adapters and before create fingerprinting**

Ensure create persists the overlaid normalized `ProblemJson`; a caller-posted stale problem cannot bypass a current override.

- [ ] **Step 6: Run focused Scheduling tests and commit**

Expected: PASS.

```powershell
git add backend/services/Business/Scheduling
git commit -m "feat(scheduling): merge base-plan locks and durable overrides"
```

## Task 4: Exposed Manual Override Endpoint

**Files:**
- Create: Scheduling upsert command file.
- Modify: Scheduling endpoints/registry/tests.

- [ ] **Step 1: Write failing endpoint and command tests**

Require `PUT`, route `/api/business/v1/scheduling/plans/{planId}/operations/{operationId}/override`, operation ID `upsertSchedulingOperationOverride`, internal-service authorization, and `PlansManage` permission.

Command tests must prove the server derives work order, sequence, and work center from `ProblemJson`, rejects an ineligible resource, and persists a valid override.

- [ ] **Step 2: Run red tests**

Expected: endpoint/command missing.

- [ ] **Step 3: Implement PUT support and the endpoint**

Extend the endpoint base switch with `case "PUT": Put(contract.Route);`. Define request fields:

```csharp
string OrganizationId,
string EnvironmentId,
[property: RouteParam] string PlanId,
[property: RouteParam] string OperationId,
string ResourceId,
DateTimeOffset StartUtc,
DateTimeOffset EndUtc
```

Load scoped plan and problem snapshot, deserialize normalized JSON, validate the operation/resource, derive work center, then create/replace the override with `TimeProvider` and request actor context.

- [ ] **Step 4: Run Scheduling endpoint/command tests and commit**

```powershell
git add backend/services/Business/Scheduling
git commit -m "feat(scheduling): expose manual operation override endpoint"
```

## Task 5: MES Dispatch Event and Idempotent Scheduling Consumer

**Files:**
- Modify/create MES contract, domain event, converter, Scheduling handler, registrations, and tests listed above.

- [ ] **Step 1: Write failing public contract and MES producer tests**

Add `MesOperationTaskManuallyDispatchedIntegrationEvent` with ADR 0011 envelope plus payload containing real work order, operation, sequence, resource, work center, start/end, and assigned time. Assert a device dispatch raises exactly one event and user-only dispatch raises none.

- [ ] **Step 2: Write failing Scheduling consumer tests**

Assert valid event upsert, duplicate idempotency, older event no-op, and invalid window dead-letter without exception.

- [ ] **Step 3: Run red contract/MES/Scheduling tests**

Expected: missing event/converter/handler.

- [ ] **Step 4: Implement MES domain event and converter**

Raise after `Assign` only when normalized device ID exists and duration is positive. Use the real task fields:

```csharp
StartUtc: task.EarliestStartUtc,
EndUtc: task.EarliestStartUtc + task.Duration
```

Do not fabricate assignment, plan, or downstream IDs.

- [ ] **Step 5: Implement the consumer**

Use `IntegrationEventConsumerGuard`, `SchedulingProcessedIntegrationEventInbox.TryRecordAsync`, and `IIntegrationEventDeadLetterStore`. Invalid payloads add a dead letter and return; valid payloads upsert the override; save races converge without throwing a poison business exception.

- [ ] **Step 6: Update the event-consumption matrix, rerun focused tests, and commit**

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.Mes backend/services/Business/MES backend/services/Business/Scheduling docs/architecture/integration-event-consumption-matrix.md
git commit -m "feat: close MES manual dispatch into scheduling locks"
```

## Task 6: Real Cross-Boundary Acceptance Test

**Files:**
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/MesDispatchSchedulingOverrideAcceptanceTests.cs`

- [ ] **Step 1: Write the failing acceptance test**

The test must:

1. persist a MES work order and real operation task,
2. execute `AssignDispatchTaskCommandHandler` with a real device,
3. take the domain event and convert it using the production converter,
4. invoke the production Scheduling consumer against Scheduling persistence,
5. generate the next plan containing the same operation,
6. assert exact resource/start/end preservation and `IsLocked == true`.

- [ ] **Step 2: Run the test red, then make only integration wiring fixes**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter FullyQualifiedName~MesDispatchSchedulingOverrideAcceptanceTests
```

Expected red reason before wiring: missing references/registrations. Do not replace real IDs with literals invented only for Scheduling.

- [ ] **Step 3: Rerun until green and commit**

```powershell
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests backend/Nerv.IIP.sln
git commit -m "test: prove MES dispatch survives scheduling replanning"
```

## Task 7: Gateway, Facade Governance, OpenAPI, and Documentation

**Files:**
- Modify generated/governed surfaces and docs listed in the file map.

- [ ] **Step 1: Add failing BusinessGateway facade and facade-coverage tests**

Proxy the same route semantics under `/api/business-console/v1/scheduling/.../override`; ensure the facade forwards auth scope and request body without business logic.

- [ ] **Step 2: Register the service endpoint as `exposed`**

Add the exact service route, operation ID, and Gateway operation ID to `facade-coverage-matrix.json`. Do not mark it deferred/internal.

- [ ] **Step 3: Implement the Gateway proxy and run Gateway/facade tests**

Expected: PASS.

- [ ] **Step 4: Export OpenAPI through the repository-governed script**

Run:

```powershell
scripts/export-gateway-openapi.ps1
pnpm -C frontend generate:api
```

Never hand-edit snapshots or generated client files.

- [ ] **Step 5: Update schema/readiness/product docs**

Record the new table, problem JSON, KPI columns, event loop, and exposed endpoint. Planner docs must still state that #78 Gantt drag interaction is pending.

- [ ] **Step 6: Run generated-client typecheck and focused frontend tests**

```powershell
pnpm -C frontend typecheck
pnpm -C frontend test -- --run
```

If the full frontend test command uses a different supported argument shape, use the package script unchanged and record the exact command.

- [ ] **Step 7: Commit**

```powershell
git add backend/gateway docs frontend
git commit -m "feat(gateway): expose scheduling operation overrides"
```

## Task 8: Verification, Review, and PR

- [ ] **Step 1: Run focused backend tests**

Run contract, Scheduling domain/web, MES domain/web, BusinessGateway, facade coverage, and acceptance test projects. All must pass.

- [ ] **Step 2: Run schema and migration checks**

Run Scheduling and MES schema convention tests and inspect the generated migration/model snapshot diff. All changed tables/columns require comments.

- [ ] **Step 3: Run the backend solution gate**

```powershell
dotnet test backend/Nerv.IIP.sln
```

Expected: PASS with warnings-as-errors unchanged.

- [ ] **Step 4: Invoke `verification-before-completion` and record fresh evidence**

Check `git diff --check`, `git status`, relevant generated artifacts, and all command exit codes.

- [ ] **Step 5: Invoke `requesting-code-review` and address factual findings**

Review against commit `36be99754ccb57a0d6210e09ae5c54ca63199202`, the approved spec, #904 behavior, and #700 scope. Rerun affected tests after fixes.

- [ ] **Step 6: Push the existing independent branch**

```powershell
git push -u origin codex/man-384-700-scheduling-overrides
```

- [ ] **Step 7: Create the PR with `gh` and stop**

Use a title beginning `MAN-384 #700`. The body must contain `Fix`, `Tests`, `Risk`, `OpenAPI or schema impact`, product documentation impact, endpoint declaration `exposed`, and `Fixes #700`. Do not merge or start another issue.
