# Quality Inspection MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #132 by extending the existing Quality service with inspection plans, inspection records and inspection result events while preserving current NCR behavior.

**Architecture:** This is a delta plan for an existing service. Quality keeps ownership of NCR and adds InspectionPlan and InspectionRecord aggregates in the same `quality` schema. Quality emits inspection result events and may open NCRs from failed records, but it never directly mutates Inventory, WMS, ERP or MES.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, EF Core PostgreSQL, xUnit, CAP-style integration event conversion, `Nerv.IIP.Testing` schema convention helpers.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-quality-inspection-mvp-design.md` as the domain contract for this plan.

## Current Code Facts

Existing Quality files include:

1. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/NonconformanceReportAggregate/NonconformanceReport.cs`
2. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs`
3. `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/NonconformanceReports/NonconformanceReportEndpoints.cs`
4. `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/NonconformanceReportAggregateTests.cs`
5. `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityEndpointContractTests.cs`

Do not run `dotnet new` for Quality.

## Files

- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/QualityFacts.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionPlanAggregate/InspectionPlan.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/NonconformanceReportAggregate/NonconformanceReport.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionPlanEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/EntityConfigurations/InspectionRecordEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Auth/BusinessPermissionCodes.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionPlans/CreateInspectionPlanCommand.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionPlans/ActivateInspectionPlanCommand.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionRecords/CreateInspectionRecordCommand.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Commands/InspectionRecords/OpenNcrFromInspectionCommand.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/InspectionPlans/ListInspectionPlansQuery.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Queries/InspectionRecords/ListInspectionRecordsQuery.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/InspectionPlans/InspectionPlanEndpoints.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/InspectionRecords/InspectionRecordEndpoints.cs`
- Create: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/InspectionAggregateTests.cs`
- Create: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionEndpointContractTests.cs`
- Create: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionIntegrationEventTests.cs`
- Modify: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityEndpointContractTests.cs`

Shared files requested from #140:

- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-quality-inspection-mvp.ps1`

## Task 1: Baseline Current Quality

- [ ] **Step 1: Read current NCR behavior**

Read:

```powershell
Get-Content backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/NonconformanceReportAggregate/NonconformanceReport.cs
Get-Content backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Endpoints/NonconformanceReports/NonconformanceReportEndpoints.cs
Get-Content backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityEndpointContractTests.cs
```

Expected: existing NCR behavior is understood and preserved.

- [ ] **Step 2: Run focused baseline tests**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
```

Expected: tests pass before changes. If they fail, record failing tests in the PR.

## Task 2: Add Inspection Domain Model

- [ ] **Step 1: Write aggregate tests**

Create `InspectionAggregateTests.cs` for:

1. Draft inspection plan can add characteristics.
2. Activated inspection plan cannot change execution characteristics.
3. New plan version supersedes previous plan.
4. Inspection record passes when all required characteristics pass.
5. Inspection record rejects when a required characteristic fails.
6. Failed inspection can open an NCR linked to the inspection record.

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~InspectionAggregateTests
```

Expected before implementation: compile failure because inspection aggregates do not exist.

- [ ] **Step 2: Implement InspectionPlan and InspectionRecord**

Implement `InspectionPlan.cs` and `InspectionRecord.cs` with public references for source document, SKU, partner, work center and file attachment IDs. Use `Guid.CreateVersion7()` for entity IDs.

- [ ] **Step 3: Add NCR link behavior**

Extend `NonconformanceReport` only as needed to link an NCR to an inspection record ID and source reference. Do not change existing NCR state transitions unless a failing regression test proves a bug.

- [ ] **Step 4: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
```

Expected: all Quality domain tests pass.

## Task 3: Add Persistence And Events

- [ ] **Step 1: Configure EF mappings**

Add DbSets and entity configurations for inspection plans and records in schema `quality`. Keep the existing `quality.__EFMigrationsHistory` configuration.

- [ ] **Step 2: Generate migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddQualityInspectionFacts --project backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Nerv.IIP.Business.Quality.Infrastructure.csproj --startup-project backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Nerv.IIP.Business.Quality.Web.csproj --output-dir Migrations
```

Expected: migration adds inspection tables without deleting or recreating NCR tables.

- [ ] **Step 3: Add event converter tests**

Create `QualityInspectionIntegrationEventTests.cs` and verify:

1. `quality.InspectionPassed`
2. `quality.InspectionRejected`
3. Existing NCR event names still pass.

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore --filter FullyQualifiedName~QualityInspectionIntegrationEventTests
```

Expected: event converter tests pass.

## Task 4: Add API Surface

- [ ] **Step 1: Add endpoint contract tests**

Create `QualityInspectionEndpointContractTests.cs` for:

1. Inspection endpoints require internal service authorization.
2. `POST /api/quality/v1/inspection-plans` creates a plan.
3. `POST /api/quality/v1/inspection-plans/{inspectionPlanId}/activate` activates a plan.
4. `POST /api/quality/v1/inspection-records` records pass and reject outcomes.
5. `POST /api/quality/v1/inspection-records/{inspectionRecordId}/failures/ncr` opens an NCR.
6. OpenAPI operation IDs are stable.
7. Existing NCR endpoint tests still pass.

- [ ] **Step 2: Implement commands, queries and FastEndpoints**

Implement the command, query and endpoint files listed in the Files section. Use the permissions from the Quality inspection spec.

- [ ] **Step 3: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
```

Expected: all Quality Web tests pass.

## Task 5: Handoff Shared Changes To #140

- [ ] **Step 1: Record shared changes**

In the PR body for this session, include:

```markdown
## Shared Changes Needed

- Add Quality inspection permissions to IAM seed and `authorization-matrix.md`.
- Add new inspection tables to `database-schema-catalog.md`.
- Add or refresh `scripts/verify-business-quality-inspection-mvp.ps1`.
- Update readiness to say Quality inspection is complete after focused tests pass.
```

- [ ] **Step 2: Run final focused verification**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
```

Expected: both commands pass.
