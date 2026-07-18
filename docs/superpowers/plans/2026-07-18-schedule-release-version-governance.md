# MAN-385 #701 Schedule Release Version Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make each Scheduling scope have one active released plan, explicitly revoke the prior release, and keep MES operation tasks aligned with the newest release under concurrency, replay, and out-of-order delivery.

**Architecture:** Scheduling owns lifecycle state and allocates a monotonic release revision while holding a PostgreSQL transaction advisory lock for `organizationId + environmentId`; a partial unique index is the final active-release invariant. Scheduling publishes a public revoke fact, and MES stores the real source plan ID/revision so release and revoke handlers converge without overwriting manual-dispatch or execution facts.

**Tech Stack:** .NET 10, C#, CleanDDD/NetCorePal commands and unit of work, EF Core, PostgreSQL/Npgsql, CAP outbox/inbox, FastEndpoints, xUnit, BusinessGateway OpenAPI, Hey API/pnpm.

---

## File Map

- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/SchedulePlanAggregate/SchedulePlan.cs`: lifecycle state and invariant methods.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/DomainEvents/SchedulingDomainEvents.cs`: revoke domain fact.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/ReleaseSchedulePlanCommand.cs`: locked release/supersede transaction.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/RevokeSchedulePlanCommand.cs`: explicit revoke command.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ScheduleReleaseScopeLock.cs`: PostgreSQL transaction advisory lock and revision query.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/SchedulePlanEntityTypeConfiguration.cs`: lifecycle columns and unique indexes.
- `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs`: release revision and revoke event contract.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventConverters/SchedulingIntegrationEventConverters.cs`: release/revoke envelope conversion.
- `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`: source-plan provenance and revoke behavior.
- `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/SchedulingPlanReleasedIntegrationEventHandler.cs`: ordered release/revoke consumers and non-poison rejection.
- `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints/Scheduling/SchedulingEndpoints.cs`: internal Scheduling revoke HTTP endpoint and contract registry.
- `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Scheduling/BusinessConsoleSchedulingEndpoints.cs`: exposed facade.
- `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`: Scheduling revoke proxy client.
- `backend/tests/Nerv.IIP.Business.Acceptance.Tests/SchedulingReleaseGovernanceAcceptanceTests.cs`: real cross-service lifecycle evidence.
- Scheduling/MES migration and snapshot files: schema rollout.
- Architecture matrices/catalog and product docs: governed documentation.

### Task 1: Define SchedulePlan lifecycle with domain-first tests

**Files:**
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/SchedulePlanAggregateTests.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/SchedulePlanAggregate/SchedulePlan.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/DomainEvents/SchedulingDomainEvents.cs`

- [ ] **Step 1: Write failing aggregate tests**

```csharp
[Fact]
public void Released_plan_can_be_superseded_once()
{
    var oldPlan = CreatePlan("plan-v1");
    oldPlan.Release(FixedNow, 1);

    oldPlan.Supersede("plan-v2", FixedNow.AddMinutes(1));
    oldPlan.Supersede("plan-v2", FixedNow.AddMinutes(1));

    Assert.Equal(SchedulePlanLifecycleStatus.Superseded, oldPlan.Status);
    Assert.Equal(1, oldPlan.ReleaseRevision);
    Assert.Equal("plan-v2", oldPlan.SupersededByPlanId);
    Assert.Single(oldPlan.DomainEvents.OfType<SchedulePlanRevokedDomainEvent>());
}

[Fact]
public void Released_plan_can_be_explicitly_revoked_idempotently()
{
    var plan = CreatePlan("plan-v1");
    plan.Release(FixedNow, 4);

    plan.Revoke(FixedNow.AddMinutes(1));
    plan.Revoke(FixedNow.AddMinutes(2));

    Assert.Equal(SchedulePlanLifecycleStatus.Revoked, plan.Status);
    Assert.Equal(SchedulePlanRevocationReason.Explicit, plan.RevocationReason);
    Assert.Single(plan.DomainEvents.OfType<SchedulePlanRevokedDomainEvent>());
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run: `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj --filter "Released_plan_can_be"`

Expected: compile failure because lifecycle values, revision, revoke methods, and event do not exist.

- [ ] **Step 3: Implement the lifecycle API**

```csharp
public enum SchedulePlanLifecycleStatus { Generated, Released, Superseded, Revoked }
public enum SchedulePlanRevocationReason { Superseded, Explicit }

