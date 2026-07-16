# MES Manual Dispatch Override Revocation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close issue #933 by publishing a versioned MES manual-dispatch clear fact and converging Scheduling overrides to an inactive tombstone so stale locks cannot survive or reappear.

**Architecture:** MES persists a monotonic per-operation manual-dispatch revision and publishes immutable dispatch/clear snapshots. Scheduling projects both event types into one override aggregate whose active state and source revision form the ordering watermark; the scheduling overlay reads active rows only. Existing HTTP routes remain unchanged, and the canonical cross-boundary acceptance test exercises the real MES command/domain/converter and Scheduling consumer/overlay/scheduler chain.

**Tech Stack:** .NET 10, C# records, NetCorePal domain/integration events, MediatR, FastEndpoints, DotNetCore.CAP, EF Core, PostgreSQL migrations, xUnit.

## Global Constraints

- Read `docs/architecture/implementation-readiness.md` before implementation and preserve the explicit #701 boundary.
- Use FastEndpoints only; this issue adds no HTTP route and changes no HTTP contract.
- Use async EF Core APIs with `CancellationToken` in application and consumer code.
- Use `Guid.CreateVersion7()` through existing event helpers; do not introduce v4 IDs.
- New integration-event handlers use `IntegrationEventConsumerGuard`, the Scheduling inbox, and the Scheduling DLQ.
- Do not physically delete Scheduling overrides; inactive tombstones preserve the source watermark.
- Do not let MES clear a current `scheduling-api` override.
- Generate EF migrations with the PostgreSQL persistence profile and never hand-edit generated migration snapshots.
- Update schema comments/catalog and the integration-event consumption matrix.
- No facade matrix, OpenAPI snapshot, or frontend code changes are expected because no HTTP endpoint changes.

---

## File Map

### MES lifecycle and contract

- `backend/common/Contracts/Nerv.IIP.Contracts.Mes/MesIntegrationEvents.cs` — public dispatch revision and cleared-event envelope/payload.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/DomainEvents/MesDomainEvents.cs` — immutable manual-dispatch lifecycle snapshots.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs` — revision, active-manual-dispatch state, assign/clear/cancel transitions.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/OperationTaskEntityTypeConfiguration.cs` — persisted lifecycle fields and comments.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventContext.cs` — HTTP/activity correlation and causation accessor.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs` — positive and cleared event conversion.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs` — authenticated actor propagation through work-order cancellation.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs` — pass authenticated actor into cancellation command without changing the request contract.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs` — register the MES integration-event context accessor.
- `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/*AddMesManualDispatchRevision*` — generated schema change.

### Scheduling projection

- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/ScheduleOperationOverrideAggregate/ScheduleOperationOverride.cs` — active/tombstone state and revision-aware transitions.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/ScheduleOperationOverrideEntityTypeConfiguration.cs` — tombstone columns and comments.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventHandlers/MesOperationTaskManuallyDispatchedIntegrationEventHandler.cs` — pass positive dispatch revision into projection ordering.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventHandlers/MesOperationTaskManualDispatchClearedIntegrationEventHandler.cs` — guarded cleared-event consumer.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingOperationOverrideOverlay.cs` — filter inactive tombstones.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/*AddSchedulingOverrideRevocationTombstones*` — generated schema change.

### Tests and governance

