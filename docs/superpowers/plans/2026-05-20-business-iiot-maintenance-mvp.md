# Business IIoT Maintenance MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build IndustrialTelemetry lite and Maintenance lite for tag mapping, device state, alarms, maintenance work orders, maintenance plans, inspections and asset availability events.

**Architecture:** IndustrialTelemetry receives controlled telemetry facts from Connector Host or external clients, stores tag mappings, device state snapshots, alarm events and coarse summaries. Maintenance owns CMMS-lite work orders and plans, consumes alarms and emits asset availability events. Neither service controls PLC/DCS/SCADA or stores device control credentials.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal integration events, xUnit.

---

## Boundaries

1. No PLC/DCS control commands.
2. No SCADA screen builder.
3. No high-frequency raw time-series storage profile in this slice.
4. Maintenance does not own DeviceAsset master data.
5. Maintenance does not own inventory balance for spare parts.

## File Structure Map

```text
backend/services/Business/IndustrialTelemetry/
  src/Nerv.IIP.Business.IndustrialTelemetry.Domain/
    AggregatesModel/TelemetryTagAggregate/TelemetryTag.cs
    AggregatesModel/DeviceStateSnapshotAggregate/DeviceStateSnapshot.cs
    AggregatesModel/AlarmEventAggregate/AlarmEvent.cs
  src/Nerv.IIP.Business.IndustrialTelemetry.Web/
    Application/Commands/CreateTelemetryTagCommand.cs
    Application/Commands/RecordTelemetrySampleCommand.cs
    Application/Commands/RaiseAlarmCommand.cs
    Application/Commands/ClearAlarmCommand.cs
    Application/Queries/QueryDeviceStateTimelineQuery.cs
    Application/IntegrationEvents/IndustrialTelemetryIntegrationEvents.cs

backend/services/Business/Maintenance/
  src/Nerv.IIP.Business.Maintenance.Domain/
    AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs
    AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs
    AggregatesModel/MaintenanceInspectionAggregate/MaintenanceInspection.cs
  src/Nerv.IIP.Business.Maintenance.Web/
    Application/Commands/CreateMaintenanceWorkOrderCommand.cs
    Application/Commands/CompleteMaintenanceWorkOrderCommand.cs
    Application/Commands/CreateMaintenancePlanCommand.cs
    Application/IntegrationEventHandlers/OpenWorkOrderWhenAlarmRaisedHandler.cs
```

## Task 1: Scaffold IndustrialTelemetry and Maintenance

**Files:**

- Create: `backend/services/Business/IndustrialTelemetry/*`
- Create: `backend/services/Business/Maintenance/*`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create services and tests**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.IndustrialTelemetry -o backend/services/Business/IndustrialTelemetry --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new netcorepal-web -n Nerv.IIP.Business.Maintenance -o backend/services/Business/Maintenance --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests -o backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.IndustrialTelemetry.Web.Tests -o backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Maintenance.Domain.Tests -o backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Maintenance.Web.Tests -o backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests --framework net10.0
```

Add all generated projects to `backend/Nerv.IIP.sln`.

- [ ] **Step 2: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/IndustrialTelemetry backend/services/Business/Maintenance
git commit -m "feat: scaffold iiot and maintenance services"
```

## Task 2: Implement IndustrialTelemetry Facts

**Files:**

- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/TelemetryTagAggregate/TelemetryTag.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/DeviceStateSnapshotAggregate/DeviceStateSnapshot.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/AlarmEventAggregate/AlarmEvent.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/IndustrialTelemetryAggregateTests.cs`

- [ ] **Step 1: Write failing telemetry tests**

Cover:

```csharp
var tag = TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-01", "spindle.speed", "number", "rpm", "sample-10s");
var state = DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-01", "running", DateTimeOffset.UtcNow, "connector-seq-001");
var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", DateTimeOffset.UtcNow, "alarm-ext-001");
alarm.Clear(DateTimeOffset.UtcNow.AddMinutes(10), "operator-001");
```

Assert tag key is unique per device, source sequence is idempotent per tag/state stream, alarm external ID is idempotent, and no aggregate exposes a control command payload.

- [ ] **Step 2: Implement events**

Create `TelemetrySampleRecordedDomainEvent`, `DeviceStateChangedDomainEvent`, `AlarmRaisedDomainEvent` and `AlarmClearedDomainEvent`.

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj --no-restore
git add backend/services/Business/IndustrialTelemetry
git commit -m "feat: add industrial telemetry facts"
```