public void Release(DateTimeOffset releasedAtUtc, long releaseRevision)
{
    if (Status == SchedulePlanLifecycleStatus.Released) return;
    if (Status is SchedulePlanLifecycleStatus.Superseded or SchedulePlanLifecycleStatus.Revoked)
        throw new InvalidOperationException("Terminal schedule plan cannot be released.");
    if (releaseRevision <= 0) throw new ArgumentOutOfRangeException(nameof(releaseRevision));
    Status = SchedulePlanLifecycleStatus.Released;
    ReleasedAtUtc = releasedAtUtc;
    ReleaseRevision = releaseRevision;
    this.AddDomainEvent(new SchedulePlanReleasedDomainEvent(this));
}

public void Supersede(string successorPlanId, DateTimeOffset revokedAtUtc) =>
    RevokeCore(SchedulePlanLifecycleStatus.Superseded, SchedulePlanRevocationReason.Superseded, successorPlanId, revokedAtUtc);

public void Revoke(DateTimeOffset revokedAtUtc) =>
    RevokeCore(SchedulePlanLifecycleStatus.Revoked, SchedulePlanRevocationReason.Explicit, null, revokedAtUtc);
```

Add `SchedulePlanRevokedDomainEvent(SchedulePlan SchedulePlan)` and a private `RevokeCore` that is idempotent only for the same terminal fact and rejects generated plans.

- [ ] **Step 4: Run the full Scheduling domain project**

Run: `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests
git commit -m "feat(scheduling): model release revoke lifecycle"
```

### Task 2: Add public revoke contract and converter

**Files:**
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventConverters/SchedulingIntegrationEventConverters.cs`
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingIntegrationEventTests.cs`

- [ ] **Step 1: Write failing contract/converter tests**

```csharp
[Fact]
public void Revoked_event_carries_real_plan_revision_successor_and_operations()
{
    var plan = CreateReleasedPlan("plan-v1", releaseRevision: 7);
    plan.Supersede("plan-v2", FixedNow);

    var message = converter.Convert(new SchedulePlanRevokedDomainEvent(plan));

    Assert.Equal("scheduling.SchedulePlanRevoked", message.EventType);
    Assert.Equal("plan-v1", message.Payload.PlanId);
    Assert.Equal(7, message.Payload.ReleaseRevision);
    Assert.Equal("superseded", message.Payload.Reason);
    Assert.Equal("plan-v2", message.Payload.SupersededByPlanId);
    Assert.Equal(plan.Assignments.Select(x => x.OperationId), message.Payload.AffectedOperations.Select(x => x.OperationId));
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "Revoked_event|Integration_event"`

Expected: compile failure for the new public contract and converter.

- [ ] **Step 3: Add the additive v1 contracts**

```csharp
public const string SchedulePlanRevoked = "scheduling.SchedulePlanRevoked";

public sealed record SchedulePlanRevokedPayload(
    string PlanId,
    string ProblemId,
    int ContractVersion,
    string AlgorithmVersion,
    string ProblemFingerprint,
    long ReleaseRevision,
    string Reason,
    string? SupersededByPlanId,
    IReadOnlyCollection<SchedulePlanAffectedOperationPayload> AffectedOperations);
```

Add `long? ReleaseRevision = null` as the final optional member of `SchedulePlanLifecyclePayload`, add the full envelope record, and convert the domain event with an idempotency key containing plan ID, revision, and reason.

- [ ] **Step 4: Run focused tests**

Run: `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "SchedulingIntegrationEventTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add backend/common/Contracts/Nerv.IIP.Contracts.Scheduling backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventConverters backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingIntegrationEventTests.cs
git commit -m "feat(scheduling): publish schedule revoke fact"
```

### Task 3: Implement locked release and explicit revoke commands

**Files:**
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ScheduleReleaseScopeLock.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/ReleaseSchedulePlanCommand.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/RevokeSchedulePlanCommand.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Program.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/ScheduleReleaseGovernanceCommandTests.cs`

- [ ] **Step 1: Write failing command tests**

```csharp
[Fact]
public async Task Second_release_supersedes_current_plan_before_releasing_successor()
{
    await SeedPlansAsync("plan-v1", "plan-v2");
    await handler.Handle(new ReleaseSchedulePlanCommand("plan-v1", Org, Env), default);
    await db.SaveChangesAsync();

    var result = await handler.Handle(new ReleaseSchedulePlanCommand("plan-v2", Org, Env), default);
    await db.SaveChangesAsync();

    Assert.Equal(2, result.ReleaseRevision);
    Assert.Equal(SchedulePlanLifecycleStatus.Superseded, await StatusOf("plan-v1"));
    Assert.Equal(SchedulePlanLifecycleStatus.Released, await StatusOf("plan-v2"));
}

[Fact]
public async Task Explicit_revoke_is_idempotent_without_a_successor()
{
    var first = await revokeHandler.Handle(new RevokeSchedulePlanCommand("plan-v1", Org, Env), default);
    await db.SaveChangesAsync();
    var second = await revokeHandler.Handle(new RevokeSchedulePlanCommand("plan-v1", Org, Env), default);
    Assert.Equal(first.RevokedAtUtc, second.RevokedAtUtc);
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "ScheduleReleaseGovernanceCommandTests"`

Expected: compile failure for scope lock, revision response, and revoke command.

- [ ] **Step 3: Implement the infrastructure boundary**

```csharp
public interface IScheduleReleaseScopeLock
{
    Task<IAsyncDisposable> AcquireAsync(string organizationId, string environmentId, CancellationToken cancellationToken);
}

public sealed class PostgreSqlScheduleReleaseScopeLock(ApplicationDbContext dbContext) : IScheduleReleaseScopeLock
{
    public async Task<IAsyncDisposable> AcquireAsync(string organizationId, string environmentId, CancellationToken cancellationToken)
    {
        var key = $"scheduling-release:{organizationId.Trim()}:{environmentId.Trim()}";
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        return NoopAsyncDisposable.Instance;
    }
}
```

Keep provider SQL in Infrastructure and register the implementation in `Program.cs`. Test providers inject a no-op serialized lock.

- [ ] **Step 4: Implement both handlers**

Under the acquired lock, load the target and active released header, validate target eligibility, compute `nextRevision = Max(ReleaseRevision) + 1`, supersede old only after validation, then release target. Revoke only a currently released plan and return the original terminal result on replay.

```csharp
public sealed record RevokeSchedulePlanCommand(string PlanId, string OrganizationId, string EnvironmentId)
    : ICommand<RevokeSchedulePlanResponse>;

public sealed record RevokeSchedulePlanResponse(
    string PlanId,
    SchedulePlanStatusContract Status,
    long ReleaseRevision,
    DateTimeOffset? RevokedAtUtc,
    string Reason,
    string? SupersededByPlanId);
```

- [ ] **Step 5: Run focused and regression tests**

Run: `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "ScheduleReleaseGovernanceCommandTests|ReleaseSchedulePlanInvalidationGateTests|SchedulingEndpointContractTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add backend/services/Business/Scheduling/src backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests
git commit -m "feat(scheduling): govern concurrent plan releases"
```

### Task 4: Add Scheduling PostgreSQL schema and real concurrency proof

**Files:**
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/SchedulePlanEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Create: EF-generated `AddScheduleReleaseGovernance` migration files under `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/ScheduleReleaseGovernancePostgresProfileTests.cs`
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingSchemaConventionTests.cs`

- [ ] **Step 1: Write PostgreSQL tests before the migration**

```csharp
[SchedulingPostgresFact]
public async Task Concurrent_releases_converge_to_one_active_plan_with_monotonic_revisions()
{
    await SeedGeneratedPlansAsync(database, "plan-v1", "plan-v2");
    await Task.WhenAll(ReleaseAsync("plan-v1"), ReleaseAsync("plan-v2"));

    var released = await ReadPlans().Where(x => x.Status == SchedulePlanLifecycleStatus.Released).ToListAsync();
    Assert.Single(released);
    Assert.Equal(new long[] { 1, 2 }, (await ReadPlans().OrderBy(x => x.ReleaseRevision).ToListAsync()).Select(x => x.ReleaseRevision));
}
```

Also seed duplicate historical released rows with equal timestamps through pre-migration SQL and assert deterministic normalization after migration.

- [ ] **Step 2: Verify RED on real PostgreSQL**

Run: `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "ScheduleReleaseGovernancePostgresProfileTests"` with `NERV_IIP_TEST_POSTGRES` already set to the verified local PostgreSQL admin connection.

Expected: FAIL because columns, migration, indexes, and locking behavior are absent.

- [ ] **Step 3: Configure columns and indexes**

```csharp
builder.Property(x => x.ReleaseRevision).HasColumnName("release_revision").HasComment("Monotonic release revision within organization and environment scope.");
builder.Property(x => x.RevokedAtUtc).HasColumnName("revoked_at_utc").HasComment("UTC instant the released plan was superseded or explicitly revoked.");
builder.Property(x => x.SupersededByPlanId).HasColumnName("superseded_by_plan_id").HasMaxLength(96).HasComment("Successor plan id for automatic supersession.");
builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId }).IsUnique().HasFilter("status = 'Released'");
builder.HasIndex(x => new { x.OrganizationId, x.EnvironmentId, x.ReleaseRevision }).IsUnique().HasFilter("release_revision IS NOT NULL");
```

- [ ] **Step 4: Generate and inspect the formal migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddScheduleReleaseGovernance --project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure --startup-project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web
```