- `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/ManualDispatchSchedulingLockEventTests.cs`
- `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesEndpointContractTests.cs`
- `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesPersistenceContractTests.cs`
- `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/ScheduleOperationOverrideTests.cs`
- `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/MesManualDispatchOverrideConsumerTests.cs`
- `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingOperationOverrideOverlayTests.cs`
- `backend/tests/Nerv.IIP.Business.Acceptance.Tests/MesDispatchSchedulingOverrideAcceptanceTests.cs`
- `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesIntegrationEventTests.cs` — MES payload/envelope assertions.
- `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventEnvelopeContractTests.cs` — reflection-based envelope contract coverage.
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/integration-event-consumption-matrix.md`
- `docs/architecture/implementation-readiness.md`
- `docs/superpowers/specs/2026-07-14-scheduling-locks-manual-overrides-design.md`

---

### Task 1: Define MES manual-dispatch lifecycle facts

**Files:**
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/ManualDispatchSchedulingLockEventTests.cs`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Mes/MesIntegrationEvents.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/DomainEvents/MesDomainEvents.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventConverters.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventConverters/MesIntegrationEventContext.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs`

**Interfaces:**
- Produces: `OperationTaskManualDispatchSnapshot`, `OperationTaskManualDispatchClearedDomainEvent`, `MesOperationTaskManualDispatchClearedIntegrationEvent`, and positive `DispatchRevision` values consumed by Task 3.
- Produces: `IMesIntegrationEventContextAccessor.GetContext()` returning correlation and causation for the cleared converter.

- [ ] **Step 1: Write failing lifecycle tests**

Add focused tests that express the immutable lifecycle:

```csharp
[Fact]
public void Clearing_a_real_manual_device_raises_one_versioned_clear_event()
{
    var task = NewTask();
    task.Assign(null, "DEV-1", null, At(1), "user:planner");
    task.ClearDomainEvents();

    task.Assign(null, null, null, At(2), "user:planner");

    var cleared = Assert.IsType<OperationTaskManualDispatchClearedDomainEvent>(
        Assert.Single(task.GetDomainEvents()));
    Assert.Equal(2, cleared.Dispatch.DispatchRevision);
    Assert.Equal("DEV-1", cleared.Dispatch.ResourceId);
    Assert.Equal("device-cleared", cleared.ReasonCode);
    Assert.False(task.HasActiveManualDispatch);
}

[Fact]
public void Equal_time_clear_and_reassign_have_distinct_monotonic_revisions()
{
    var task = NewTask();
    task.Assign(null, "DEV-1", null, At(1), "user:planner");
    task.Assign(null, null, null, At(1), "user:planner");
    task.Assign(null, "DEV-2", null, At(1), "user:planner");

    Assert.Equal(3, task.ManualDispatchRevision);
    Assert.True(task.HasActiveManualDispatch);
}
```

Also add tests for repeated null assignment producing no event, cancel producing
`operation-cancelled`, and released schedule assignment not becoming a manual fact.

- [ ] **Step 2: Run the MES lifecycle tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --filter FullyQualifiedName~ManualDispatchSchedulingLockEventTests
```

Expected: compilation/test failure because revision, clear event, and active state do not exist.

- [ ] **Step 3: Add immutable domain snapshots and aggregate transitions**

Define snapshot-style events rather than storing a mutable aggregate reference:

```csharp
public sealed record OperationTaskManualDispatchSnapshot(
    string OrganizationId, string EnvironmentId, string WorkOrderId,
    string OperationTaskId, int OperationSequence, string ResourceId,
    string WorkCenterId, DateTimeOffset StartUtc, DateTimeOffset EndUtc,
    DateTimeOffset OccurredAtUtc, long DispatchRevision);

public sealed record OperationTaskManuallyDispatchedDomainEvent(
    OperationTaskManualDispatchSnapshot Dispatch, string Actor) : IDomainEvent;

public sealed record OperationTaskManualDispatchClearedDomainEvent(
    OperationTaskManualDispatchSnapshot Dispatch, string ReasonCode,
    DateTimeOffset ClearedAtUtc, string Actor) : IDomainEvent;
```

Add `ManualDispatchRevision` and `HasActiveManualDispatch` to `OperationTask`.
Capture the previous device before mutation. Increment the revision for every real
device dispatch/re-dispatch, for real-device to null, and for cancellation while a
manual dispatch is active. Do not increment for null-to-null or released schedule
assignment. Require a canonical non-blank actor before raising either event.

- [ ] **Step 4: Add the public cleared contract and converter context**

Extend the positive payload compatibly:

```csharp
public sealed record OperationTaskManuallyDispatchedPayload(
    string WorkOrderId, string OperationTaskId, int OperationSequence,
    string ResourceId, string WorkCenterId, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, DateTimeOffset AssignedAtUtc,
    long DispatchRevision = 0);
```

Add the cleared envelope/payload:

