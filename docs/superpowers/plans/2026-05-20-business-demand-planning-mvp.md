# Business Demand Planning MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build DemandPlanning lite with demand sources, MPS, MRP runs, planned purchase suggestions, planned work order suggestions and pegging.

**Architecture:** DemandPlanning consumes released engineering versions and inventory availability through APIs, contracts or imported snapshots. For planned work orders it resolves ProductEngineering ProductionVersion by SKU, due date and lot size instead of choosing naked MBOM/routing ids. It owns planning runs and suggestions, but it does not create formal purchase orders, formal work orders or stock movements. MRP starts as a deterministic daily-bucket calculation so the first slice can be tested without APS complexity.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal integration events, xUnit.

---

## MasterData Realignment Dependency

Before executing this plan, complete `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`. DemandPlanning must consume MasterData reference snapshots for SKU, UOM conversion, work center, work calendar, resource capability and device/resource availability baseline. Planning may add planning-specific defaults or parameters only after the MasterData field matrix decides they do not belong on SKU or resource master facts.

## Boundaries

1. No APS optimizer or constraint solver in this slice.
2. No direct writes to ERP, MES or Inventory tables.
3. MRP time bucket is daily for the MVP.
4. Suggestions are planning facts until ERP or MES accepts them.
5. Do not create parallel SKU, UOM, work center, calendar or device master facts in DemandPlanning.

## File Structure Map

```text
backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/
  DemandPlanningFacts.cs
  AggregatesModel/DemandSourceAggregate/DemandSource.cs
  AggregatesModel/MasterProductionScheduleAggregate/MasterProductionSchedule.cs
  AggregatesModel/MrpRunAggregate/MrpRun.cs
  AggregatesModel/PlanningSuggestionAggregate/PlanningSuggestion.cs
  DomainEvents/DemandPlanningDomainEvents.cs

backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/
  Application/Commands/CreateDemandSourceCommand.cs
  Application/Commands/RunMrpCommand.cs
  Application/Commands/AcceptPlanningSuggestionCommand.cs
  Application/Queries/ListMrpRunsQuery.cs
  Application/Queries/GetMrpPeggingQuery.cs
  Application/Queries/ListPlanningSuggestionsQuery.cs
  Application/Planning/MrpCalculator.cs
  Application/IntegrationEvents/DemandPlanningIntegrationEvents.cs
  Endpoints/Planning/PlanningEndpoints.cs
```

## Task 1: Scaffold DemandPlanning Service

**Files:**

- Create: `backend/services/Business/DemandPlanning/*`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create projects and tests**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.DemandPlanning -o backend/services/Business/DemandPlanning --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.DemandPlanning.Domain.Tests -o backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.DemandPlanning.Web.Tests -o backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/Nerv.IIP.Business.DemandPlanning.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Nerv.IIP.Business.DemandPlanning.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj
```

- [ ] **Step 2: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/DemandPlanning
git commit -m "feat: scaffold demand planning service"
```

## Task 2: Add Planning Domain Model and MRP Calculator

**Files:**

- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/DemandSourceAggregate/DemandSource.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/MasterProductionScheduleAggregate/MasterProductionSchedule.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/MrpRunAggregate/MrpRun.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/PlanningSuggestionAggregate/PlanningSuggestion.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs`
- Create: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/DemandPlanningAggregateTests.cs`
- Create: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/MrpCalculatorTests.cs`

- [ ] **Step 1: Write failing tests**

MRP calculator tests must use this fixture:

| Input | Value |
| --- | --- |
| Demand | SKU-FG-1000, quantity 10, due 2026-06-01 |
| On hand | SKU-FG-1000 quantity 2 |
| MBOM | SKU-FG-1000 requires SKU-RM-1000 quantity 3 |
| On hand material | SKU-RM-1000 quantity 5 |

Expected suggestions:

| Suggestion | Quantity |
| --- | --- |
| planned work order for SKU-FG-1000 | 8 |
| planned purchase for SKU-RM-1000 | 19 |

Assert pegging links both suggestions back to the demand source and input version references.

- [ ] **Step 2: Implement daily-bucket MRP**

`MrpCalculator` accepts immutable input records:

```csharp
public sealed record MrpDemandInput(string DemandSourceId, string SkuCode, decimal Quantity, DateOnly DueDate);
public sealed record MrpInventoryInput(string SkuCode, decimal AvailableQuantity);
public sealed record MrpBomInput(string ParentSkuCode, string ComponentSkuCode, decimal QuantityPerParent, string VersionId);
public sealed record MrpSuggestionResult(string SuggestionType, string SkuCode, decimal Quantity, DateOnly DueDate, string? ProductionVersionId, IReadOnlyCollection<string> PeggingRefs);
```

The calculator subtracts available finished goods first, explodes component demand from net production quantity, subtracts component inventory and returns positive net suggestions only.

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MrpCalculatorTests
git add backend/services/Business/DemandPlanning
git commit -m "feat: add deterministic demand planning model"
```

Expected: tests pass before commit.

## Task 3: Add Persistence, Events and API

**Files:**

- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEvents/DemandPlanningIntegrationEvents.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/*.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Queries/*.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Endpoints/Planning/PlanningEndpoints.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Configure schema**

Use schema `demand_planning` and tables `demand_sources`, `master_production_schedules`, `mrp_runs`, `planning_suggestions`, `mrp_pegging_links`.

- [ ] **Step 2: Implement integration events**

Create events:

```csharp
public sealed record MrpRunCompletedIntegrationEvent(string RunId, DateOnly HorizonStart, DateOnly HorizonEnd, int SuggestionCount);
public sealed record PlannedPurchaseSuggestedIntegrationEvent(string SuggestionId, string SkuCode, decimal Quantity, DateOnly DueDate, IReadOnlyCollection<string> PeggingRefs);
public sealed record PlannedWorkOrderSuggestedIntegrationEvent(string SuggestionId, string SkuCode, decimal Quantity, DateOnly DueDate, string ProductionVersionId, IReadOnlyCollection<string> VersionRefs);
```

- [ ] **Step 3: Add routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/planning/demands` | `business.planning.demands.manage` |
| `GET /api/business/v1/planning/demands` | `business.planning.demands.read` |
| `POST /api/business/v1/planning/mrp-runs` | `business.planning.mrp.run` |
| `GET /api/business/v1/planning/mrp-runs` | `business.planning.mrp.read` |
| `GET /api/business/v1/planning/mrp-runs/{runId}/pegging` | `business.planning.mrp.read` |
| `GET /api/business/v1/planning/suggestions` | `business.planning.mrp.read` |
| `POST /api/business/v1/planning/suggestions/{suggestionId}/accept` | `business.planning.suggestions.manage` |

- [ ] **Step 4: Seed permissions and run tests**

Run:

```powershell
dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

Expected: PASS.

- [ ] **Step 5: Commit API**

Run:

```powershell
git add backend/services/Business/DemandPlanning backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md
git commit -m "feat: expose demand planning api"
```

## Task 4: Add Verification and Readiness

**Files:**

- Create: `scripts/verify-business-demand-planning-mvp.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Add verification script**

The script runs all DemandPlanning tests and fails if any suggestion quantity differs from the deterministic fixture.

- [ ] **Step 2: Run final verification**

Run:

```powershell
scripts/verify-business-demand-planning-mvp.ps1
git diff --check
```

Expected: both commands exit `0`.

- [ ] **Step 3: Commit docs**

Run:

```powershell
git add scripts/verify-business-demand-planning-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record demand planning readiness"
```

## Self-Review Checklist

1. MRP suggestions are explainable through pegging.
2. DemandPlanning does not create formal ERP or MES documents.
3. The daily bucket rule is documented as the MVP calculation boundary.
4. Permissions and operation IDs are stable.
