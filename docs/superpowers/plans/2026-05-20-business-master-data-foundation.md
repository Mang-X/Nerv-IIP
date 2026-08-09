# 业务主数据基础实施计划

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**构建首个业务平台服务，提供 SKU、业务伙伴、工作中心、日历和设备资产主数据。

**架构：**将 `backend/services/Business/MasterData` 创建为包含三个项目的 CleanDDD/netcorepal 服务。MasterData 仅拥有业务主数据，以字符串形式引用 IAM 的组织/环境标识符，绝不读取 IAM 表。PostgreSQL 持久化使用 `business_masterdata` schema、服务本地 migration 和 schema 约定测试。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal repository/unit-of-work 原语、xUnit、PostgreSQL profile 测试。

---

## 重新对齐门禁

2026-05-21 的审核发现，本计划是有效的最小骨架，但不足以作为同时支持离散制造和流程制造的长期 MasterData 基础。继续执行任务 4 或任务 5 前，先执行 `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`。

重新对齐受以下文档治理：

1. `docs/adr/0013-business-master-data-governance.md`
2. `docs/architecture/business-master-data-field-matrix.md`
3. `docs/architecture/business-master-data-process-manufacturing-supplement.md`

任务 1 至任务 3 可视为历史基础工作。任务 4 和任务 5 必须由重新对齐计划更新，以确保 API 契约、IAM 权限、schema 目录和就绪说明涵盖 UOM、SKU 工业属性、资源层级、流程制造边界、下游解析 API 和 MasterData 变更事件。

## 输入资料

1. `docs/adr/0012-business-platform-domain-layering.md`
2. `docs/architecture/business-platform-domain-architecture.md`
3. `docs/superpowers/specs/2026-05-20-business-platform-domain-design.md`
4. `docs/architecture/backend-cleanddd-netcorepal-guidelines.md`
5. `docs/architecture/database-schema-conventions.md`
6. `docs/architecture/authorization-matrix.md`

## 边界

1. 不得在本服务中创建 ProductEngineering、Inventory、WMS、MES、ERP、Telemetry 或 Maintenance 规则。
2. 不得复制 IAM 用户、角色、成员关系或权限。
3. 不得在 `DeviceAsset` 上持久化 PLC/DCS/SCADA 连接密钥。
4. 本计划不得在前端添加业务页面。
5. 所有数据库对象必须位于 `business_masterdata` schema 内。

## 文件结构图

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

## 任务 1：搭建 MasterData 服务骨架

**文件：**

- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/Nerv.IIP.Business.MasterData.Domain.csproj`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Nerv.IIP.Business.MasterData.Infrastructure.csproj`
- 创建：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj`
- 创建：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：使用已批准模板创建服务**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.MasterData -o backend/services/Business/MasterData --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/Nerv.IIP.Business.MasterData.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Nerv.IIP.Business.MasterData.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj
```

预期：命令以 `0` 退出；生成的项目以 `net10.0` 为目标；任何服务都不引用 `backend/services/Iam`。

- [ ] **步骤 2：添加测试项目**

运行：

```powershell
dotnet new xunit -n Nerv.IIP.Business.MasterData.Domain.Tests -o backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.MasterData.Web.Tests -o backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests --framework net10.0
dotnet add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj reference backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/Nerv.IIP.Business.MasterData.Domain.csproj
dotnet add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj reference backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj
dotnet add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj reference backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj
```

预期：测试项目已添加到后端 solution。

- [ ] **步骤 3：提交服务骨架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/MasterData
git commit -m "feat: scaffold business master data service"
```

## 任务 2：添加 MasterData 领域不变量

**文件：**

- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/MasterDataFacts.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/SkuAggregate/Sku.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/BusinessPartnerAggregate/BusinessPartner.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/DepartmentAggregate/Department.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/TeamAggregate/Team.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/PersonnelSkillAggregate/PersonnelSkill.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCenterAggregate/WorkCenter.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/WorkCalendarAggregate/WorkCalendar.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/DeviceAssetAggregate/DeviceAsset.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/DomainEvents/MasterDataDomainEvents.cs`
- 创建：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/MasterDataAggregateTests.cs`

- [ ] **步骤 1：编写预期失败的聚合测试**

创建包含以下测试的 `MasterDataAggregateTests.cs`：

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

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
```

预期：失败，因为聚合类型尚不存在。