```csharp
public sealed record OperationTaskManualDispatchClearedPayload(
    string WorkOrderId, string OperationTaskId, int OperationSequence,
    string ResourceId, string WorkCenterId, DateTimeOffset StartUtc,
    DateTimeOffset EndUtc, long DispatchRevision,
    string ReasonCode, DateTimeOffset ClearedAtUtc);
```

Create `MesIntegrationEventContext` and an HTTP implementation following the
Inventory/Scheduling accessors: read `X-Correlation-Id` and `X-Causation-Id`, fall
back to `Activity.Current`, then to version-7 generated values. Register it as scoped
in MES `Program.cs` and inject it into the cleared converter.

Use an idempotency key shaped as:

```csharp
EventIds.Idempotency("operation-task-manual-dispatch-cleared",
    dispatch.OrganizationId, dispatch.EnvironmentId, dispatch.OperationTaskId,
    dispatch.DispatchRevision.ToString(CultureInfo.InvariantCulture), reasonCode)
```

- [ ] **Step 5: Run the lifecycle and contract-focused tests and verify GREEN**

Run the lifecycle filter above plus `MesIntegrationEventTests` and the reflection
contract project that exercise `MesIntegrationEvents.cs`.

Expected: all selected tests pass; positive V1 payloads without the optional revision still deserialize.

- [ ] **Step 6: Commit the MES lifecycle slice**

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.Mes `
  backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain `
  backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web `
  backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/ManualDispatchSchedulingLockEventTests.cs
git commit -m "feat(mes): publish manual dispatch revocation facts"
```

---

### Task 2: Persist MES dispatch revision and cancellation actor

**Files:**
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesPersistenceContractTests.cs`
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesEndpointContractTests.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/OperationTaskEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/*AddMesManualDispatchRevision*`

**Interfaces:**
- Consumes: Task 1 `OperationTask.Cancel(DateTimeOffset, string)` and lifecycle properties.
- Produces: persisted positive revision and authenticated cancellation event used by acceptance tests.

- [ ] **Step 1: Write failing persistence and cancellation tests**

Extend persistence coverage to save, clear the tracker, reload, and assert revision 1
plus active true; clear the device, save/reload, and assert revision 2 plus active
false. Extend the existing released-work-order cancellation test by manually
dispatching a device first and asserting one clear domain event with the endpoint
actor.

- [ ] **Step 2: Run the two focused tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --filter "FullyQualifiedName~Dispatch_assignment_persists|FullyQualifiedName~Cancel_released_work_order"
```

Expected: failure because fields are not mapped and cancellation does not propagate actor.

- [ ] **Step 3: Map lifecycle fields and propagate cancellation actor**

Map:

```csharp
builder.Property(x => x.ManualDispatchRevision)
    .HasColumnName("manual_dispatch_revision").IsRequired().HasDefaultValue(0L)
    .HasComment("Monotonic MES manual-device dispatch lifecycle revision.");
builder.Property(x => x.HasActiveManualDispatch)
    .HasColumnName("has_active_manual_dispatch").IsRequired().HasDefaultValue(false)
    .HasComment("Whether the operation currently owns an active MES manual-device dispatch lock.");
```

Add `Actor = "system:mes"` to `CancelWorkOrderCommand`, pass it through
`WorkOrderCancellationOrchestrator.CancelAsync`, and call
`operationTask.Cancel(cancelledAtUtc, actor)`. The endpoint supplies
`MesAuthenticatedActor.Resolve(HttpContext)`.

- [ ] **Step 4: Generate the MES migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddMesManualDispatchRevision `
  --project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure `
  --startup-project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web
```

Inspect the generated migration for the two columns, defaults, comments, and model snapshot changes.

- [ ] **Step 5: Run MES Web tests and pending-model gate**

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj
$env:Persistence__Provider = "PostgreSQL"
dotnet tool run dotnet-ef migrations has-pending-model-changes `
  --project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure `
  --startup-project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web
```

Expected: MES tests pass and EF reports no pending model changes.

- [ ] **Step 6: Commit MES persistence**

```powershell
git add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure `
  backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web `
  backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests
git commit -m "feat(mes): persist manual dispatch lifecycle revision"
```

---

### Task 3: Add the Scheduling tombstone projection

**Files:**
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/ScheduleOperationOverrideTests.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/ScheduleOperationOverrideAggregate/ScheduleOperationOverride.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/ScheduleOperationOverrideEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/*AddSchedulingOverrideRevocationTombstones*`

**Interfaces:**
- Consumes: Task 1 dispatch revision and clear payload semantics.
- Produces: `TryApplyMesDispatch`, `TryClearMesDispatch`, and `CreateClearedMesDispatch` for Task 4 consumers.

- [ ] **Step 1: Write failing aggregate ordering tests**

Add tests for:

```csharp
Assert.True(fact.TryClearMesDispatch(2, "evt-clear", "user:planner",
    At(2), "device-cleared", At(2)));
Assert.False(fact.IsActive);
Assert.False(fact.TryApplyMesDispatch("DEV-OLD", "WC-1", At(8), At(9),
    "evt-old", "user:planner", 1, At(1), At(3)));
Assert.True(fact.TryApplyMesDispatch("DEV-NEW", "WC-1", At(10), At(11),
    "evt-new", "user:planner", 3, At(2), At(4)));
Assert.True(fact.IsActive);
```

Also prove a MES clear returns false for a `scheduling-api` fact and prove equal
timestamps converge by revision.

- [ ] **Step 2: Run Scheduling domain tests and verify RED**

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj --filter FullyQualifiedName~ScheduleOperationOverrideTests
```

Expected: compilation failure for missing tombstone transitions.

- [ ] **Step 3: Implement active/tombstone and ordering transitions**

Add properties:

```csharp
public bool IsActive { get; private set; } = true;
public long? SourceRevision { get; private set; }
public string? ClearedReasonCode { get; private set; }
public DateTimeOffset? ClearedAtUtc { get; private set; }
```

`TryApplyMesDispatch` compares positive MES revisions when the current lineage is
MES; otherwise it retains the existing source-time rule. On success it sets active,
clears revocation metadata, and stores `SourceType = "mes-dispatch"`.

`TryClearMesDispatch`:

- returns false for `SourceType == "scheduling-api"`;
- returns false when the positive revision is not greater than current positive MES revision;
- falls back to source time only for legacy revision-less state;
- on success keeps the last real resource/window, sets inactive, source event/revision/time, clear reason, and clear time.

`CreateClearedMesDispatch` creates an inactive row from the clear payload so revoke-before-create retains a watermark. `ReplaceManually` resets revision and clear metadata and always activates the row.

- [ ] **Step 4: Map tombstone columns and generate migration**

Map `is_active` with default true, nullable `source_revision`, nullable
`cleared_reason_code` length 64, and nullable `cleared_at_utc`, all with comments.

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool run dotnet-ef migrations add AddSchedulingOverrideRevocationTombstones `
  --project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure `
  --startup-project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web
```

Inspect the migration and snapshot; existing override rows must migrate as active.

- [ ] **Step 5: Run domain, schema, and pending-model tests**

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter FullyQualifiedName~SchedulingSchemaConventionTests
$env:Persistence__Provider = "PostgreSQL"
dotnet tool run dotnet-ef migrations has-pending-model-changes `
  --project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure `
  --startup-project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web
```

Expected: all tests pass and no pending model changes.

- [ ] **Step 6: Commit Scheduling tombstone model**

```powershell
git add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain `
  backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure `
  backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests `
  backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingSchemaConventionTests.cs
git commit -m "feat(scheduling): retain override revocation tombstones"
```

---

### Task 4: Consume cleared events and exclude inactive overrides

**Files:**
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/MesManualDispatchOverrideConsumerTests.cs`
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingOperationOverrideOverlayTests.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventHandlers/MesOperationTaskManuallyDispatchedIntegrationEventHandler.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventHandlers/MesOperationTaskManualDispatchClearedIntegrationEventHandler.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingOperationOverrideOverlay.cs`

**Interfaces:**
- Consumes: Task 3 aggregate transitions and Task 1 public clear contract.
- Produces: persisted consumer behavior used by Task 5 acceptance tests.

- [ ] **Step 1: Write failing consumer and overlay tests**

Cover both delivery orders and final state:

