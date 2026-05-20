# Business Maintenance MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Maintenance lite for maintenance work orders, maintenance plans, inspections, downtime reasons and asset availability events.

**Architecture:** Maintenance is an independent CMMS-lite CleanDDD service under `backend/services/Business/Maintenance`. It consumes IndustrialTelemetry alarm events and references MasterData device assets, but it does not own device master data, telemetry samples, production work orders or spare-part stock balance.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal integration events, xUnit.

---

## Boundaries

1. Do not create or mutate `DeviceAsset`; reference MasterData device IDs/codes.
2. Do not store telemetry samples or alarm raw payloads; consume alarm event IDs.
3. Do not own spare-part inventory balance; request or reference Inventory movements.
4. Do not change MES schedules directly; publish asset availability events for MES and Planning.
5. Do not implement full EAM depreciation or asset accounting.

## File Structure Map

```text
backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/
  MaintenanceFacts.cs
  AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs
  AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs
  AggregatesModel/MaintenanceInspectionAggregate/MaintenanceInspection.cs
  AggregatesModel/DowntimeReasonAggregate/DowntimeReason.cs
  DomainEvents/MaintenanceDomainEvents.cs

backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/
  ApplicationDbContext.cs
  EntityConfigurations/*.cs
  Repositories/*.cs
  Migrations/*

backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/
  Application/Auth/MaintenancePermissionCodes.cs
  Application/Commands/CreateMaintenanceWorkOrderCommand.cs
  Application/Commands/CompleteMaintenanceWorkOrderCommand.cs
  Application/Commands/CreateMaintenancePlanCommand.cs
  Application/Commands/RecordMaintenanceInspectionCommand.cs
  Application/Queries/ListMaintenanceWorkOrdersQuery.cs
  Application/Queries/ListMaintenancePlansQuery.cs
  Application/IntegrationEvents/MaintenanceIntegrationEvents.cs
  Application/IntegrationEventHandlers/OpenWorkOrderWhenAlarmRaisedHandler.cs
  Endpoints/Maintenance/MaintenanceEndpoints.cs

backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/
  MaintenanceAggregateTests.cs

backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/
  MaintenanceEndpointTests.cs
  MaintenanceIntegrationEventHandlerTests.cs
  MaintenanceSchemaConventionTests.cs
```

## Task 1: Scaffold Maintenance Service

**Files:**

- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/Nerv.IIP.Business.Maintenance.Domain.csproj`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Nerv.IIP.Business.Maintenance.Infrastructure.csproj`
- Create: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj`
- Create: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create service and test projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Maintenance -o backend/services/Business/Maintenance --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Maintenance.Domain.Tests -o backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Maintenance.Web.Tests -o backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/Nerv.IIP.Business.Maintenance.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Nerv.IIP.Business.Maintenance.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj
```

Expected: projects are added and no IndustrialTelemetry project is created by this plan.

- [ ] **Step 2: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Maintenance
git commit -m "feat: scaffold maintenance service"
```

## Task 2: Implement Maintenance Domain Facts

**Files:**

- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/MaintenanceFacts.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceInspectionAggregate/MaintenanceInspection.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/DowntimeReasonAggregate/DowntimeReason.cs`
- Create: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/MaintenanceAggregateTests.cs`

- [ ] **Step 1: Write failing maintenance tests**

Create tests covering:

```csharp
var workOrder = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, "over temperature");
workOrder.Complete("replaced sensor", 45, new[] { SparePartLine.Create("SKU-SP-001", 1m) });

var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "weekly-inspection", "P7D", DateOnly.FromDateTime(DateTime.UtcNow));
var inspection = MaintenanceInspection.Record("org-001", "env-dev", plan.Id.Value, "operator-001", "passed", DateTimeOffset.UtcNow);
```

Assert completion requires result and downtime attribution, plan interval is explicit, spare part quantities are positive, and inspections reference a plan or work order.

- [ ] **Step 2: Implement events**

Create `MaintenanceWorkOrderOpenedDomainEvent`, `MaintenanceWorkOrderCompletedDomainEvent`, `AssetUnavailableDomainEvent`, `AssetRestoredDomainEvent`, `MaintenancePlanCreatedDomainEvent` and `MaintenanceInspectionRecordedDomainEvent`.

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj --no-restore
git add backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests
git commit -m "feat: add maintenance cmms lite facts"
```

