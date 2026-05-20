# Business Master Data Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first Business Platform service with SKU, business partner, work center, calendar and device asset master data.

**Architecture:** Create `backend/services/Business/MasterData` as a CleanDDD/netcorepal three-project service. MasterData owns business master data only, references IAM organization/environment identifiers as strings, and never reads IAM tables. PostgreSQL persistence uses the `business_masterdata` schema, service-local migrations and schema convention tests.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal repository/unit-of-work primitives, xUnit, PostgreSQL profile tests.

---

## Source Inputs

1. `docs/adr/0012-business-platform-domain-layering.md`
2. `docs/architecture/business-platform-domain-architecture.md`
3. `docs/superpowers/specs/2026-05-20-business-platform-domain-design.md`
4. `docs/architecture/backend-cleanddd-netcorepal-guidelines.md`
5. `docs/architecture/database-schema-conventions.md`
6. `docs/architecture/authorization-matrix.md`

## Boundaries

1. Do not create ProductEngineering, Inventory, WMS, MES, ERP, Telemetry or Maintenance rules in this service.
2. Do not duplicate IAM users, roles, memberships or permissions.
3. Do not persist PLC/DCS/SCADA connection secrets on `DeviceAsset`.
4. Do not add business pages in the frontend in this plan.
5. Keep all database objects inside the `business_masterdata` schema.

## File Structure Map

```text
backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/
  MasterDataFacts.cs
  AggregatesModel/SkuAggregate/Sku.cs
  AggregatesModel/BusinessPartnerAggregate/BusinessPartner.cs
  AggregatesModel/DepartmentAggregate/Department.cs
  AggregatesModel/TeamAggregate/Team.cs
  AggregatesModel/PersonnelSkillAggregate/PersonnelSkill.cs
  AggregatesModel/WorkCenterAggregate/WorkCenter.cs
  AggregatesModel/WorkCalendarAggregate/WorkCalendar.cs
  AggregatesModel/DeviceAssetAggregate/DeviceAsset.cs
  DomainEvents/MasterDataDomainEvents.cs

backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/
  ApplicationDbContext.cs
  MasterDataPersistenceServiceCollectionExtensions.cs
  MasterDataDatabaseMigrationRunner.cs
  EntityConfigurations/*.cs
  Repositories/*.cs
  Migrations/*

backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/
  Program.cs
  Application/Auth/BusinessPermissionCodes.cs
  Application/Commands/*.cs
  Application/Queries/*.cs
  Endpoints/MasterData/*.cs
  Endpoints/Health/HealthEndpoint.cs
  Endpoints/ResponseDataEndpointResults.cs

backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/
  MasterDataAggregateTests.cs

backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/
  MasterDataEndpointTests.cs
  MasterDataOpenApiTests.cs
  MasterDataPostgresProfileTests.cs
  MasterDataSchemaConventionTests.cs

docs/architecture/database-schema-catalog.md
docs/architecture/implementation-readiness.md
README.md
```

## Task 1: Scaffold MasterData Service

**Files:**

- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/Nerv.IIP.Business.MasterData.Domain.csproj`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Nerv.IIP.Business.MasterData.Infrastructure.csproj`
- Create: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj`
- Create: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create the service from the approved template**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.MasterData -o backend/services/Business/MasterData --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/Nerv.IIP.Business.MasterData.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Nerv.IIP.Business.MasterData.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj
```

Expected: commands exit `0`; generated projects target `net10.0`; no service references `backend/services/Iam`.

- [ ] **Step 2: Add test projects**

Run:

```powershell
dotnet new xunit -n Nerv.IIP.Business.MasterData.Domain.Tests -o backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.MasterData.Web.Tests -o backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests --framework net10.0
dotnet add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj reference backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/Nerv.IIP.Business.MasterData.Domain.csproj
dotnet add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj reference backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj
dotnet add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj reference backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj
```

Expected: test projects are added to the backend solution.