```csharp
await clearHandler.HandleAsync(ClearEvent(revision: 2), CancellationToken.None);
await dispatchHandler.HandleAsync(DispatchEvent(revision: 1), CancellationToken.None);

var fact = await db.ScheduleOperationOverrides.SingleAsync();
Assert.False(fact.IsActive);
Assert.Equal(2, fact.SourceRevision);
```

Add tests for duplicate clear, dispatch 1 -> clear 2 -> dispatch 3, clear not
affecting a Scheduling API row, invalid clear entering the DLQ once, and stale
two-DbContext update convergence. Add an overlay test proving an inactive tombstone
is absent from `LockedAssignments`.

- [ ] **Step 2: Run the focused tests and verify RED**

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "FullyQualifiedName~MesManualDispatchOverrideConsumerTests|FullyQualifiedName~SchedulingOperationOverrideOverlayTests"
```

Expected: compilation/test failures because the cleared handler and active filter do not exist.

- [ ] **Step 3: Pass revision through the positive consumer**

Replace direct `TryReplace` calls with `TryApplyMesDispatch(...,
payload.DispatchRevision, integrationEvent.OccurredAtUtc, ...)`. Preserve legacy
revision zero behavior and the current insert/update retry.

- [ ] **Step 4: Implement the cleared consumer**

Use consumer name
`business-scheduling.mes-operation-manual-dispatch-cleared` and validate the new V1
type. Validate real work order/operation/resource/work-center IDs, positive revision,
recognized reason, and positive prior window. Record the inbox before mutation.

On no row, add `ScheduleOperationOverride.CreateClearedMesDispatch(...)`. On an
existing row, call `TryClearMesDispatch(...)`. Mirror the existing save-race retry:
clear tracker, re-record inbox, reload the current row with `requireExisting`, reapply,
and save. Stale/no-op outcomes return normally.

- [ ] **Step 5: Filter inactive rows in the overlay**

Add `x.IsActive` to the database predicate before mapping overrides into locked assignments.

- [ ] **Step 6: Run full Scheduling Web tests**

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj
```

Expected: all Scheduling Web tests pass with no poison-message regression.

- [ ] **Step 7: Commit Scheduling consumption**

```powershell
git add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web `
  backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests
git commit -m "feat(scheduling): consume MES override revocations"
```

---

### Task 5: Prove the real MES-to-Scheduling lifecycle

**Files:**
- Modify: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/MesDispatchSchedulingOverrideAcceptanceTests.cs`
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesIntegrationEventTests.cs`
- Modify: `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventEnvelopeContractTests.cs`

**Interfaces:**
- Consumes: Tasks 1-4 complete production path.
- Produces: acceptance evidence for establish -> revoke -> optimize and revoke -> reassign.

- [ ] **Step 1: Write the failing cross-boundary acceptance test**

Extend the existing test to execute:

```csharp
await dispatchHandler.Handle(assignDevice2, CancellationToken.None);
var dispatched = ConvertSingleDispatch(mesTask);
await dispatchConsumer.HandleAsync(dispatched, CancellationToken.None);

await dispatchHandler.Handle(clearDevice, CancellationToken.None);
var cleared = ConvertSingleClear(mesTask, mesEventContext);
await clearConsumer.HandleAsync(cleared, CancellationToken.None);

var overlaid = await overlay.ApplyAsync(CreateProblem(start), CancellationToken.None);
var plan = scheduler.Schedule(overlaid, "plan-after-clear", start.AddMinutes(-1));
var assignment = Assert.Single(plan.Assignments);
Assert.False(assignment.IsLocked);
Assert.Equal(0, plan.Metrics.LockedOperationCount);
Assert.Equal(1, plan.Metrics.OptimizableOperationCount);
Assert.NotEqual("DEVICE-2", assignment.ResourceId);
```

Add a second acceptance case for revision 3 re-dispatch after revision 2 clear and
then replay revisions 1/2, asserting the new resource stays locked.

- [ ] **Step 2: Run acceptance test and verify RED**

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --filter FullyQualifiedName~MesDispatchSchedulingOverrideAcceptanceTests
```

Expected: failure until all real command/converter/consumer wiring is correct.