- [ ] **步骤 2：实现聚合签名和事实**

使用以下公共成员实现领域模型：

```csharp
namespace Nerv.IIP.Business.MasterData.Domain;

public static class MasterDataFacts
{
    public const string Schema = "business_masterdata";
    public const string ServiceName = "BusinessMasterData";
}
```

每个聚合都必须公开 `OrganizationId`、`EnvironmentId`、`Code`、`Disabled`、`CreatedAtUtc`、`UpdatedAtUtc`，以及与测试匹配的领域方法。空白文本使用 `ArgumentException`，非正数产能使用 `ArgumentOutOfRangeException`，会改变已禁用聚合的状态转换使用 `InvalidOperationException`。

`PersonnelSkill` 公开 `OrganizationId`、`EnvironmentId`、`UserId`、`SkillCode`、`Level`、`EffectiveFrom`、`EffectiveTo`、`Disabled`、`CreatedAtUtc`、`UpdatedAtUtc` 和 `IsValidOn(DateOnly date)`。它仅存储 IAM `userId` 引用，不从 IAM 复制登录名、电子邮件、角色或成员关系事实。

- [ ] **步骤 3：运行领域测试**

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
```

预期：通过。

- [ ] **步骤 4：提交领域模型**

运行：

```powershell
git add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests
git commit -m "feat: add business master data aggregates"
```

## 任务 3：添加持久化、migration 和 schema 目录

**文件：**

- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/SkuEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/BusinessPartnerEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/DepartmentEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/TeamEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/PersonnelSkillEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/WorkCenterEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/WorkCalendarEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/EntityConfigurations/DeviceAssetEntityTypeConfiguration.cs`
- 创建：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataSchemaConventionTests.cs`
- 创建：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataPostgresProfileTests.cs`
- 修改：`docs/architecture/database-schema-catalog.md`

- [ ] **步骤 1：编写 schema 约定测试**

创建针对 MasterData `ApplicationDbContext` 调用 `SchemaConventionAssertions` 的测试，并断言：

```csharp
Assert.Equal("business_masterdata", db.Model.GetDefaultSchema());
SchemaConventionAssertions.AssertBusinessTablesHaveComments(db);
SchemaConventionAssertions.AssertBusinessColumnsHaveComments(db);
SchemaConventionAssertions.AssertMigrationsHistoryTableUsesSchema(db, "business_masterdata");
```

预期初始结果：失败，因为 DbContext 和实体配置尚不存在。

- [ ] **步骤 2：配置表和索引**

配置以下表和唯一索引：

| 表 | 唯一键 | 必需的列表索引 |
| --- | --- | --- |
| `skus` | organizationId + environmentId + code | category + disabled |
| `business_partners` | organizationId + environmentId + partnerType + code | partnerType + disabled |
| `departments` | organizationId + environmentId + code | parentDepartmentCode + disabled |
| `teams` | organizationId + environmentId + code | departmentCode + disabled |
| `personnel_skills` | organizationId + environmentId + userId + skillCode + effectiveFrom | userId + disabled; skillCode + disabled |
| `work_centers` | organizationId + environmentId + code | disabled |
| `work_calendars` | organizationId + environmentId + code | disabled |
| `device_assets` | organizationId + environmentId + code | workCenterCode + disabled |

每个业务属性都必须具有英文列注释，注明业务含义，并在适用时注明单位。

- [ ] **步骤 3：生成 migration**

运行：

```powershell
dotnet ef migrations add InitialBusinessMasterData --project backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure/Nerv.IIP.Business.MasterData.Infrastructure.csproj --startup-project backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Nerv.IIP.Business.MasterData.Web.csproj --output-dir Migrations
```

预期：migration 创建 `business_masterdata` schema、八张业务表、索引和服务 schema 的 migration 历史配置。

- [ ] **步骤 4：更新 schema 目录**

在 `docs/architecture/database-schema-catalog.md` 中添加 `BusinessMasterData` 章节，说明上述各表的用途、所有者、关键列、索引意图和生命周期。

