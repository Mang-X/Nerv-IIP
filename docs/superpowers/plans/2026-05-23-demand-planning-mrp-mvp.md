# DemandPlanning MPS/MRP MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #128 by creating DemandPlanning as the fact source for demand sources, MPS, deterministic daily-bucket MRP runs, planned purchase/work-order suggestions and pegging.

**Architecture:** DemandPlanning is a CleanDDD business service under `backend/services/Business/DemandPlanning`. It consumes ProductEngineering and Inventory through public API/contract adapters and stores only planning snapshots. It does not create ERP purchase documents, MES work orders or Inventory movements.

**Tech Stack:** .NET 10, NetCorePal CleanDDD template, FastEndpoints, EF Core PostgreSQL, xUnit, ADR 0011 integration event conversion, `Nerv.IIP.Testing` schema convention helpers.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-demand-planning-mrp-mvp-design.md` as the domain contract for this plan.

## Files

- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/Nerv.IIP.Business.DemandPlanning.Domain.csproj`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Nerv.IIP.Business.DemandPlanning.Infrastructure.csproj`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/DemandSourceAggregate/DemandSource.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/MasterProductionScheduleAggregate/MasterProductionSchedule.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/MrpRunAggregate/MrpRun.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/PlanningSuggestionAggregate/PlanningSuggestion.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/DomainEvents/DemandPlanningDomainEvents.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Auth/DemandPlanningPermissionCodes.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/*.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Queries/*.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEvents/DemandPlanningIntegrationEvents.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEventConverters/DemandPlanningIntegrationEventConverters.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Endpoints/Planning/PlanningEndpoints.cs`
- Create: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/DemandPlanningAggregateTests.cs`
- Create: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/MrpCalculatorTests.cs`
- Create: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningEndpointContractTests.cs`
- Create: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningIntegrationEventTests.cs`
- Create: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningSchemaConventionTests.cs`

Shared files requested from WAVE2-INTEG:

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-demand-planning-mvp.ps1`
- `scripts/verify-business-wave2-execution.ps1`

## Task 1: Scaffold DemandPlanning Service Locally

- [ ] **Step 1: Create service projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.DemandPlanning -o backend/services/Business/DemandPlanning --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.DemandPlanning.Domain.Tests -o backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.DemandPlanning.Web.Tests -o backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests --framework net10.0
```

Expected: DemandPlanning Domain, Infrastructure, Web and test projects exist.

- [ ] **Step 2: Remove template demo code**

Remove template demo endpoints, sample aggregates, sample migrations, demo SignalR hubs and demo tests. Verify no file contains `OrderAggregate`, `DeliverRecord`, `LoginEndpoint`, `ChatHub` or `LockEndpoint`.

Run:

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/DemandPlanning
```

Expected: no matches.

## Task 2: Implement Planning Domain And MRP Calculator

- [ ] **Step 1: Write failing domain tests**

Create `DemandPlanningAggregateTests.cs` covering:

1. Demand source creation requires organization, environment, SKU, quantity and due date.
2. MRP run can move from created to running to completed with input snapshot metadata.
3. Planning suggestion can be accepted once and cannot be accepted after rejected or closed.
4. Pegging links preserve demand source and version references.

- [ ] **Step 2: Write deterministic MRP calculator tests**

Create `MrpCalculatorTests.cs` with the fixture from the spec:

1. Demand `SKU-FG-1000` quantity `10`, due `2026-06-01`.
2. Finished-good availability `2`.
3. MBOM line `SKU-FG-1000 -> SKU-RM-1000` quantity `3`.
4. Component availability `5`.
5. Expected work order suggestion `8`.
6. Expected purchase suggestion `19`.

- [ ] **Step 3: Implement aggregate roots and pure calculator**

Implement the aggregate files and `MrpCalculator`. Keep the calculator deterministic and free of database/service calls. Input adapters should prepare immutable records before invoking it.

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MrpCalculatorTests
```

Expected: domain and calculator tests pass.

## Task 3: Add Persistence And Events

- [ ] **Step 1: Configure DbContext and schema**

Use schema `demand_planning` and tables:

1. `demand_sources`
2. `master_production_schedules`
3. `mrp_runs`
4. `planning_suggestions`
5. `mrp_pegging_links`

Configure migrations history as `demand_planning.__EFMigrationsHistory`.

- [ ] **Step 2: Generate migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialDemandPlanningSchema --project backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Nerv.IIP.Business.DemandPlanning.Infrastructure.csproj --startup-project backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj --output-dir Migrations
```

- [ ] **Step 3: Add event converter tests**

Verify event names:

1. `demandPlanning.MrpRunCompleted`
2. `demandPlanning.PlannedPurchaseSuggested`
3. `demandPlanning.PlannedWorkOrderSuggested`
4. `demandPlanning.PlanningSuggestionAccepted`

Run:

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~DemandPlanningIntegrationEventTests
```

Expected: event converter tests pass.

## Task 4: Add API Surface

- [ ] **Step 1: Add endpoint contract tests**

Create `DemandPlanningEndpointContractTests.cs` covering:

1. All endpoints require the expected permission code.
2. Route shape and operation IDs are stable.
3. Demand source creation returns a demand source ID.
4. MRP run creates suggestions from fixture-backed ProductEngineering/Inventory adapters.
5. Pegging endpoint returns demand/source/version refs.
6. Suggestion acceptance is idempotent for the same downstream reference and rejects conflicting repeats.

- [ ] **Step 2: Implement commands, queries and FastEndpoints**

Implement endpoints from the spec. Keep ProductEngineering and Inventory access behind adapters that can be replaced by fixture implementations in tests.

- [ ] **Step 3: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore
```

Expected: DemandPlanning Web tests pass.

## Task 5: Handoff Shared Changes To WAVE2-INTEG

- [ ] **Step 1: Record shared changes**

In the PR/session summary, include:

```markdown
## Shared Changes Needed

- Add DemandPlanning projects/tests to `backend/Nerv.IIP.sln`.
- Register DemandPlanning in AppHost with a PostgreSQL database and InMemory messaging by default.
- Add DemandPlanning permissions to IAM seed and `authorization-matrix.md`.
- Add `demand_planning` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-demand-planning-mvp.ps1`.
```

- [ ] **Step 2: Run final focused verification**

Run:

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore
```

Expected: both commands pass.