- [ ] **Step 3: Complete only the minimal wiring exposed by the acceptance failure**

Fix constructor registrations, event converter test stubs, or domain-event clearing
needed for the real chain. Do not replace the real MES command or Scheduling handler
with fabricated production state.

- [ ] **Step 4: Run acceptance and contract suites and verify GREEN**

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --filter FullyQualifiedName~MesDispatchSchedulingOverrideAcceptanceTests
dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj
```

Expected: establish, revoke, optimization recovery, re-dispatch, duplicate, and stale replay pass.

- [ ] **Step 5: Commit cross-boundary acceptance**

```powershell
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests `
  backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests `
  backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesIntegrationEventTests.cs
git commit -m "test: prove MES scheduling override revocation lifecycle"
```

---

### Task 6: Update governance and run release gates

**Files:**
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/integration-event-consumption-matrix.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/superpowers/specs/2026-07-14-scheduling-locks-manual-overrides-design.md`

**Interfaces:**
- Consumes: final event names, migration names, columns, and verified behavior from Tasks 1-5.
- Produces: reviewable architecture/governance record and PR evidence.

- [ ] **Step 1: Update architecture records with code facts**

Add the new MES cleared event row immediately after
`mes.OperationTaskManuallyDispatched`. State that Scheduling retains an inactive
tombstone, orders by positive dispatch revision, falls back to source time for legacy
dispatches, treats stale/duplicate/lineage mismatch as successful no-op, and sends
invalid payloads to the DLQ.

Record both migration names and new columns in the schema catalog. Update readiness
to say #933 closes cancel/clear lifecycle while #701 remains released-plan governance.
Amend the #700 design out-of-scope statement so only completion/timeout/release/revoke
automatic cleanup remains excluded.

- [ ] **Step 2: Run documentation and diff checks**

```powershell
rg -n "OperationTaskManualDispatchCleared|#933|manual_dispatch_revision|is_active" docs/architecture docs/superpowers/specs
git diff --check
```

Expected: all four concepts are documented and `git diff --check` exits zero.

- [ ] **Step 3: Run targeted verification fresh**

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj
dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj
```

Expected: all targeted suites pass. Report environment-gated PostgreSQL skips separately.

- [ ] **Step 4: Run backend solution gate**

```powershell
dotnet test backend/Nerv.IIP.sln
```

Expected: exit zero with no new warnings; if an unrelated baseline failure appears,
rerun the exact failing test and document code-fact evidence before deciding status.

- [ ] **Step 5: Commit governance updates**

```powershell
git add docs/architecture docs/superpowers/specs/2026-07-14-scheduling-locks-manual-overrides-design.md
git commit -m "docs: record MES scheduling override revocation closure"
```

- [ ] **Step 6: Review final diff and create PR**

```powershell
git status --short
git diff origin/main...HEAD --check
git diff --stat origin/main...HEAD
git log --oneline origin/main..HEAD
git push -u origin codex/issue-933-mes-scheduling-override-revocation
gh pr create --repo Mang-X/Nerv-IIP --base main `
  --head codex/issue-933-mes-scheduling-override-revocation `
  --title "Fix #933 MES manual dispatch override revocation" `
  --body "## Summary`n- publish versioned MES manual-dispatch cleared facts with monotonic operation revisions`n- retain inactive Scheduling override tombstones so stale events cannot revive old locks`n- prove establish, revoke, optimization recovery, and re-dispatch across the real MES-to-Scheduling code path`n`n## Verification`n- dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj`n- dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj`n- dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj`n- dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj`n- dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj`n- dotnet test backend/Nerv.IIP.sln`n`n## Impact`n- PostgreSQL migrations: MES manual-dispatch lifecycle revision; Scheduling override revocation tombstones`n- Endpoint declaration: not applicable — no business HTTP endpoint was added or changed`n- 文档：有影响，已更新事件消费矩阵、schema catalog、readiness 与 #700 设计边界`n`nFixes #933"
```

The PR body must include `Fixes #933`, targeted/full verification evidence, migration
impact, `文档：有影响`, and `Endpoint declaration: not applicable — no business HTTP
endpoint was added or changed`.
