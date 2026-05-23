# MES CleanDDD Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #135 by moving MES from Web-only in-memory scheduling state to CleanDDD Domain, Infrastructure and PostgreSQL persistence while preserving current scheduling, rush order and reschedule behavior.

**Architecture:** This is a migration plan for existing MES behavior. Keep current Web endpoints and tests as behavioral contracts, introduce Domain aggregates for durable work order and execution facts, then replace `MesPlanningStore` with a repository-backed application service. MES references ProductEngineering ProductionVersion, Inventory, WMS, Quality, Telemetry and Maintenance by public IDs or event payloads only.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, EF Core PostgreSQL, xUnit, `Nerv.IIP.Testing` schema convention helpers.

---

## Current Code Facts

Existing MES files include:

1. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Planning/MesPlanningStore.cs`
2. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Scheduling/RuleScheduler.cs`
3. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/CreateRushWorkOrderCommand.cs`
4. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Schedules/RescheduleCommand.cs`
5. `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
6. `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RuleSchedulerTests.cs`
7. `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RushWorkOrderCommandTests.cs`
8. `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RescheduleCommandTests.cs`

Do not rewrite the scheduler from scratch unless an existing test proves the behavior is wrong.

## Files

- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/Nerv.IIP.Business.Mes.Domain.csproj`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Nerv.IIP.Business.Mes.Infrastructure.csproj`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/WorkOrderAggregate/WorkOrder.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReport.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ScheduleAggregate/ScheduleResult.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/FinishedGoodsReceiptRequestAggregate/FinishedGoodsReceiptRequest.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/DomainEvents/MesDomainEvents.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/WorkOrderEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/OperationTaskEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/ProductionReportEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/ScheduleResultEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/FinishedGoodsReceiptRequestEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/MesPersistenceServiceCollectionExtensions.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Planning/MesPlanningStore.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/CreateRushWorkOrderCommand.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Schedules/RescheduleCommand.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/MesAggregateTests.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesPersistenceContractTests.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesSchemaConventionTests.cs`

Shared files requested from #140:

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-mes-execution-mvp.ps1`

## Task 1: Freeze Current Web Behavior

- [ ] **Step 1: Run current MES tests**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

Expected: current MES Web tests pass. If they fail, record the failures before changing behavior.

- [ ] **Step 2: Add persistence regression tests**

Create `MesPersistenceContractTests.cs` asserting:

1. Rush work order survives service scope recreation when persistence is enabled.
2. Reschedule uses persisted work order and schedule facts.
3. Maintenance asset unavailable event updates persisted scheduling constraints.

Expected before implementation: compile failure or failing tests because persistence types do not exist.

## Task 2: Add Domain Projects And Aggregates

- [ ] **Step 1: Create Domain and Infrastructure projects**

Run:

```powershell
dotnet new classlib -n Nerv.IIP.Business.Mes.Domain -o backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain --framework net10.0
dotnet new classlib -n Nerv.IIP.Business.Mes.Infrastructure -o backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Mes.Domain.Tests -o backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests --framework net10.0
```

Expected: projects exist. Add project references from Web to Domain and Infrastructure, and from Infrastructure to Domain.

- [ ] **Step 2: Write aggregate tests**

Create `MesAggregateTests.cs` for:

1. WorkOrder references one ProductEngineering `productionVersionId`.
2. WorkOrder release creates operation tasks from routing step snapshots.
3. Rule schedule result is deterministic for the same work orders and constraints.
4. ProductionReport records good quantity, scrap quantity and operation completion.
5. FinishedGoodsReceiptRequest references work order, SKU, quantity and UOM but does not post Inventory movement.

- [ ] **Step 3: Implement domain aggregates**

Implement aggregate files listed in the Files section. Use `Guid.CreateVersion7()` for new IDs. Keep scheduler calculations deterministic and side-effect free.

- [ ] **Step 4: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj --no-restore
```

Expected: MES domain tests pass.

## Task 3: Add PostgreSQL Persistence

- [ ] **Step 1: Implement DbContext and mappings**

Create `ApplicationDbContext.cs` with schema `mes` and `MigrationsHistoryTable("__EFMigrationsHistory", "mes")`. Add table and column comments for every persisted business field.

- [ ] **Step 2: Generate migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialMesExecutionSchema --project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Nerv.IIP.Business.Mes.Infrastructure.csproj --startup-project backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj --output-dir Migrations
```

Expected: initial MES migration is created.

- [ ] **Step 3: Add schema convention tests**

Create `MesSchemaConventionTests.cs` using the existing `Nerv.IIP.Testing` schema convention helper.

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MesSchemaConventionTests
```

Expected: schema convention tests pass.

## Task 4: Replace In-Memory Store With Repository-Backed Service

- [ ] **Step 1: Keep current API request and response contracts**

Do not change current MES route paths or response shapes unless an existing contract test is updated with a documented reason.

- [ ] **Step 2: Adapt command handlers**

Modify rush work order and reschedule commands to use the persisted work order, schedule and constraint facts. Keep `RuleScheduler` as the deterministic scheduling component.

- [ ] **Step 3: Run Web regression tests**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

Expected: existing MES Web tests plus new persistence tests pass.

## Task 5: Handoff Shared Changes To #140

- [ ] **Step 1: Record shared changes**

In the PR body for this session, include:

```markdown
## Shared Changes Needed

- Add MES Domain and Infrastructure projects/tests to `backend/Nerv.IIP.sln`.
- Register MES in AppHost after Web project compiles.
- Add MES schema entries to `database-schema-catalog.md`.
- Add or refresh `scripts/verify-business-mes-execution-mvp.ps1`.
- Update readiness to say MES has durable Domain/Infrastructure persistence after focused tests pass.
```

- [ ] **Step 2: Run final focused verification**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

Expected: both commands pass.