- [ ] **Step 3: Commit the scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/MasterData
git commit -m "feat: scaffold business master data service"
```

## Task 2: Add MasterData Domain Invariants

**Files:**

- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/MasterDataFacts.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/SkuAggregate/Sku.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/BusinessPartnerAggregate/BusinessPartner.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/DepartmentAggregate/Department.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/TeamAggregate/Team.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/PersonnelSkillAggregate/PersonnelSkill.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCenterAggregate/WorkCenter.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCalendarAggregate/WorkCalendar.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/DeviceAssetAggregate/DeviceAsset.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/DomainEvents/MasterDataDomainEvents.cs`
- Create: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/MasterDataAggregateTests.cs`

- [ ] **Step 1: Write failing aggregate tests**

Create `MasterDataAggregateTests.cs` with these tests:

```csharp
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.BusinessPartnerAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DepartmentAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.DeviceAssetAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.PersonnelSkillAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.SkuAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.TeamAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCalendarAggregate;
using Nerv.IIP.Business.MasterData.Domain.AggregatesModel.WorkCenterAggregate;

namespace Nerv.IIP.Business.MasterData.Domain.Tests;

public sealed class MasterDataAggregateTests
{
    [Fact]
    public void Sku_requires_code_name_unit_and_scope()
    {
        var sku = Sku.Create("org-001", "env-dev", "FG-1000", "Finished Good 1000", "EA", "finished-good");

        Assert.Equal("FG-1000", sku.Code);
        Assert.Equal("EA", sku.Unit);
        Assert.False(sku.Disabled);
    }

    [Fact]
    public void Sku_can_be_disabled_but_not_renamed_to_blank()
    {
        var sku = Sku.Create("org-001", "env-dev", "RM-1000", "Raw Material 1000", "KG", "raw-material");

        sku.Disable("duplicate registration");

        Assert.True(sku.Disabled);
        Assert.Throws<ArgumentException>(() => sku.Rename(" "));
    }

    [Fact]
    public void Business_partner_classifies_customer_supplier_and_carrier()
    {
        var partner = BusinessPartner.Create("org-001", "env-dev", "SUP-001", "supplier", "Acme Supplier");

        Assert.Equal("supplier", partner.PartnerType);
        Assert.False(partner.Disabled);
    }

    [Fact]
    public void Work_center_capacity_and_calendar_are_positive()
    {
        var workCenter = WorkCenter.Create("org-001", "env-dev", "WC-CNC-01", "CNC Cell 01", 480);
        var calendar = WorkCalendar.Create("org-001", "env-dev", "CAL-DAY", "Day Shift Calendar");
        calendar.AddWorkingTime(DayOfWeek.Monday, TimeOnly.FromTimeSpan(TimeSpan.FromHours(8)), TimeOnly.FromTimeSpan(TimeSpan.FromHours(16)));

        Assert.Equal(480, workCenter.CapacityMinutesPerDay);
        Assert.Single(calendar.WorkingTimes);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkCenter.Create("org-001", "env-dev", "WC-BAD", "Bad Cell", 0));
    }

    [Fact]
    public void Department_team_and_personnel_skill_reference_business_scope_without_copying_iam_user_facts()
    {
        var department = Department.Create("org-001", "env-dev", "D-PROD", "Production", null);
        var team = Team.Create("org-001", "env-dev", "T-DAY-A", "Day Shift A", department.Code, "day-shift");
        var skill = PersonnelSkill.Assign("org-001", "env-dev", "user-001", "welding", "level-2", DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)));

        Assert.Equal("D-PROD", department.Code);
        Assert.Equal("D-PROD", team.DepartmentCode);
        Assert.Equal("user-001", skill.UserId);
        Assert.Equal("welding", skill.SkillCode);
        Assert.True(skill.IsValidOn(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))));
    }

    [Fact]
    public void Device_asset_belongs_to_work_center_without_holding_control_secrets()
    {
        var asset = DeviceAsset.Register("org-001", "env-dev", "DEV-CNC-01", "CNC-500", "line-1", "WC-CNC-01");

        Assert.Equal("WC-CNC-01", asset.WorkCenterCode);
        Assert.Empty(asset.ControlSecretNames);
    }
}
```

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
```

Expected: FAIL because the aggregate types do not exist yet.

- [ ] **Step 2: Implement aggregate signatures and facts**

Implement the domain model with these public members:

```csharp
namespace Nerv.IIP.Business.MasterData.Domain;

public static class MasterDataFacts
{
    public const string Schema = "business_masterdata";
    public const string ServiceName = "BusinessMasterData";
}
```