Expected: tests pass before commit.

## Task 3: Implement Maintenance Facts

**Files:**

- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceInspectionAggregate/MaintenanceInspection.cs`
- Create: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/MaintenanceAggregateTests.cs`

- [ ] **Step 1: Write failing maintenance tests**

Cover:

```csharp
var workOrder = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
workOrder.MarkAssetUnavailable(DateTimeOffset.UtcNow, "over temperature");
workOrder.Complete("replaced sensor", 45, new[] { SparePartLine.Create("SKU-SP-001", 1m) });

var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "weekly-inspection", "P7D", DateOnly.FromDateTime(DateTime.UtcNow));
```

Assert completion requires result and downtime attribution, plan interval is explicit, and spare part quantities are positive.

- [ ] **Step 2: Implement events**

Create `MaintenanceWorkOrderOpenedDomainEvent`, `MaintenanceWorkOrderCompletedDomainEvent`, `AssetUnavailableDomainEvent`, `AssetRestoredDomainEvent` and `MaintenancePlanCreatedDomainEvent`.

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj --no-restore
git add backend/services/Business/Maintenance
git commit -m "feat: add maintenance cmms lite facts"
```

Expected: tests pass before commit.

## Task 4: Add API, Persistence, Alarm-to-Maintenance Flow and Permissions

**Files:**

- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Endpoints/Iiot/IiotEndpoints.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventHandlers/OpenWorkOrderWhenAlarmRaisedHandler.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointTests.cs`
- Create: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Configure schemas**

Use schemas `industrial_telemetry` and `maintenance`. Telemetry tables include tags, state snapshots and alarms; Maintenance tables include work orders, plans and inspections.

- [ ] **Step 2: Add routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/iiot/tags` | `business.iiot.tags.manage` |
| `POST /api/business/v1/iiot/samples` | `business.iiot.telemetry.write` |
| `POST /api/business/v1/iiot/alarms` | `business.iiot.alarms.write` |
| `GET /api/business/v1/iiot/alarms` | `business.iiot.alarms.read` |
| `GET /api/business/v1/iiot/devices/{deviceAssetId}/timeline` | `business.iiot.telemetry.read` |
| `POST /api/business/v1/maintenance/work-orders` | `business.maintenance.work-orders.manage` |
| `POST /api/business/v1/maintenance/work-orders/{workOrderId}/complete` | `business.maintenance.work-orders.manage` |
| `GET /api/business/v1/maintenance/work-orders` | `business.maintenance.work-orders.read` |
| `POST /api/business/v1/maintenance/plans` | `business.maintenance.plans.manage` |
| `GET /api/business/v1/maintenance/plans` | `business.maintenance.plans.read` |

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
git add backend/services/Business/IndustrialTelemetry backend/services/Business/Maintenance backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md
git commit -m "feat: expose iiot and maintenance api"
```

Expected: tests pass before commit.

## Task 5: Add Verification and Readiness

**Files:**

- Create: `scripts/verify-business-iiot-maintenance-mvp.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Run verification**

Run:

```powershell
scripts/verify-business-iiot-maintenance-mvp.ps1
git diff --check
```

Expected: script runs both services' domain and web tests and exits `0`.

- [ ] **Step 2: Commit docs**

Run:

```powershell
git add scripts/verify-business-iiot-maintenance-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record iiot maintenance readiness"
```

## Self-Review Checklist

1. No PLC/DCS/SCADA control command is modeled.
2. Connector-host writes are permissioned by telemetry write or alarm write permissions.
3. Alarm-to-maintenance flow is idempotent.
4. Asset unavailable/restored events are available for MES and Planning consumers.
