# Business Master Data Realignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Realign BusinessMasterData so it can serve as a governed foundation for discrete and process manufacturing before completing the public API and downstream service rollout.

**Architecture:** Keep BusinessMasterData as the Layer 0 owner of common business identity, UOM, resource and static reference facts. Keep versioned engineering facts in ProductEngineering, inventory facts in Inventory, quality workflow facts in Quality, execution facts in MES and runtime industrial facts in IndustrialTelemetry. Add downstream resolve contracts and MasterData change events so other services can depend on MasterData without direct database coupling.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal repository/unit-of-work primitives, xUnit, ADR 0011 IntegrationEvent envelope.

---

## Source Inputs

1. `docs/adr/0012-business-platform-domain-layering.md`
2. `docs/adr/0013-business-master-data-governance.md`
3. `docs/architecture/business-platform-domain-architecture.md`
4. `docs/architecture/business-master-data-field-matrix.md`
5. `docs/architecture/business-master-data-process-manufacturing-supplement.md`
6. `docs/superpowers/specs/2026-05-20-business-platform-domain-design.md`
7. `docs/superpowers/plans/2026-05-20-business-master-data-foundation.md`

## Boundaries

1. Do not move EBOM, MBOM, Recipe, Formula, Routing, ECO or ECN into MasterData.
2. Do not move stock balance, stock movement, actual lot, serial, heat, expiry or inventory status into MasterData.
3. Do not move inspection standards, inspection records, COA, quality release decisions or nonconformance workflow into MasterData.
4. Do not move MES batch records, actual consumption, actual output, deviations, cleaning execution or genealogy into MasterData.
5. Do not store PLC/DCS/SCADA connection secrets, telemetry samples, alarms or device state snapshots in MasterData.
6. Do not duplicate IAM user, role, permission or membership facts.

## Task 1: Expand MasterData Domain Model

**Files:**

- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/SkuAggregate/Sku.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/BusinessPartnerAggregate/BusinessPartner.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCenterAggregate/WorkCenter.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCalendarAggregate/WorkCalendar.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/DeviceAssetAggregate/DeviceAsset.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/UnitOfMeasureAggregate/UnitOfMeasure.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/UomConversionAggregate/UomConversion.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/SiteAggregate/Site.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ProductionLineAggregate/ProductionLine.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ShiftAggregate/Shift.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/ReferenceDataAggregate/ReferenceDataCode.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/DomainEvents/MasterDataDomainEvents.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/MasterDataAggregateTests.cs`

- [ ] **Step 1: Write failing aggregate tests**

Add tests that cover:

1. UOM requires code, dimension type, precision and rounding mode.
2. UOM conversion rejects non-positive factors and same-unit conversion.
3. SKU requires base UOM and traceability policy.
4. Process-material SKU can hold storage condition, shelf-life policy, hazard/allergen tags and quality-required flag.
5. WorkCenter can reference plant, line, resource type, capacity unit and default calendar.
6. DeviceAsset can hold asset class, manufacturer, serial number, static capacity range, capacity UOM, criticality, maintainable flag and external references without control secrets.
7. Shift supports cross-midnight working time.
8. ReferenceDataCode is unique by code set and code.

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
```

Expected: FAIL because the new aggregates and properties do not exist.

- [ ] **Step 2: Implement minimal domain changes**

Implement only the fields and invariants listed in Step 1. Use string codes for cross-aggregate references and keep IAM references as public IDs. Add domain events for created/changed/disabled facts:

```csharp
SkuChangedDomainEvent
SkuDisabledDomainEvent
UnitOfMeasureChangedDomainEvent
BusinessPartnerChangedDomainEvent
ResourceChangedDomainEvent
WorkCalendarChangedDomainEvent
DeviceAssetChangedDomainEvent
ReferenceDataCodeChangedDomainEvent
```

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit domain realignment**

Run:

```powershell
git add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests
git commit -m "feat: realign business master data domain"
```

## Task 2: Update Persistence, Migration and Schema Catalog

**Files:**

- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/UnitOfMeasureEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/UomConversionEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/SiteEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/ProductionLineEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/ShiftEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/ReferenceDataCodeEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataSchemaConventionTests.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataPostgresProfileTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Expand schema tests**

Extend schema convention tests to assert new tables use schema `business_masterdata`, table comments, column comments, string ID conventions and migrations history schema.

- [ ] **Step 2: Configure tables and indexes**

Use these unique keys:

| Table | Unique key |
| --- | --- |
| `units_of_measure` | organizationId + environmentId + code |
| `uom_conversions` | organizationId + environmentId + fromUomCode + toUomCode + effectiveFrom |
| `sites` | organizationId + environmentId + code |
| `production_lines` | organizationId + environmentId + code |
| `shifts` | organizationId + environmentId + code |
| `reference_data_codes` | organizationId + environmentId + codeSet + code |

Add comments for every business property and include unit meaning where relevant.

- [ ] **Step 3: Generate migration**

Run:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add RealignBusinessMasterData --project backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Nerv.IIP.Business.MasterData.Infrastructure.csproj --startup-project backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj --output-dir Migrations
```

Expected: migration creates only `business_masterdata` schema objects and does not change IAM or other business service schemas.

- [ ] **Step 4: Run persistence tests**

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MasterDataSchemaConventionTests|FullyQualifiedName~MasterDataPostgresProfileTests"
```