Each aggregate must expose `OrganizationId`, `EnvironmentId`, `Code`, `Disabled`, `CreatedAtUtc`, `UpdatedAtUtc` and domain methods matching the tests. Use `ArgumentException` for blank text, `ArgumentOutOfRangeException` for non-positive capacity and `InvalidOperationException` for state transitions that would mutate a disabled aggregate.

`PersonnelSkill` exposes `OrganizationId`, `EnvironmentId`, `UserId`, `SkillCode`, `Level`, `EffectiveFrom`, `EffectiveTo`, `Disabled`, `CreatedAtUtc`, `UpdatedAtUtc` and `IsValidOn(DateOnly date)`. It stores only IAM `userId` references and does not copy login name, email, roles or membership facts from IAM.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit domain model**

Run:

```powershell
git add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests
git commit -m "feat: add business master data aggregates"
```

## Task 3: Add Persistence, Migration and Schema Catalog

**Files:**

- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/SkuEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/BusinessPartnerEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/DepartmentEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/TeamEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/PersonnelSkillEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/WorkCenterEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/WorkCalendarEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/DeviceAssetEntityTypeConfiguration.cs`
- Create: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataSchemaConventionTests.cs`
- Create: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataPostgresProfileTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Write schema convention tests**

Create tests that call `SchemaConventionAssertions` against the MasterData `ApplicationDbContext` and assert:

```csharp
Assert.Equal("business_masterdata", db.Model.GetDefaultSchema());
SchemaConventionAssertions.AssertBusinessTablesHaveComments(db);
SchemaConventionAssertions.AssertBusinessColumnsHaveComments(db);
SchemaConventionAssertions.AssertMigrationsHistoryTableUsesSchema(db, "business_masterdata");
```

Expected initial result: FAIL because the DbContext and entity configurations do not exist yet.

- [ ] **Step 2: Configure tables and indexes**

Configure these tables and unique indexes:

| Table | Unique key | Required list index |
| --- | --- | --- |
| `skus` | organizationId + environmentId + code | category + disabled |
| `business_partners` | organizationId + environmentId + partnerType + code | partnerType + disabled |
| `departments` | organizationId + environmentId + code | parentDepartmentCode + disabled |
| `teams` | organizationId + environmentId + code | departmentCode + disabled |
| `personnel_skills` | organizationId + environmentId + userId + skillCode + effectiveFrom | userId + disabled; skillCode + disabled |
| `work_centers` | organizationId + environmentId + code | disabled |
| `work_calendars` | organizationId + environmentId + code | disabled |
| `device_assets` | organizationId + environmentId + code | workCenterCode + disabled |

Every business property must have an English column comment that names the business meaning and unit where relevant.

- [ ] **Step 3: Generate migration**

Run:

```powershell
dotnet ef migrations add InitialBusinessMasterData --project backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Nerv.IIP.Business.MasterData.Infrastructure.csproj --startup-project backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj --output-dir Migrations
```

Expected: migration creates the `business_masterdata` schema, eight business tables, indexes and the service schema migrations history configuration.

- [ ] **Step 4: Update schema catalog**

Add a `BusinessMasterData` section to `docs/architecture/database-schema-catalog.md` with table purpose, owner, key columns, index intent and lifecycle for each table listed above.

