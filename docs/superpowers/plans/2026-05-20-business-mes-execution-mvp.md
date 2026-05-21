# Business MES Execution MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build MES MVP covering work orders, operation tasks, rule scheduling, reporting, finished goods receipt requests and production day reports.

**Architecture:** MES owns manufacturing execution facts and references released MBOM/routing versions from ProductEngineering. It accepts planned work order suggestions from DemandPlanning but does not own MRP calculation. Finished goods receipt is requested from WMS; inventory balance remains in Inventory.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal integration events, xUnit.

---

## MasterData Realignment Dependency

Before executing this plan, complete `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`. MES must resolve SKU, UOM, work center, work calendar, device asset, team and personnel skill references through MasterData contracts. For process manufacturing, MES owns batch execution, actual consumption/output, batch records, deviations, cleaning execution and genealogy; it does not own recipe/formula versions or static material/resource master facts.

## Boundaries

1. No APS optimizer; scheduling is deterministic rule scheduling.
2. No direct inventory balance writes.
3. No direct maintenance fact mutation; MES consumes availability events.
4. Work orders require released MBOM and routing references.
5. Process batch records, actual process values and deviations are MES execution facts; reusable material attributes, UOM and static resource capability remain MasterData facts.

## File Structure Map

```text
backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/
  AggregatesModel/WorkOrderAggregate/WorkOrder.cs
  AggregatesModel/OperationTaskAggregate/OperationTask.cs
  AggregatesModel/ProductionReportAggregate/ProductionReport.cs
  AggregatesModel/ScheduleResultAggregate/ScheduleResult.cs
  AggregatesModel/FinishedGoodsReceiptRequestAggregate/FinishedGoodsReceiptRequest.cs

backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/
  Application/Scheduling/RuleScheduler.cs
  Application/Commands/CreateWorkOrderFromSuggestionCommand.cs
  Application/Commands/ReleaseWorkOrderCommand.cs
  Application/Commands/ReportOperationCommand.cs
  Application/Commands/CreateFinishedGoodsReceiptRequestCommand.cs
  Application/Queries/GetScheduleGanttQuery.cs
  Application/IntegrationEvents/MesIntegrationEvents.cs
  Endpoints/Mes/MesEndpoints.cs
```

## Task 1: Scaffold MES Service

**Files:**

- Create: `backend/services/Business/Mes/*`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create service and tests**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Mes -o backend/services/Business/Mes --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Mes.Domain.Tests -o backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Mes.Web.Tests -o backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/Nerv.IIP.Business.Mes.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Nerv.IIP.Business.Mes.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Nerv.IIP.Business.Mes.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj
```

- [ ] **Step 2: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Mes
git commit -m "feat: scaffold mes service"
```

## Task 2: Implement Work Order and Reporting Model

**Files:**

- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/WorkOrderAggregate/WorkOrder.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReport.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/MesAggregateTests.cs`

- [ ] **Step 1: Write failing tests**

Cover:

```csharp
var workOrder = WorkOrder.FromPlanningSuggestion("org-001", "env-dev", "suggestion-wo-001", "SKU-FG-1000", 8m, "mbom-A", "routing-A");
workOrder.Release("approval-chain-003");
var task = OperationTask.Create("org-001", "env-dev", workOrder.Id.Value, 10, "WC-CNC-01", 8m);
var report = task.Report(5m, 1m, "surface-defect", 120, "idem-report-001");
```

Assert release requires MBOM/routing references, good plus defect quantity cannot exceed remaining quantity, defect quantity requires a reason, and reporting requires an idempotency key.

- [ ] **Step 2: Implement events**

Create `WorkOrderReleasedDomainEvent`, `OperationReportedDomainEvent`, `FinishedGoodsReceiptRequestedDomainEvent` and `DowntimeRecordedDomainEvent`.

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj --no-restore
git add backend/services/Business/Mes
git commit -m "feat: add mes work order reporting model"
```

Expected: tests pass before commit.

## Task 3: Implement Rule Scheduler and Gantt Query

**Files:**

- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ScheduleResultAggregate/ScheduleResult.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Scheduling/RuleScheduler.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/RuleSchedulerTests.cs`

- [ ] **Step 1: Write failing scheduler tests**

Fixture:

| Operation | Work center | Duration |
| --- | --- | --- |
| 10 | WC-CNC-01 | 60 minutes |
| 20 | WC-ASSY-01 | 45 minutes |

Calendar is 08:00 to 16:00 UTC on 2026-06-01. Expected schedule places operation 10 first and operation 20 after operation 10 completion, with no overlap inside the same work center.

- [ ] **Step 2: Implement scheduler**

`RuleScheduler` sorts by work order priority, due date, operation sequence and earliest available work center slot. It returns immutable `ScheduleResult` entries with start/end UTC timestamps and reason text `rule-sequenced`.

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore --filter FullyQualifiedName~RuleSchedulerTests
git add backend/services/Business/Mes
git commit -m "feat: add mes rule scheduling"
```

Expected: tests pass before commit.

## Task 4: Add Persistence, API, Events and Permissions

**Files:**

- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/*.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/*.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/IntegrationEvents/MesIntegrationEvents.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesEndpointTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Configure schema**

Use schema `mes`. Tables include `work_orders`, `operation_tasks`, `production_reports`, `schedule_results`, `finished_goods_receipt_requests`.

- [ ] **Step 2: Add routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/mes/work-orders/from-suggestion` | `business.mes.work-orders.manage` |
| `POST /api/business/v1/mes/work-orders/{workOrderId}/release` | `business.mes.work-orders.manage` |
| `GET /api/business/v1/mes/work-orders` | `business.mes.work-orders.read` |
| `POST /api/business/v1/mes/operation-tasks/{operationTaskId}/reports` | `business.mes.reporting.write` |
| `GET /api/business/v1/mes/reports` | `business.mes.reporting.read` |
| `POST /api/business/v1/mes/schedules/run` | `business.mes.schedules.manage` |
| `GET /api/business/v1/mes/schedules/gantt` | `business.mes.schedules.read` |

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
git add backend/services/Business/Mes backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md
git commit -m "feat: expose mes execution api"
```

Expected: tests pass before commit.

## Task 5: Add Verification and Readiness

**Files:**

- Create: `scripts/verify-business-mes-execution-mvp.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Add and run verification**

Run:

```powershell
scripts/verify-business-mes-execution-mvp.ps1
git diff --check
```

Expected: script runs MES domain/web tests and exits `0`.

- [ ] **Step 2: Commit docs**

Run:

```powershell
git add scripts/verify-business-mes-execution-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record mes execution readiness"
```

## Self-Review Checklist

1. Work orders reference released MBOM and routing versions.
2. Reporting is idempotent and rejects over-reporting.
3. Rule scheduling is deterministic and documented as the MVP boundary.
4. Finished goods receipt is a WMS request, not an Inventory balance write.