Add deterministic PostgreSQL backfill SQL before creating the partial index; retain generated designer and snapshot files.

- [ ] **Step 5: Run PostgreSQL and schema tests**

Run: `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "ScheduleReleaseGovernancePostgresProfileTests|SchedulingSchemaConventionTests"`

Expected: PASS with PostgreSQL tests executed, not skipped.

- [ ] **Step 6: Commit**

```powershell
git add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests
git commit -m "feat(scheduling): enforce one active release per scope"
```

### Task 5: Persist MES plan provenance and consume revoke facts

**Files:**
- Modify: `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`
- Modify: `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/OperationTaskEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEventHandlers/SchedulingPlanReleasedIntegrationEventHandler.cs`
- Modify: `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Web/MesIntegrationEventConsumerRegistrationExtensions.cs`
- Modify: `backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/SchedulingPlanReleasedHandlerTests.cs`
- Create: `backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/SchedulingPlanRevokedHandlerTests.cs`

- [ ] **Step 1: Write failing MES behavior tests**

```csharp
[Fact]
public async Task Late_old_revoke_does_not_clear_new_release()
{
    await HandleReleaseAsync(planId: "plan-v2", revision: 2, operationId: RealOperationId);
    await HandleRevokeAsync(planId: "plan-v1", revision: 1, operationId: RealOperationId);

    var task = await db.OperationTasks.SingleAsync();
    Assert.Equal("plan-v2", task.SchedulePlanId);
    Assert.Equal(2, task.ScheduleReleaseRevision);
}

[Fact]
public async Task Revoke_clears_matching_schedule_but_preserves_manual_dispatch()
{
    var task = await SeedManuallyDispatchedTaskAsync();
    task.ApplyScheduleAssignment("plan-v1", 1, WorkCenter, ScheduledDevice, Start, End, FixedNow);
    await HandleRevokeAsync("plan-v1", 1, task.OperationTaskId);

    Assert.Null(task.SchedulePlanId);
    Assert.True(task.HasActiveManualDispatch);
    Assert.Equal(ManualDevice, task.DeviceAssetId);
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --filter "SchedulingPlanRevokedHandlerTests|Late_old_revoke"`

