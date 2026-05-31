# BusinessScheduling APS Lite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #206 by creating the BusinessScheduling service, stable APS lite contracts, deterministic finite-capacity scheduler, persistence/API surface, BusinessGateway facade and verification script.

**Architecture:** BusinessScheduling is a CleanDDD business service under `backend/services/Business/Scheduling` with schema `scheduling`. The pure scheduler consumes a fully materialized `SchedulingProblem` and returns `SchedulePlan`; service endpoints and adapters handle persistence, OpenAPI, permissions and events. MES, #78 Gantt and BusinessGateway consume Scheduling outputs; they do not calculate official schedules.

**Tech Stack:** .NET 10, NetCorePal CleanDDD template, FastEndpoints, EF Core PostgreSQL, xUnit, ADR 0011 integration event conversion, `Nerv.IIP.Testing` schema convention helpers, Hey API generated `@nerv-iip/api-client`.

---

## Specification

Use `docs/superpowers/specs/2026-05-31-business-scheduling-aps-lite-design.md` as the domain contract for this plan. ADR 0014 is the architectural authority for APS/MES/IIoT boundaries.

## Files

- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/Nerv.IIP.Contracts.Scheduling.csproj`
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs`
- Create: `backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/SchedulingContractSerializationTests.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/Nerv.IIP.Business.Scheduling.Domain.csproj`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Nerv.IIP.Business.Scheduling.Infrastructure.csproj`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/SchedulePlanAggregate/SchedulePlan.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Auth/SchedulingPermissionCodes.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/FiniteCapacityScheduler.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/ShockAbsorberSchedulingFixture.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/*.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Queries/*.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEvents/SchedulingIntegrationEvents.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventConverters/SchedulingIntegrationEventConverters.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints/Scheduling/SchedulingEndpoints.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/SchedulePlanAggregateTests.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/FiniteCapacitySchedulerTests.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEndpointContractTests.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingIntegrationEventTests.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingSchemaConventionTests.cs`
- Modify: `backend/Nerv.IIP.sln`
- Modify: `backend/Directory.Packages.props` only if the new Scheduling projects introduce a central package version requirement already used by sibling services
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Scheduling/BusinessConsoleSchedulingEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/*`
- Modify: `docs/architecture/authorization-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Create: `scripts/verify-business-scheduling-aps-lite.ps1`

## Task 1: Create Scheduling Contracts First

- [ ] **Step 1: Create contract and test project shells**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Contracts.Scheduling -o backend/common/Contracts/Nerv.IIP.Contracts.Scheduling --framework net10.0
dotnet new xunit -n Nerv.IIP.Contracts.Scheduling.Tests -o backend/tests/Nerv.IIP.Contracts.Scheduling.Tests --framework net10.0
dotnet add backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj reference backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/Nerv.IIP.Contracts.Scheduling.csproj
```

Expected: the contract and test projects exist, but no scheduling records exist yet.

- [ ] **Step 2: Write failing contract serialization tests**

Create `backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/SchedulingContractSerializationTests.cs` with tests named:

```csharp
[Fact]
public void Scheduling_problem_round_trips_contract_version_and_core_inputs()
{
    var problem = SchedulingContractSamples.CreateShockAbsorberProblem();
    var json = JsonSerializer.Serialize(problem, SchedulingJson.Options);
    var roundTrip = JsonSerializer.Deserialize<SchedulingProblemContract>(json, SchedulingJson.Options);

    Assert.NotNull(roundTrip);
    Assert.Equal(1, roundTrip!.ContractVersion);
    Assert.Equal("org-001", roundTrip.OrganizationId);
    Assert.Contains(roundTrip.Orders, x => x.OrderId == "WO-RUSH-REAR-001");
    Assert.Contains(roundTrip.Resources, x => x.ResourceId == "DEV-OIL-01");
}

[Fact]
public void Schedule_plan_round_trips_assignments_conflicts_and_gantt_items()
{
    var plan = SchedulingContractSamples.CreateExpectedShockAbsorberPlan();
    var json = JsonSerializer.Serialize(plan, SchedulingJson.Options);
    var roundTrip = JsonSerializer.Deserialize<SchedulePlanContract>(json, SchedulingJson.Options);

    Assert.NotNull(roundTrip);
    Assert.Equal("aps-lite-v1", roundTrip!.AlgorithmVersion);
    Assert.NotEmpty(roundTrip.Assignments);
    Assert.NotEmpty(roundTrip.ResourceLoads);
    Assert.NotEmpty(roundTrip.GanttItems);
}
```

The sample helper can live in the same test file until production contracts exist.

- [ ] **Step 3: Run tests and verify RED**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj --no-restore
```

Expected: FAIL because `Nerv.IIP.Contracts.Scheduling` and the contract records do not exist.

- [ ] **Step 4: Implement minimal contracts**

Create `SchedulingContracts.cs` with public records and enums:

```csharp
public sealed record SchedulingProblemContract(
    int ContractVersion,
    string ProblemId,
    string OrganizationId,
    string EnvironmentId,
    DateTimeOffset HorizonStartUtc,
    DateTimeOffset HorizonEndUtc,
    IReadOnlyCollection<SchedulingOrderContract> Orders,
    IReadOnlyCollection<SchedulingResourceContract> Resources,
    IReadOnlyCollection<SchedulingCalendarContract> Calendars,
    IReadOnlyCollection<SchedulingUnavailabilityWindowContract> UnavailabilityWindows,
    IReadOnlyCollection<SchedulingLockedAssignmentContract> LockedAssignments);

public sealed record SchedulePlanContract(
    int ContractVersion,
    string PlanId,
    string ProblemId,
    string ProblemFingerprint,
    string AlgorithmVersion,
    SchedulePlanStatusContract Status,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyCollection<ScheduleAssignmentContract> Assignments,
    IReadOnlyCollection<ScheduleResourceLoadContract> ResourceLoads,
    IReadOnlyCollection<ScheduleConflictContract> Conflicts,
    IReadOnlyCollection<UnscheduledOperationContract> UnscheduledOperations,
    IReadOnlyCollection<ScheduleChangeContract> ChangeSummary,
    IReadOnlyCollection<GanttScheduleItemContract> GanttItems);
```

Add the supporting records for orders, operations, resources, calendars, unavailability windows, assignments, loads, conflicts, unscheduled operations, changes and Gantt items. Keep them immutable records with only primitive/string/decimal/date-time members.

- [ ] **Step 5: Run contract tests and verify GREEN**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj --no-restore
```

Expected: PASS.

## Task 2: Scaffold BusinessScheduling Service

- [ ] **Step 1: Create service projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Scheduling -o backend/services/Business/Scheduling --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Scheduling.Domain.Tests -o backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Scheduling.Web.Tests -o backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests --framework net10.0
```

Expected: Domain, Infrastructure, Web and test projects exist.

- [ ] **Step 2: Remove template demo code**

Delete template demo endpoints, sample aggregates, sample migrations, SignalR hubs and demo tests.

Run:

```powershell
Get-ChildItem -Recurse -File backend/services/Business/Scheduling | Select-String -Pattern 'OrderAggregate','DeliverRecord','LoginEndpoint','ChatHub','LockEndpoint' -SimpleMatch
```

Expected: no matches.

- [ ] **Step 3: Add service to solution**

Run:

```powershell
dotnet sln backend/Nerv.IIP.sln add backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/Nerv.IIP.Contracts.Scheduling.csproj
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/Nerv.IIP.Business.Scheduling.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Nerv.IIP.Business.Scheduling.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj
```

Expected: all Scheduling projects are part of `backend/Nerv.IIP.sln`.

## Task 3: Implement Pure Finite-Capacity Scheduler With TDD

- [ ] **Step 1: Write failing scheduler tests**

Create `FiniteCapacitySchedulerTests.cs` covering:

1. `Schedule_returns_identical_plan_for_repeated_shock_absorber_input`
2. `Schedule_preserves_operation_precedence`
3. `Schedule_avoids_maintenance_window`
4. `Schedule_places_rush_order_before_normal_order_on_shared_bottleneck`
5. `Schedule_reports_due_date_conflict_when_assignment_finishes_late`
6. `Schedule_preserves_locked_assignment_and_reserves_capacity`
7. `Schedule_returns_unscheduled_reason_when_no_resource_can_run_operation`

Use the fixture from the spec and assert exact UTC timestamps for at least the oil/seal bottleneck operations.

- [ ] **Step 2: Run scheduler tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore --filter FullyQualifiedName~FiniteCapacitySchedulerTests
```

Expected: FAIL because `FiniteCapacityScheduler` does not exist.

- [ ] **Step 3: Implement minimal pure scheduler**

Create `FiniteCapacityScheduler.cs` with a public method:

```csharp
public sealed class FiniteCapacityScheduler
{
    public SchedulePlanContract Schedule(SchedulingProblemContract problem, string planId, DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var state = SchedulerState.From(problem, planId, generatedAtUtc);
        state.ReserveLockedAssignments();
        state.ScheduleOpenOperations();
        return state.ToPlan();
    }
}
```

Implementation constraints:

1. Do not read clocks inside the algorithm; use `generatedAtUtc`.
2. Do not call database, HTTP clients or static local-time APIs.
3. Use the deterministic sort from ADR 0014.
4. Treat capacity as one operation per resource in P0 unless `CapacityUnits` is greater than 1.
5. Keep unscheduled operations in output with explicit reason code.

- [ ] **Step 4: Run scheduler tests and verify GREEN**

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore --filter FullyQualifiedName~FiniteCapacitySchedulerTests
```

Expected: PASS.

## Task 4: Add Domain Lifecycle, Persistence And Events

- [ ] **Step 1: Write failing aggregate and event tests**

Create tests proving:

1. Generated plan can be released once.
2. Released plan cannot be regenerated or mutated.
3. Plan stores `problemFingerprint`, `algorithmVersion` and status.
4. Event names are exactly `scheduling.SchedulePlanGenerated`, `scheduling.ScheduleConflictDetected` and `scheduling.SchedulePlanReleased`.

- [ ] **Step 2: Implement aggregate and event converters**

Implement `SchedulePlan` aggregate and integration event converters. Use `Guid.CreateVersion7()` for new IDs. Store public IDs separately from EF keys when existing service patterns do so.

- [ ] **Step 3: Configure schema and migration**

Configure schema `scheduling`, tables from the spec and migrations history:

```csharp
options.MigrationsHistoryTable("__EFMigrationsHistory", "scheduling");
```

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialSchedulingSchema --project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Nerv.IIP.Business.Scheduling.Infrastructure.csproj --startup-project backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj --output-dir Migrations
```

- [ ] **Step 4: Run focused domain/web tests**

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore --filter FullyQualifiedName~SchedulingIntegrationEventTests
```

Expected: both commands pass.

## Task 5: Add Service API And Contract Tests

- [ ] **Step 1: Write failing endpoint contract tests**

Create `SchedulingEndpointContractTests.cs` covering:

1. Route and operation ID for preview, create, list, detail, Gantt and release endpoints.
2. Every endpoint requires `InternalServiceAuthorizationPolicy`.
3. Permission metadata contains `business.scheduling.plans.read`, `business.scheduling.plans.manage` or `business.scheduling.plans.release`.
4. Preview returns a deterministic plan for the shock absorber fixture without persisting release state.
5. Create persists a generated plan and detail returns assignments/conflicts.
6. Release changes status to released and repeated release is idempotent for the same plan.

- [ ] **Step 2: Implement endpoints, commands and queries**

Place FastEndpoints in `Endpoints/Scheduling/SchedulingEndpoints.cs`. Do not map Minimal API routes in `Program.cs`. Keep request/response DTOs aligned with `Nerv.IIP.Contracts.Scheduling` and expose stable operation IDs.

- [ ] **Step 3: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore
```

Expected: Scheduling Web tests pass.

## Task 6: Register Shared Platform Surfaces

- [ ] **Step 1: Register AppHost and solution dependencies**

Add BusinessScheduling to `infra/aspire/Nerv.IIP.AppHost/Program.cs` using port `5120`, which is currently free between BusinessGateway `5119` and BusinessConsole `5125`. Follow the existing business service registration style and keep default Development messaging as InMemory.

- [ ] **Step 2: Update IAM seed and authorization docs**

Add permission codes:

```text
business.scheduling.plans.read
business.scheduling.plans.manage
business.scheduling.plans.release
```

Update `docs/architecture/authorization-matrix.md` and the IAM seed location used by other business permissions.

- [ ] **Step 3: Update schema catalog and readiness docs**

Add the `scheduling` schema and tables to `docs/architecture/database-schema-catalog.md`. Update `docs/architecture/implementation-readiness.md` with #206 status, service port, verification command and current limitations.

- [ ] **Step 4: Add verification script**

Create `scripts/verify-business-scheduling-aps-lite.ps1`. Dot-source `scripts/lib/ScriptAutomation.ps1`, declare script classification metadata, and use helper functions such as `Invoke-DotNet`; do not call native `dotnet` directly in script body.

## Task 7: Add BusinessGateway Facade For Gantt Consumers

- [ ] **Step 1: Add Scheduling client registration**

Modify `BusinessServiceClients.cs` to register a Scheduling HTTP client. Use the non-idempotent-safe resilience strategy for create/release calls, or split read/write clients if the local pattern already does that.

- [ ] **Step 2: Add facade endpoint tests**

Test that `/api/business-console/v1/scheduling/plans/preview`, `/plans`, `/plans/{planId}`, `/plans/{planId}/gantt` and `/plans/{planId}/release` enforce IAM permission checks and proxy the stable DTO without adding scheduling rules in Gateway.

- [ ] **Step 3: Implement facade endpoints**

Create `BusinessConsoleSchedulingEndpoints.cs`. It may translate page-level routes and forward bearer/context/internal service token, but must not persist scheduling facts or compute assignments.

- [ ] **Step 4: Run gateway tests**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

Expected: BusinessGateway tests pass.

## Task 8: Final Verification

- [ ] **Step 1: Run focused verification**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj --no-restore
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --no-restore
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

Expected: all commands pass.

- [ ] **Step 2: Run governed script checks**

Run:

```powershell
pwsh scripts/check-script-governance.ps1
pwsh scripts/verify-business-scheduling-aps-lite.ps1
```

Expected: both commands pass. If Docker-dependent checks are added later and Docker is unavailable, report the skip explicitly.

- [ ] **Step 3: Run backend build if focused tests pass**

Run:

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

Expected: build passes with no new warnings.