- [ ] **步骤 5：运行持久化测试**

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MasterDataSchemaConventionTests|FullyQualifiedName~MasterDataPostgresProfileTests"
```

预期：配置 `NERV_IIP_TEST_POSTGRES` 时通过；无论 PostgreSQL 是否可用，schema 约定测试都通过。

- [ ] **步骤 6：提交持久化实现**

运行：

```powershell
git add backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Infrastructure backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests docs/architecture/database-schema-catalog.md
git commit -m "feat: persist business master data"
```

## 任务 4：添加命令、查询、endpoint 和授权

**文件：**

- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Auth/BusinessPermissionCodes.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateSkuCommand.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateBusinessPartnerCommand.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateDepartmentCommand.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateTeamCommand.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/AssignPersonnelSkillCommand.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateWorkCenterCommand.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/CreateWorkCalendarCommand.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/RegisterDeviceAssetCommand.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListSkusQuery.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListBusinessPartnersQuery.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListDepartmentsQuery.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListTeamsQuery.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListPersonnelSkillsQuery.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListWorkCalendarsQuery.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Queries/ListResourcesQuery.cs`
- 创建：`backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/MasterData/MasterDataEndpoints.cs`
- 创建：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataEndpointTests.cs`
- 创建：`backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataOpenApiTests.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`

- [ ] **步骤 1：编写 endpoint 测试**

覆盖以下路由和权限：

| 路由 | 权限 |
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
| `GET /api/business/v1/master-data/work-calendars` | `business.masterdata.resources.read` |
| `POST /api/business/v1/master-data/device-assets` | `business.masterdata.resources.manage` |
| `GET /api/business/v1/master-data/resources` | `business.masterdata.resources.read` |

测试必须断言：匿名请求返回 `401`，缺少权限时返回 `403`，成功创建时返回 `200` 或 `201`，重复业务键返回已知错误响应。

- [ ] **步骤 2：实现权限码常量**

创建与 `docs/architecture/authorization-matrix.md` 完全匹配的常量：

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

- [ ] **步骤 3：实现命令和查询**

请求必须包含 `organizationId` 和 `environmentId`。列表查询必须支持 `keyword`、`status`、`page`、`pageSize`；伙伴列表和资源列表还支持 `partnerType` 或 `resourceType`。部门列表支持 `parentDepartmentCode`，班组列表支持 `departmentCode`，人员技能列表支持 `userId`、`skillCode` 和 `validOn`，工作日历列表支持 `keyword` 和 `status`。

- [ ] **步骤 4：在 IAM 中播种权限**

将六项 MasterData 权限添加到 IAM 种子权限列表，并将其分配给已播种的管理员角色。权限字符串必须与授权矩阵完全相同。

- [ ] **步骤 5：运行 endpoint 和 OpenAPI 测试**

运行：

```powershell
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

预期：通过。OpenAPI 测试确认十五个 operation ID 保持稳定，且所有 endpoint 都要求授权。

- [ ] **步骤 6：提交 API 接口**

运行：

```powershell
git add backend/services/Business/MasterData backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs
git commit -m "feat: expose business master data api"
```

## 任务 5：添加验证脚本入口和就绪说明

**文件：**

- 创建：`scripts/verify-business-master-data-foundation.ps1`
- 修改：`docs/architecture/implementation-readiness.md`
- 修改：`README.md`

- [ ] **步骤 1：添加验证脚本**

脚本必须运行：

```powershell
dotnet restore backend/Nerv.IIP.sln
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj --no-restore
```

预期：测试通过时以 `0` 退出。分类和副作用声明遵循 `docs/architecture/script-automation-governance.md`。

- [ ] **步骤 2：更新就绪文档**

记录纵切 1 已实施，并列出服务路径、schema、权限、API 覆盖范围和验证脚本。

- [ ] **步骤 3：运行最终验证**

运行：

```powershell
scripts/verify-business-master-data-foundation.ps1
git diff --check
```

预期：两条命令均以 `0` 退出。

- [ ] **步骤 4：提交验证内容和文档**

运行：

```powershell
git add scripts/verify-business-master-data-foundation.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record business master data readiness"
```

## 自审清单

1. BP-MD-001 至 BP-MD-005 的每项 MasterData 需求都有领域聚合、endpoint 测试、migration 和 schema 目录条目。
2. 本服务不引用 IAM Infrastructure，也不引用 MasterData 以外的任何业务服务。
3. 权限字符串与 `docs/architecture/authorization-matrix.md` 匹配。
4. `business_masterdata` 是本服务使用的唯一默认 schema。
5. PostgreSQL profile 和 schema 约定测试覆盖注释、字符串长度及 migration 历史 schema。