Expected: tests pass before commit.

## Task 3: Add Persistence, Alarm Consumer and Events

**Files:**

- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceWorkOrderEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenancePlanEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceInspectionEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/DowntimeReasonEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEvents/MaintenanceIntegrationEvents.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventHandlers/OpenWorkOrderWhenAlarmRaisedHandler.cs`
- Create: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventHandlerTests.cs`
- Create: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSchemaConventionTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Configure schema**

Use schema `maintenance`. Tables are `maintenance_work_orders`, `maintenance_plans`, `maintenance_inspections`, `downtime_reasons`.

- [ ] **Step 2: Implement alarm consumer**

`OpenWorkOrderWhenAlarmRaisedHandler` consumes `industrialTelemetry.AlarmRaised` and creates one maintenance work order per `sourceAlarmId`. Duplicate delivery returns the existing work order ID and does not create a second work order.

- [ ] **Step 3: Define integration events**

Create:

```csharp
public sealed record MaintenanceWorkOrderOpenedIntegrationEvent(string WorkOrderId, string DeviceAssetId, string? SourceAlarmId, string Priority);
public sealed record MaintenanceWorkOrderCompletedIntegrationEvent(string WorkOrderId, string DeviceAssetId, int DowntimeMinutes);
public sealed record AssetUnavailableIntegrationEvent(string DeviceAssetId, string Reason, DateTimeOffset FromUtc);
public sealed record AssetRestoredIntegrationEvent(string DeviceAssetId, DateTimeOffset RestoredAtUtc);
```

- [ ] **Step 4: Run schema and handler tests**

Run:

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~MaintenanceSchemaConventionTests|FullyQualifiedName~MaintenanceIntegrationEventHandlerTests"
```

Expected: PASS.

- [ ] **Step 5: Commit persistence**

Run:

```powershell
git add backend/services/Business/Maintenance docs/architecture/database-schema-catalog.md
git commit -m "feat: persist maintenance facts"
```

## Task 4: Add Maintenance API and Permissions

**Files:**

- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Auth/MaintenancePermissionCodes.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/CreateMaintenanceWorkOrderCommand.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/CompleteMaintenanceWorkOrderCommand.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/CreateMaintenancePlanCommand.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/RecordMaintenanceInspectionCommand.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/ListMaintenanceWorkOrdersQuery.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/ListMaintenancePlansQuery.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- Create: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`

- [ ] **Step 1: Add routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/maintenance/work-orders` | `business.maintenance.work-orders.manage` |
| `POST /api/business/v1/maintenance/work-orders/{workOrderId}/complete` | `business.maintenance.work-orders.manage` |
| `GET /api/business/v1/maintenance/work-orders` | `business.maintenance.work-orders.read` |
| `POST /api/business/v1/maintenance/plans` | `business.maintenance.plans.manage` |
| `GET /api/business/v1/maintenance/plans` | `business.maintenance.plans.read` |
| `POST /api/business/v1/maintenance/inspections` | `business.maintenance.plans.manage` |

- [ ] **Step 2: Seed permissions**

Seed `business.maintenance.work-orders.read`, `business.maintenance.work-orders.manage`, `business.maintenance.plans.read`, `business.maintenance.plans.manage`.

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
git add backend/services/Business/Maintenance backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs
git commit -m "feat: expose maintenance api"
```

Expected: tests pass before commit.

## Task 5: Add Verification and Readiness

**Files:**

- Create: `scripts/verify-business-maintenance-mvp.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Run verification**

Run:

```powershell
scripts/verify-business-maintenance-mvp.ps1
git diff --check
```

Expected: script runs Maintenance domain and web tests and exits `0`.

- [ ] **Step 2: Commit docs**

Run:

```powershell
git add scripts/verify-business-maintenance-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record maintenance readiness"
```

## Self-Review Checklist

1. Maintenance is tracked independently from IndustrialTelemetry.
2. Alarm-to-work-order flow is idempotent.
3. Asset unavailable/restored events are available for MES, Planning and Notification consumers.
4. Maintenance stores no telemetry samples, DeviceAsset master data or spare-part inventory balance.