Expected: schema convention tests pass; PostgreSQL profile tests pass when `NERV_IIP_TEST_POSTGRES` is configured or skip explicitly when not configured.

- [ ] **Step 5: Commit persistence realignment**

Run:

```powershell
git add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests docs/architecture/database-schema-catalog.md
git commit -m "feat: persist realigned business master data"
```

## Task 3: Add Resolve Contracts and Integration Events

**Files:**

- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEvents/MasterDataIntegrationEvents.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEventConverters/MasterDataIntegrationEventConverters.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ResolveMasterDataReferencesQuery.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ValidateMasterDataReferencesQuery.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataIntegrationEventTests.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataEndpointTests.cs`
- Modify: `docs/architecture/business-platform-domain-architecture.md`

- [ ] **Step 1: Define event payloads**

Add stable records for:

```csharp
SkuChangedIntegrationEvent
SkuDisabledIntegrationEvent
UnitOfMeasureChangedIntegrationEvent
BusinessPartnerChangedIntegrationEvent
ResourceChangedIntegrationEvent
WorkCalendarChangedIntegrationEvent
DeviceAssetChangedIntegrationEvent
ReferenceDataCodeChangedIntegrationEvent
```

Events must contain organizationId, environmentId, stable code, current status and occurred business timestamp. They must not contain tokens, secrets, full attachments or PLC control data.

- [ ] **Step 2: Add serialization tests**

Assert event JSON uses camelCase property names and remains ADR 0011 envelope-compatible.

- [ ] **Step 3: Add resolve query contracts**

Add resolve and validate queries that accept organizationId, environmentId and a collection of `{ resourceType, code }` references. Return:

```text
resourceType, code, exists, active, displayName, snapshotVersion, disabledReason
```

- [ ] **Step 4: Update architecture event baseline**

Add MasterData change events to `docs/architecture/business-platform-domain-architecture.md`.

- [ ] **Step 5: Run event and query tests**

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MasterDataIntegrationEventTests|FullyQualifiedName~MasterDataEndpointTests"
```

Expected: PASS.

- [ ] **Step 6: Commit contracts**

Run:

```powershell
git add backend/services/Business/MasterData docs/architecture/business-platform-domain-architecture.md
git commit -m "feat: add master data resolve contracts"
```

## Task 4: Complete API Surface and IAM Permissions

**Files:**

- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Auth/BusinessPermissionCodes.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/*.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/*.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/MasterData/MasterDataEndpoints.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataEndpointTests.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataOpenApiTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Modify: `docs/architecture/authorization-matrix.md`

- [ ] **Step 1: Update endpoint tests**

Cover anonymous `401`, missing permission `403`, successful create, duplicate key and resolve/validate behavior for SKU, UOM, partner, department, team, personnel skill, work center, calendar, shift, site, production line, device asset and reference data.

- [ ] **Step 2: Implement endpoints**

Expose create/list/resolve APIs using FastEndpoints. Keep operation IDs stable and require the permission codes documented in `authorization-matrix.md`.

- [ ] **Step 3: Seed permissions**

Add new MasterData permissions to IAM seed and seeded admin role. Keep existing permission strings unchanged when their meaning remains compatible.

- [ ] **Step 4: Run API and IAM tests**

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

Expected: PASS.

- [ ] **Step 5: Commit API realignment**

Run:

```powershell
git add backend/services/Business/MasterData backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/authorization-matrix.md
git commit -m "feat: expose realigned master data api"
```

## Task 5: Update Downstream Plans and Readiness

**Files:**

- Modify: `docs/superpowers/plans/2026-05-20-business-product-engineering-mvp.md`
- Modify: `docs/superpowers/plans/2026-05-20-business-common-capability-foundation.md`
- Modify: `docs/superpowers/plans/2026-05-20-business-demand-planning-mvp.md`
- Modify: `docs/superpowers/plans/2026-05-20-business-mes-execution-mvp.md`
- Modify: `docs/superpowers/plans/2026-05-20-business-master-data-foundation.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Update ProductEngineering plan**

Add Recipe/Formula and ProcessParameter as process-manufacturing versioned engineering facts owned by ProductEngineering.

- [ ] **Step 2: Update Inventory/Quality/MES plan notes**

Document that these services consume MasterData UOM, SKU traceability policy, resource hierarchy and characteristic definitions but own actual transactional facts.

- [ ] **Step 3: Update readiness docs**

Document that MasterData realignment is the gate before downstream services can treat BusinessMasterData as a stable dependency.

- [ ] **Step 4: Run documentation verification**

Run:

```powershell
rg -n "MasterData realignment|BusinessMasterData Process|Recipe|Formula|UnitOfMeasure|UomConversion" docs README.md
git diff --check
```

Expected: commands exit `0`.

- [ ] **Step 5: Commit readiness updates**

Run:

```powershell
git add docs README.md
git commit -m "docs: record master data realignment readiness"
```

## Self-Review Checklist

1. Every object in `business-master-data-field-matrix.md` has either a MasterData implementation task or a documented non-MasterData owner.
2. Process manufacturing requirements are represented without moving recipe/formula versions into MasterData.
3. API contracts include create/list plus batch resolve and validate operations.
4. IntegrationEvents cover changes that downstream services may cache.
5. Downstream plans do not create parallel SKU, UOM, partner, resource or device master facts.