- [ ] **Step 5: Run persistence tests**

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MasterDataSchemaConventionTests|FullyQualifiedName~MasterDataPostgresProfileTests"
```

Expected: PASS when `NERV_IIP_TEST_POSTGRES` is configured; schema convention tests pass regardless of PostgreSQL availability.

- [ ] **Step 6: Commit persistence**

Run:

```powershell
git add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests docs/architecture/database-schema-catalog.md
git commit -m "feat: persist business master data"
```

## Task 4: Add Commands, Queries, Endpoints and Authorization

**Files:**

- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Auth/BusinessPermissionCodes.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateSkuCommand.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateBusinessPartnerCommand.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateDepartmentCommand.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateTeamCommand.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/AssignPersonnelSkillCommand.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateWorkCenterCommand.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateWorkCalendarCommand.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/RegisterDeviceAssetCommand.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListSkusQuery.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListBusinessPartnersQuery.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListDepartmentsQuery.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListTeamsQuery.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListPersonnelSkillsQuery.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListResourcesQuery.cs`
- Create: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/MasterData/MasterDataEndpoints.cs`
- Create: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataEndpointTests.cs`
- Create: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataOpenApiTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`

- [ ] **Step 1: Write endpoint tests**

Cover these routes and permissions:

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/master-data/skus` | `business.masterdata.products.manage` |
| `GET /api/business/v1/master-data/skus` | `business.masterdata.products.read` |
| `POST /api/business/v1/master-data/partners` | `business.masterdata.partners.manage` |
| `GET /api/business/v1/master-data/partners` | `business.masterdata.partners.read` |
| `POST /api/business/v1/master-data/departments` | `business.masterdata.resources.manage` |
| `GET /api/business/v1/master-data/departments` | `business.masterdata.resources.read` |
| `POST /api/business/v1/master-data/teams` | `business.masterdata.resources.manage` |
| `GET /api/business/v1/master-data/teams` | `business.masterdata.resources.read` |
| `POST /api/business/v1/master-data/personnel-skills` | `business.masterdata.resources.manage` |
| `GET /api/business/v1/master-data/personnel-skills` | `business.masterdata.resources.read` |
| `POST /api/business/v1/master-data/work-centers` | `business.masterdata.resources.manage` |
| `POST /api/business/v1/master-data/work-calendars` | `business.masterdata.resources.manage` |
| `POST /api/business/v1/master-data/device-assets` | `business.masterdata.resources.manage` |
| `GET /api/business/v1/master-data/resources` | `business.masterdata.resources.read` |

Tests must assert anonymous requests return `401`, missing permission returns `403`, successful create returns `200` or `201`, and duplicate business keys return a known error response.

- [ ] **Step 2: Implement permission code constants**

Create constants exactly matching `docs/architecture/authorization-matrix.md`:

```csharp
public static class BusinessPermissionCodes
{
    public const string MasterDataProductsRead = "business.masterdata.products.read";
    public const string MasterDataProductsManage = "business.masterdata.products.manage";
    public const string MasterDataPartnersRead = "business.masterdata.partners.read";
    public const string MasterDataPartnersManage = "business.masterdata.partners.manage";
    public const string MasterDataResourcesRead = "business.masterdata.resources.read";
    public const string MasterDataResourcesManage = "business.masterdata.resources.manage";
}
```

- [ ] **Step 3: Implement commands and queries**

Requests must include `organizationId` and `environmentId`. List queries must support `keyword`, `status`, `page`, `pageSize`; partner and resource lists also support `partnerType` or `resourceType`. Department lists support `parentDepartmentCode`, team lists support `departmentCode`, and personnel skill lists support `userId`, `skillCode` and `validOn`.

- [ ] **Step 4: Seed permissions in IAM**

Add the six MasterData permissions to the IAM seed permission list and assign them to the seeded admin role. Keep the permission strings identical to the authorization matrix.

- [ ] **Step 5: Run endpoint and OpenAPI tests**

Run:

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

Expected: PASS. OpenAPI test confirms the fourteen operation IDs are stable and all endpoints require authorization.

- [ ] **Step 6: Commit API surface**

Run:

```powershell
git add backend/services/Business/MasterData backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs
git commit -m "feat: expose business master data api"
```

## Task 5: Add Verification Script Entry and Readiness Notes

**Files:**

- Create: `scripts/verify-business-master-data-foundation.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Add verification script**

The script must run:

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore
```

Expected: exits `0` when tests pass. Follow `docs/architecture/script-automation-governance.md` for classification and side-effect declaration.

- [ ] **Step 2: Update readiness documentation**

Document that Slice 1 is implemented, list the service path, schema, permissions, API coverage and verification script.

- [ ] **Step 3: Run final verification**

Run:

```powershell
scripts/verify-business-master-data-foundation.ps1
git diff --check
```

Expected: both commands exit `0`.

- [ ] **Step 4: Commit verification and docs**

Run:

```powershell
git add scripts/verify-business-master-data-foundation.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record business master data readiness"
```

## Self-Review Checklist

1. Every MasterData requirement from BP-MD-001 through BP-MD-005 has a domain aggregate, endpoint test, migration and schema catalog entry.
2. The service does not reference IAM Infrastructure or any Business service outside MasterData.
3. Permission strings match `docs/architecture/authorization-matrix.md`.
4. `business_masterdata` is the only default schema used by the service.
5. PostgreSQL profile and schema convention tests cover comments, string lengths and migrations history schema.
