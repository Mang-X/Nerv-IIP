# Business Product Engineering MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build ProductEngineering lite with engineering documents, engineering items, EBOM, MBOM, routing, ProductionVersion binding and ECO/ECN release flow.

**Architecture:** Create `backend/services/Business/ProductEngineering` as the PDM/PLM-lite owner. It stores file references from File Storage, versioned engineering facts and release events; it does not implement CAD design, inventory, formal work orders or MRP calculation. Published EBOM, MBOM and routing versions are immutable. ProductionVersion binds a released MBOM + Routing for one SKU/effective window/lot-size range and exposes a resolve API for Planning and MES.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal domain events/integration event converters, xUnit.

---

## MasterData Realignment Dependency

Before executing this plan, complete `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`. ProductEngineering must consume the realigned MasterData contracts for SKU, UOM, resource hierarchy, work center, device asset and reference data.

For process manufacturing, this plan must treat `Recipe` / `Formula` and `ProcessParameter` as first-class versioned engineering facts owned by ProductEngineering. MasterData owns reusable material attributes, UOM, resource capability and parameter definitions; ProductEngineering owns released product-specific recipe/formula/routing versions.

## Source Inputs

1. Business spec requirements `BP-ENG-001` through `BP-ENG-004`
2. Architecture chain `CAD/PDM/PLM -> EBOM/MBOM/Routing -> ECO/ECN -> MRP/MES`
3. Authorization matrix entries under `business.engineering.*`
4. ADR 0011 integration event envelope baseline
5. `docs/adr/0013-business-master-data-governance.md`
6. `docs/architecture/business-master-data-process-manufacturing-supplement.md`

## Boundaries

1. Do not parse CAD files or store object storage keys.
2. Do not create purchase orders, work orders, stock movements or MRP suggestions.
3. Do not auto-change in-flight MES work orders after an engineering change release.
4. Do not share ProductEngineering tables with MasterData.
5. Do not store reusable UOM, SKU material attributes, resource hierarchy or device capability facts in ProductEngineering; resolve them from MasterData.

## File Structure Map

```text
backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/
  ProductEngineeringFacts.cs
  AggregatesModel/EngineeringDocumentAggregate/EngineeringDocument.cs
  AggregatesModel/EngineeringItemAggregate/EngineeringItem.cs
  AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs
  AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs
  AggregatesModel/RoutingAggregate/Routing.cs
  AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs
  AggregatesModel/EngineeringChangeAggregate/EngineeringChange.cs
  DomainEvents/ProductEngineeringDomainEvents.cs

backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/
  ApplicationDbContext.cs
  EntityConfigurations/*.cs
  Repositories/*.cs
  Migrations/*

backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/
  Application/Auth/EngineeringPermissionCodes.cs
  Application/Commands/*.cs
  Application/Queries/*.cs
  Application/IntegrationEvents/*.cs
  Application/IntegrationEventConverters/*.cs
  Endpoints/Engineering/*.cs

backend/services/Business/ProductEngineering/tests/
  Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringAggregateTests.cs
  Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringEndpointTests.cs
  Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringIntegrationEventTests.cs
  Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringSchemaConventionTests.cs
```

## Task 1: Scaffold ProductEngineering Service

**Files:**

- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/Nerv.IIP.Business.ProductEngineering.Domain.csproj`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.ProductEngineering -o backend/services/Business/ProductEngineering --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.ProductEngineering.Domain.Tests -o backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.ProductEngineering.Web.Tests -o backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/Nerv.IIP.Business.ProductEngineering.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj
```

Expected: projects are added without references to Inventory, MES, WMS or ERP.

- [ ] **Step 2: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/ProductEngineering
git commit -m "feat: scaffold product engineering service"
```

## Task 2: Add Versioned Engineering Aggregates

**Files:**

- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/ProductEngineeringFacts.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringDocumentAggregate/EngineeringDocument.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringItemAggregate/EngineeringItem.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/RoutingAggregate/Routing.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ProductionVersionAggregate/ProductionVersion.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringChangeAggregate/EngineeringChange.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringAggregateTests.cs`

- [ ] **Step 1: Write failing tests for release immutability**

Create tests for these cases:

```csharp
EngineeringDocument.Register("org-001", "env-dev", "file-001", "cad-drawing", "A");
EngineeringItem.Create("org-001", "env-dev", "ENG-1000", "Pump Assembly");
EngineeringBom.CreateDraft("org-001", "env-dev", "ENG-1000", "A").AddLine("ENG-1001", 2m, "EA").Release(DateOnly.FromDateTime(DateTime.UtcNow));
ManufacturingBom.CreateDraft("org-001", "env-dev", "SKU-FG-1000", "A").AddLine("SKU-RM-1000", 1.5m, "KG").Release(DateOnly.FromDateTime(DateTime.UtcNow));
Routing.CreateDraft("org-001", "env-dev", "SKU-FG-1000", "A").AddOperation(10, "WC-CNC-01", 30).Release(DateOnly.FromDateTime(DateTime.UtcNow));
ProductionVersion.Create("org-001", "env-dev", "SKU-FG-1000", "mbom-A", "routing-A", DateOnly.FromDateTime(DateTime.UtcNow), null, null, null, 10, true, EngineeringVersionStatus.Published, EngineeringVersionStatus.Published);
EngineeringChange.Open("org-001", "env-dev", "ECO-0001", "release mbom A").Approve("approval-chain-001").Release();
```

Assert that released EBOM, MBOM and Routing reject `AddLine`, `AddOperation` and `Rename` calls with `InvalidOperationException`.

Expected initial result: FAIL because aggregates do not exist.

- [ ] **Step 2: Implement aggregate rules**

Implement these invariants:

| Aggregate | Invariant |
| --- | --- |
| EngineeringDocument | `fileId + version` is the idempotency key; only `fileId` is stored, not object storage key. |
| EngineeringItem | lifecycle is `draft`, `released`, `archived`; released item cannot be renamed directly. |
| EngineeringBom | child lines cannot repeat in the same version; released version is immutable. |
| ManufacturingBom | all lines reference SKU codes; released version is immutable. |
| Routing | operation sequence is unique and positive; work center code is required. |
| ProductionVersion | binds only published MBOM/Routing, rejects invalid effective/lot windows, and archived versions cannot resolve for new work orders. |
| EngineeringChange | release requires approval reference and affected version list. |

Create domain events named `EngineeringDocumentRegisteredDomainEvent`, `EngineeringBomReleasedDomainEvent`, `ManufacturingBomReleasedDomainEvent`, `RoutingReleasedDomainEvent` and `EngineeringChangeReleasedDomainEvent`.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit domain**

Run:

```powershell
git add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests
git commit -m "feat: add product engineering versioned aggregates"
```

## Task 3: Add Persistence and Integration Events

**Files:**

- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEvents/ProductEngineeringIntegrationEvents.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/IntegrationEventConverters/ProductEngineeringIntegrationEventConverters.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringIntegrationEventTests.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringSchemaConventionTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Define stable event contracts**

Create these records:

```csharp
public sealed record BomReleasedIntegrationEvent(string BomVersionId, string BomType, string ItemOrSkuCode, IReadOnlyCollection<BomReleasedLine> Lines, DateOnly EffectiveDate);
public sealed record RoutingReleasedIntegrationEvent(string RoutingVersionId, string SkuCode, IReadOnlyCollection<RoutingReleasedOperation> Operations, DateOnly EffectiveDate);
public sealed record EngineeringChangeReleasedIntegrationEvent(string ChangeId, IReadOnlyCollection<string> AffectedVersionIds, DateOnly EffectiveDate);
```

Tests must serialize the records and assert property names remain camelCase.

- [ ] **Step 2: Add EF mapping**

Use schema `product_engineering` and these tables: `engineering_documents`, `engineering_items`, `engineering_boms`, `manufacturing_boms`, `routings`, `production_versions`, `engineering_changes`. Add comments for every business column and unique indexes for organization/environment plus code/version.

- [ ] **Step 3: Generate migration and update catalog**

Run:

```powershell
dotnet ef migrations add InitialProductEngineering --project backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj --startup-project backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj --output-dir Migrations
```

Expected: migration creates only `product_engineering` schema objects.