Expected: compile failure for provenance properties, revised assignment signature, and revoke handler.

- [ ] **Step 3: Implement aggregate ordering and revoke behavior**

```csharp
public bool ApplyScheduleAssignment(string planId, long releaseRevision, string workCenterId, string? deviceAssetId,
    DateTimeOffset plannedStartUtc, DateTimeOffset plannedEndUtc, DateTimeOffset assignedAtUtc, string? operationCode = null)
{
    if (ScheduleReleaseRevision is > 0 && releaseRevision < ScheduleReleaseRevision) return false;
    // existing execution/manual-dispatch guards remain
    SchedulePlanId = DomainGuard.Required(planId, nameof(planId));
    ScheduleReleaseRevision = releaseRevision;
    ScheduledAtUtc = assignedAtUtc;
    return true;
}

public bool RevokeScheduleAssignment(string planId, long releaseRevision, string reasonCode)
{
    if (SchedulePlanId != planId || ScheduleReleaseRevision != releaseRevision) return false;
    SchedulePlanId = null;
    ScheduleReleaseRevision = null;
    ScheduledAtUtc = null;
    MarkScheduleInvalidated(reasonCode);
    return true;
}
```

Keep manual resource fields intact. Closed/in-execution states retain history and safely ignore destructive clearing.

- [ ] **Step 4: Implement guarded consumers**

Register `SchedulePlanRevokedIntegrationEventHandlerForWithdrawDispatch`. Released invalid windows create an `IntegrationEventDeadLetterMessage` and return; they do not throw `KnownException`. Legacy released messages without a revision apply only when the task has no governed revision.

- [ ] **Step 5: Run MES tests**

