# Product Engineering Gap Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete #127 by extending the existing ProductEngineering service beyond ProductionVersion into engineering documents, engineering items, EBOM, MBOM, routing and ECO/ECN release facts.

**Architecture:** This is a delta plan, not a scaffold plan. Preserve the existing ProductEngineering service, migration baseline and ProductionVersion APIs, then add missing aggregates and release APIs around them. ProductEngineering owns released engineering facts and emits release events; it references MasterData and FileStorage by public IDs only.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, EF Core PostgreSQL, xUnit, CAP-style integration event conversion, `Nerv.IIP.Testing` schema convention helpers.

---

## Current Code Facts

Existing files include:

1. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs`
2. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/ApplicationDbContext.cs`
3. `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/ProductionVersions/ProductionVersionEndpoints.cs`
4. `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductionVersionAggregateTests.cs`
5. `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductionVersionApiContractTests.cs`

Do not run `dotnet new` for this service.

## Files

- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/ProductEngineeringFacts.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringDocumentAggregate/EngineeringDocument.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringItemAggregate/EngineeringItem.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/RoutingAggregate/Routing.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringChangeAggregate/EngineeringChange.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/EngineeringDocumentEntityTypeConfiguration.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/EngineeringItemEntityTypeConfiguration.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/EngineeringBomEntityTypeConfiguration.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/ManufacturingBomEntityTypeConfiguration.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/RoutingEntityTypeConfiguration.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/EngineeringChangeEntityTypeConfiguration.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEvents/ProductEngineeringIntegrationEvents.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEventConverters/ProductEngineeringIntegrationEventConverters.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/EngineeringDocuments/EngineeringDocumentEndpoints.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/EngineeringBoms/EngineeringBomEndpoints.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/ManufacturingBoms/ManufacturingBomEndpoints.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/Routings/RoutingEndpoints.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/EngineeringChanges/EngineeringChangeEndpoints.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringReleaseAggregateTests.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringIntegrationEventTests.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringSchemaConventionTests.cs`

Shared files requested from #140:

- `backend/Nerv.IIP.sln`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-product-engineering-mvp.ps1`

## Task 1: Baseline Current ProductEngineering

- [ ] **Step 1: Read current aggregate and endpoint facts**

Read:

```powershell
Get-Content backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs
Get-Content backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/ProductionVersions/ProductionVersionEndpoints.cs
Get-Content backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductionVersionApiContractTests.cs
```

Expected: current ProductionVersion behavior is understood before adding new release facts.

- [ ] **Step 2: Run focused baseline tests**

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
```

Expected: tests pass before changes. If they fail, record the failing test names in the PR before implementing.

## Task 2: Add Engineering Release Domain Model

- [ ] **Step 1: Add failing domain tests**

Add tests in `ProductEngineeringReleaseAggregateTests.cs` for:

1. EngineeringDocument registers a FileStorage file reference and rejects blank `fileId`.
2. EngineeringItem creates a released item reference for one SKU code and revision.
3. EngineeringBom release makes its components immutable.
4. ManufacturingBom release references a released EngineeringBom and process recipe/formula lines.
5. Routing release creates ordered operation steps with work center references.
6. EngineeringChange release references affected documents, EBOM, MBOM, routing or ProductionVersion IDs.
7. ProductionVersion cannot bind an unpublished MBOM or routing.

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ProductEngineeringReleaseAggregateTests
```

Expected before implementation: compile failure because the new aggregate types do not exist.

- [ ] **Step 2: Implement aggregate roots and value objects**

Implement the aggregate files listed in the Files section. Use `Guid.CreateVersion7()` for new entity IDs, async repository patterns in Infrastructure repositories, and public MasterData/FileStorage IDs instead of cross-service object references.

Required aggregate behaviors:

1. `EngineeringDocument.Register(...)`
2. `EngineeringItem.CreateRevision(...)`
3. `EngineeringBom.Release(...)`
4. `ManufacturingBom.ReleaseFromEngineeringBom(...)`
5. `Routing.Release(...)`
6. `EngineeringChange.Release(...)`
7. `ProductionVersion.Create(...)` or equivalent must verify published MBOM and routing references by status fields available in ProductEngineering.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
```

Expected: all ProductEngineering domain tests pass.

## Task 3: Add Persistence And Events

- [ ] **Step 1: Add DbSets and entity configurations**

Update `ApplicationDbContext.cs` with DbSets for all new aggregate roots and add entity configurations with:

1. Schema `product_engineering`.
2. Table comments and column comments.
3. Required unique indexes for business keys such as document number/revision, item code/revision, BOM code/revision and routing code/revision.
4. Migrations history already configured to `product_engineering.__EFMigrationsHistory`.

- [ ] **Step 2: Generate migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add CompleteProductEngineeringReleaseFacts --project backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj --startup-project backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj --output-dir Migrations
```

Expected: a new migration is created under ProductEngineering Infrastructure migrations.

- [ ] **Step 3: Add event converter tests**

Create `ProductEngineeringIntegrationEventTests.cs` and verify these event names:

1. `productEngineering.BomReleased`
2. `productEngineering.RoutingReleased`
3. `productEngineering.ProductionVersionCreated`
4. `productEngineering.EngineeringChangeReleased`

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ProductEngineeringIntegrationEventTests
```

Expected: event converter tests pass.

## Task 4: Add API Surface

- [ ] **Step 1: Add endpoint contract tests**

Create `ProductEngineeringReleaseApiContractTests.cs` and assert:

1. New endpoints require internal service authorization.
2. Operation IDs are stable and use ProductEngineering names.
3. Release commands reject blank organization, environment, code, revision and file IDs.
4. Resolve ProductionVersion still returns current behavior for existing tests.

- [ ] **Step 2: Implement endpoints and commands**

Add FastEndpoints under the endpoint folders listed in the Files section. Required endpoints:

1. `POST /api/product-engineering/v1/documents`
2. `POST /api/product-engineering/v1/engineering-boms/release`
3. `POST /api/product-engineering/v1/manufacturing-boms/release`
4. `POST /api/product-engineering/v1/routings/release`
5. `POST /api/product-engineering/v1/engineering-changes/release`
6. `GET /api/product-engineering/v1/engineering-boms`
7. `GET /api/product-engineering/v1/routings`

- [ ] **Step 3: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
```

Expected: all ProductEngineering Web tests pass.

## Task 5: Handoff Shared Changes To #140

- [ ] **Step 1: Create PR summary section**

In the PR body for this session, include:

```markdown
## Shared Changes Needed

- Add ProductEngineering projects/tests to `backend/Nerv.IIP.sln` if missing.
- Register ProductEngineering in AppHost after Web project compiles.
- Add ProductEngineering permissions to IAM seed and `authorization-matrix.md`.
- Add ProductEngineering schema entries to `database-schema-catalog.md`.
- Add or refresh `scripts/verify-business-product-engineering-mvp.ps1`.
```

- [ ] **Step 2: Run final focused verification**

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
```

Expected: both commands pass.