- [ ] **Step 4: Run persistence and event tests**

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~ProductEngineeringIntegrationEventTests|FullyQualifiedName~ProductEngineeringSchemaConventionTests"
```

Expected: PASS.

- [ ] **Step 5: Commit persistence and events**

Run:

```powershell
git add backend/services/Business/ProductEngineering docs/architecture/database-schema-catalog.md
git commit -m "feat: persist product engineering releases"
```

## Task 4: Add Engineering API Surface

**Files:**

- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Auth/EngineeringPermissionCodes.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/RegisterEngineeringDocumentCommand.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ReleaseEngineeringBomCommand.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ReleaseManufacturingBomCommand.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ReleaseRoutingCommand.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/CreateProductionVersionCommand.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ProductionVersions/ResolveProductionVersionQuery.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ReleaseEngineeringChangeCommand.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/ListEngineeringBomsQuery.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Queries/GetEngineeringChangeQuery.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/Engineering/EngineeringEndpoints.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringEndpointTests.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringOpenApiTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`

- [ ] **Step 1: Write endpoint tests**

Cover:

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/engineering/documents` | `business.engineering.documents.manage` |
| `GET /api/business/v1/engineering/documents` | `business.engineering.documents.read` |
| `POST /api/business/v1/engineering/eboms/{ebomId}/release` | `business.engineering.boms.manage` |
| `POST /api/business/v1/engineering/mboms/{mbomId}/release` | `business.engineering.boms.manage` |
| `POST /api/business/v1/engineering/routings/{routingId}/release` | `business.engineering.boms.manage` |
| `GET /api/business/v1/engineering/eboms` | `business.engineering.boms.read` |
| `GET /api/business/v1/engineering/production-versions` | `business.engineering.production-versions.read` |
| `GET /api/business/v1/engineering/production-versions/resolve` | `business.engineering.production-versions.read` |
| `POST /api/business/v1/engineering/production-versions` | `business.engineering.production-versions.manage` |
| `PUT /api/business/v1/engineering/production-versions/{productionVersionId}` | `business.engineering.production-versions.manage` |
| `POST /api/business/v1/engineering/production-versions/{productionVersionId}/archive` | `business.engineering.production-versions.manage` |
| `POST /api/business/v1/engineering/changes/{changeId}/release` | `business.engineering.changes.manage` |
| `GET /api/business/v1/engineering/changes/{changeId}` | `business.engineering.changes.read` |

Tests must assert released versions cannot be changed through the API.

- [ ] **Step 2: Implement permission constants and IAM seed**

Use only these constants:

```csharp
business.engineering.documents.read
business.engineering.documents.manage
business.engineering.boms.read
business.engineering.boms.manage
business.engineering.changes.read
business.engineering.changes.manage
```

- [ ] **Step 3: Implement handlers**

Commands validate organization/environment scope, idempotency keys for file registration and version release, and File Storage reference shape. This slice validates `fileId`, `fileName`, `contentType` and `version` fields locally and rejects blank file references; it does not call File Storage because ProductEngineering must remain independently testable.

- [ ] **Step 4: Run API tests**

Run:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

Expected: PASS.

- [ ] **Step 5: Commit API**

Run:

```powershell
git add backend/services/Business/ProductEngineering backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs
git commit -m "feat: expose product engineering api"
```

## Task 5: Add Verification and Readiness Notes

**Files:**

- Create: `scripts/verify-business-product-engineering-mvp.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Add verification script**

Run inside the script:

```powershell
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj --no-restore
```

- [ ] **Step 2: Run final verification**

Run:

```powershell
scripts/verify-business-product-engineering-mvp.ps1
git diff --check
```

Expected: both commands exit `0`.

- [ ] **Step 3: Commit verification docs**

Run:

```powershell
git add scripts/verify-business-product-engineering-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record product engineering readiness"
```

## Self-Review Checklist

1. `BP-ENG-001` through `BP-ENG-004` are covered by tests and endpoints.
2. Published EBOM, MBOM, Routing and EngineeringChange facts are immutable; ProductionVersion only binds published MBOM/Routing and resolves active, non-archived versions for MES/MRP.
3. Events use ADR 0011 envelope-compatible payloads and contain no object storage keys.
4. ProductEngineering stores only file references and released engineering facts.