Run: `dotnet test backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --filter "SchedulingPlanReleasedHandlerTests|SchedulingPlanRevokedHandlerTests|ManualDispatch"`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add backend/services/Business/MES/src backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests
git commit -m "feat(mes): converge schedule release and revoke facts"
```

### Task 6: Add MES migration and schema proof

**Files:**
- Modify: `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Create: EF-generated `AddMesSchedulePlanProvenance` migration files under `backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/`
- Modify: `backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/MesSchemaConventionTests.cs`
- Modify: `backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/MesPersistenceContractTests.cs`

- [ ] **Step 1: Add failing schema/persistence assertions**

```csharp
Assert.Equal("schedule_plan_id", entity.FindProperty(nameof(OperationTask.SchedulePlanId))!.GetColumnName());
Assert.Equal("schedule_release_revision", entity.FindProperty(nameof(OperationTask.ScheduleReleaseRevision))!.GetColumnName());
Assert.Contains(entity.GetIndexes(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[]
{
    nameof(OperationTask.OrganizationId), nameof(OperationTask.EnvironmentId), nameof(OperationTask.SchedulePlanId)
}));
```

- [ ] **Step 2: Verify RED, generate migration, and inspect comments**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool run dotnet-ef migrations add AddMesSchedulePlanProvenance --project backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Infrastructure --startup-project backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Web
```

Expected initial test failure becomes PASS after the generated migration, snapshot, comments, and index are present.

- [ ] **Step 3: Run MES schema/persistence tests**

Run: `dotnet test backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --filter "MesSchemaConventionTests|MesPersistenceContractTests"`

Expected: PASS.

- [ ] **Step 4: Commit**

```powershell
git add backend/services/Business/MES/src/Nerv.IIP.Business.Mes.Infrastructure backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests
git commit -m "feat(mes): persist schedule plan provenance"
```

### Task 7: Prove the real cross-service closed loop

**Files:**
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/SchedulingReleaseGovernanceAcceptanceTests.cs`
- Modify: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj`

- [ ] **Step 1: Write the failing acceptance test**

Use actual Scheduling plan aggregates/commands and converters to produce plan A release, plan A revoke, and plan B release events. Feed those exact public messages to actual MES handlers backed by its real DbContext.

```csharp
[Fact]
public async Task Two_releases_then_explicit_revoke_converge_mes_and_replay_is_idempotent()
{
    var v1 = await scheduling.ReleaseAsync("plan-v1");
    await mes.ConsumeAsync(v1.ReleasedEvent);

    var v2 = await scheduling.ReleaseAsync("plan-v2");
    await mes.ConsumeAsync(v2.ReleasedEvent);
    await mes.ConsumeAsync(v2.RevokedPriorEvent); // deliberate cross-topic reversal

    Assert.All(await mes.TasksForScopeAsync(), task => Assert.Equal("plan-v2", task.SchedulePlanId));

    var revoke = await scheduling.RevokeAsync("plan-v2");
    await mes.ConsumeAsync(revoke.Event);
    await mes.ConsumeAsync(revoke.Event); // original event replay

    Assert.All(await mes.TasksForScopeAsync(), task => Assert.Null(task.SchedulePlanId));
}
```

- [ ] **Step 2: Verify RED, add only necessary project references, then make GREEN**

Run: `dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --filter "SchedulingReleaseGovernanceAcceptanceTests"`

Expected first run: compile/test failure until the real harness wiring is complete. Final run: PASS with persisted MES business assertions.

- [ ] **Step 3: Commit**

```powershell
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: prove scheduling revoke reaches mes"
```

### Task 8: Expose revoke through Scheduling and BusinessGateway

**Files:**
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints/Scheduling/SchedulingEndpoints.cs`
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEndpointContractTests.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Scheduling/BusinessConsoleSchedulingEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `docs/architecture/facade-coverage-matrix.json`

- [ ] **Step 1: Write failing endpoint, authorization, proxy, OpenAPI, and facade coverage tests**

Assert Scheduling operation ID `revokeSchedulingPlan`, facade route `/api/business-console/v1/scheduling/plans/{planId}/revoke`, permission `business.scheduling.plans.release`, and exact downstream query scope.

- [ ] **Step 2: Verify RED**

Run the Scheduling endpoint tests, BusinessGateway scheduling proxy/OpenAPI/authorization filters, and facade coverage tests. Expected: missing endpoint and matrix row failures.

- [ ] **Step 3: Implement the endpoints and declare `exposed`**

```csharp
[HttpPost("/api/business-console/v1/scheduling/plans/{planId}/revoke")]
[EndpointName("revokeBusinessConsoleSchedulingPlan")]
public sealed class RevokeBusinessConsoleSchedulingPlanEndpoint(...)
    : AuthorizedBusinessSchedulingProxyEndpoint<BusinessConsoleSchedulingPlanRequest, BusinessConsoleRevokeSchedulePlanResponse>(
        client, authorizationService, BusinessGatewayPermissions.SchedulingPlansRelease)
{
    protected override Task<BusinessServiceResponse<BusinessConsoleRevokeSchedulePlanResponse>> ForwardAsync(
        BusinessConsoleSchedulingPlanRequest request, CancellationToken cancellationToken) =>
        Client.RevokeSchedulePlanAsync(request, cancellationToken);
}
```

Register the live Scheduling endpoint in `SchedulingEndpointContracts.All` and add an `exposed` matrix row pointing at the BusinessGateway path.

- [ ] **Step 4: Run focused gateway and facade tests**

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests backend/gateway/BusinessGateway docs/architecture/facade-coverage-matrix.json
git commit -m "feat(gateway): expose schedule plan revoke"
```

### Task 9: Refresh governed contracts and documentation

**Files:**
- Modify: governed OpenAPI snapshots produced by repository export tooling
- Modify: generated files under `frontend/packages/api-client/`
- Modify: `docs/architecture/integration-event-consumption-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/facade-coverage-matrix.md`
- Modify: relevant Scheduling guide under `frontend/apps/docs/`

- [ ] **Step 1: Update narrative docs with exact lifecycle semantics**

Document one active released plan per scope, release revision, automatic supersede, explicit revoke, MES provenance, manual-dispatch preservation, inbox/replay behavior, both migrations, and `exposed` facade status.

- [ ] **Step 2: Export OpenAPI through the governed repository command**

Run: `pwsh scripts/export-gateway-openapi.ps1`

Expected: the governed script updates `frontend/packages/api-client/openapi/platform-gateway.v1.json` and `frontend/packages/api-client/openapi/business-gateway-console.v1.json`; no snapshot is hand-edited.

- [ ] **Step 3: Regenerate the client**

Run: `pnpm -C frontend generate:api`

Expected: generated revoke operation and updated status/revision types appear in `@nerv-iip/api-client`.

- [ ] **Step 4: Run generated-client and touched-file formatting checks**

Run the API-client typecheck/build commands prescribed by the codegen document and `pnpm -C frontend exec vp fmt --check` for every manually touched frontend documentation file.

- [ ] **Step 5: Commit**

```powershell
git add docs/architecture frontend/apps/docs frontend/packages/api-client
git commit -m "docs: govern schedule release and revoke contracts"
```

### Task 10: Verification, review, and branch handoff

**Files:**
- Verify all files changed since `origin/main`
- Modify only defects found by verification/review

- [ ] **Step 1: Run targeted projects**

Run Scheduling Domain/Web, MES Web, BusinessGateway Web, and Business Acceptance test projects. Expected: PASS, with PostgreSQL facts executed when selected.

- [ ] **Step 2: Run PostgreSQL-specific tests with a real connection**

Run `ScheduleReleaseGovernancePostgresProfileTests` and confirm the test output contains passed tests rather than skips.

- [ ] **Step 3: Run repository gates**

Run `scripts/verify-business-scheduling-aps-lite.ps1` after restoring AppHost if required, then `dotnet test backend/Nerv.IIP.sln -m:1`. Expected: PASS or a separately evidenced pre-existing/environment-only failure.

- [ ] **Step 4: Invoke `verification-before-completion`**

Re-run fresh commands required by that skill and record exact pass/fail/skip counts.

- [ ] **Step 5: Invoke `requesting-code-review`**

Review `origin/main...HEAD`, fix every valid finding with TDD, and repeat relevant tests.

- [ ] **Step 6: Invoke `finishing-a-development-branch`**

Verify only #701 files are committed, `skills-lock.json` remains excluded unless independently proven required, and no AppHub/IndustrialTelemetry/Connector Host file is changed.

- [ ] **Step 7: Push and create the PR**

Push `codex/man-385-701-schedule-version-governance`. Create a PR with title beginning `MAN-385 #701`; body sections are `Fix`, `Tests`, `Risk`, `OpenAPI or schema impact`, product documentation impact, and `Fixes #701`.

- [ ] **Step 8: Stop**

Report the PR URL, exact verification evidence, migrations/contracts/docs impact, and remaining uncommitted `skills-lock.json` state. Do not merge and do not start #717.
