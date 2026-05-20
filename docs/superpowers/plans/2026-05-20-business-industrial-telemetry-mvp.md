# Business Industrial Telemetry MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build IndustrialTelemetry lite for tag mapping, controlled telemetry ingestion, device state snapshots, alarm events and coarse time-series summaries.

**Architecture:** IndustrialTelemetry is an independent CleanDDD service under `backend/services/Business/IndustrialTelemetry`. It accepts telemetry facts from Connector Host or authorized external clients through public APIs and business integration contracts. It does not own DeviceAsset master data, does not control PLC/DCS/SCADA and does not store high-frequency raw time-series data in the MVP.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal integration events, xUnit.

---

## Boundaries

1. Do not implement PLC/DCS control commands.
2. Do not implement SCADA screen building.
3. Do not store PLC/DCS credentials or raw control payloads.
4. Do not own `DeviceAsset`; reference device assets by stable ID/code from MasterData.
5. Do not create Maintenance work orders directly; publish alarm events for Maintenance to consume.

## File Structure Map

```text
backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/
  IndustrialTelemetryFacts.cs
  AggregatesModel/TelemetryTagAggregate/TelemetryTag.cs
  AggregatesModel/DeviceStateSnapshotAggregate/DeviceStateSnapshot.cs
  AggregatesModel/AlarmEventAggregate/AlarmEvent.cs
  AggregatesModel/TelemetrySummaryAggregate/TelemetrySummary.cs
  DomainEvents/IndustrialTelemetryDomainEvents.cs

backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/
  ApplicationDbContext.cs
  EntityConfigurations/*.cs
  Repositories/*.cs
  Migrations/*

backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/
  Application/Auth/IndustrialTelemetryPermissionCodes.cs
  Application/Commands/CreateTelemetryTagCommand.cs
  Application/Commands/RecordTelemetrySampleCommand.cs
  Application/Commands/RaiseAlarmCommand.cs
  Application/Commands/ClearAlarmCommand.cs
  Application/Queries/ListTelemetryTagsQuery.cs
  Application/Queries/QueryDeviceStateTimelineQuery.cs
  Application/Queries/ListAlarmEventsQuery.cs
  Application/IntegrationEvents/IndustrialTelemetryIntegrationEvents.cs
  Endpoints/Iiot/IiotEndpoints.cs

backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/
  IndustrialTelemetryAggregateTests.cs

backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/
  IndustrialTelemetryEndpointTests.cs
  IndustrialTelemetryIntegrationEventTests.cs
  IndustrialTelemetrySchemaConventionTests.cs
```

## Task 1: Scaffold IndustrialTelemetry Service

**Files:**

- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/Nerv.IIP.Business.IndustrialTelemetry.Domain.csproj`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.csproj`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create service and test projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.IndustrialTelemetry -o backend/services/Business/IndustrialTelemetry --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests -o backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.IndustrialTelemetry.Web.Tests -o backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/Nerv.IIP.Business.IndustrialTelemetry.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Nerv.IIP.Business.IndustrialTelemetry.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj
```

Expected: projects are added and no Maintenance project is created by this plan.

- [ ] **Step 2: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/IndustrialTelemetry
git commit -m "feat: scaffold industrial telemetry service"
```

## Task 2: Implement Telemetry Domain Facts

**Files:**

- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/IndustrialTelemetryFacts.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/TelemetryTagAggregate/TelemetryTag.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/DeviceStateSnapshotAggregate/DeviceStateSnapshot.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/AlarmEventAggregate/AlarmEvent.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain/AggregatesModel/TelemetrySummaryAggregate/TelemetrySummary.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/IndustrialTelemetryAggregateTests.cs`

- [ ] **Step 1: Write failing telemetry tests**

Create tests covering:

```csharp
var tag = TelemetryTag.Create("org-001", "env-dev", "DEV-CNC-01", "spindle.speed", "number", "rpm", "sample-10s");
var state = DeviceStateSnapshot.Record("org-001", "env-dev", "DEV-CNC-01", "running", DateTimeOffset.UtcNow, "connector-seq-001");
var alarm = AlarmEvent.Raise("org-001", "env-dev", "DEV-CNC-01", "OVER_TEMP", "critical", DateTimeOffset.UtcNow, "alarm-ext-001");
alarm.Clear(DateTimeOffset.UtcNow.AddMinutes(10), "operator-001");
```

Assert tag key is unique per device, source sequence is idempotent per tag/state stream, alarm external ID is idempotent, and no aggregate exposes a control command payload.

- [ ] **Step 2: Implement events**

Create `TelemetryTagCreatedDomainEvent`, `TelemetrySampleRecordedDomainEvent`, `DeviceStateChangedDomainEvent`, `AlarmRaisedDomainEvent` and `AlarmClearedDomainEvent`.

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj --no-restore
git add backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Domain backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests
git commit -m "feat: add industrial telemetry facts"
```

Expected: tests pass before commit.

## Task 3: Add Persistence and Events

**Files:**

- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/TelemetryTagEntityTypeConfiguration.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/DeviceStateSnapshotEntityTypeConfiguration.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/AlarmEventEntityTypeConfiguration.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Infrastructure/EntityConfigurations/TelemetrySummaryEntityTypeConfiguration.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/IntegrationEvents/IndustrialTelemetryIntegrationEvents.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryIntegrationEventTests.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetrySchemaConventionTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Configure schema**

Use schema `industrial_telemetry`. Tables are `telemetry_tags`, `device_state_snapshots`, `alarm_events`, `telemetry_summaries`. Add unique indexes for `deviceAssetId + tagKey`, `deviceAssetId + sourceSequence`, and `externalAlarmId`.

- [ ] **Step 2: Define integration events**

Create:

```csharp
public sealed record DeviceStateChangedIntegrationEvent(string DeviceAssetId, string PreviousState, string CurrentState, DateTimeOffset OccurredAtUtc);
public sealed record AlarmRaisedIntegrationEvent(string AlarmId, string DeviceAssetId, string AlarmCode, string Severity, DateTimeOffset OccurredAtUtc);
public sealed record AlarmClearedIntegrationEvent(string AlarmId, string DeviceAssetId, DateTimeOffset ClearedAtUtc);
```

- [ ] **Step 3: Run schema and event tests**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~IndustrialTelemetrySchemaConventionTests|FullyQualifiedName~IndustrialTelemetryIntegrationEventTests"
```

Expected: PASS.

- [ ] **Step 4: Commit persistence**

Run:

```powershell
git add backend/services/Business/IndustrialTelemetry docs/architecture/database-schema-catalog.md
git commit -m "feat: persist industrial telemetry facts"
```

## Task 4: Add Telemetry API and Permissions

**Files:**

- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Auth/IndustrialTelemetryPermissionCodes.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/CreateTelemetryTagCommand.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/RecordTelemetrySampleCommand.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/RaiseAlarmCommand.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Commands/ClearAlarmCommand.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/ListTelemetryTagsQuery.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/QueryDeviceStateTimelineQuery.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/ListAlarmEventsQuery.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Endpoints/Iiot/IiotEndpoints.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`

- [ ] **Step 1: Add routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/iiot/tags` | `business.iiot.tags.manage` |
| `GET /api/business/v1/iiot/tags` | `business.iiot.telemetry.read` |
| `POST /api/business/v1/iiot/samples` | `business.iiot.telemetry.write` |
| `POST /api/business/v1/iiot/alarms` | `business.iiot.alarms.write` |
| `GET /api/business/v1/iiot/alarms` | `business.iiot.alarms.read` |
| `GET /api/business/v1/iiot/devices/{deviceAssetId}/timeline` | `business.iiot.telemetry.read` |

- [ ] **Step 2: Seed permissions**

Seed `business.iiot.tags.manage`, `business.iiot.telemetry.read`, `business.iiot.telemetry.write`, `business.iiot.alarms.read`, `business.iiot.alarms.write`. Connector-host and external-client write tests must include organization/environment and capability scope.

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
git add backend/services/Business/IndustrialTelemetry backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs
git commit -m "feat: expose industrial telemetry api"
```

Expected: tests pass before commit.

## Task 5: Add Verification and Readiness

**Files:**

- Create: `scripts/verify-business-industrial-telemetry-mvp.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Run verification**

Run:

```powershell
scripts/verify-business-industrial-telemetry-mvp.ps1
git diff --check
```

Expected: script runs IndustrialTelemetry domain and web tests and exits `0`.

- [ ] **Step 2: Commit docs**

Run:

```powershell
git add scripts/verify-business-industrial-telemetry-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record industrial telemetry readiness"
```

## Self-Review Checklist

1. IndustrialTelemetry is tracked independently from Maintenance.
2. Connector-host writes are permissioned by telemetry write or alarm write permissions.
3. No PLC/DCS/SCADA control command is modeled.
4. Alarm and device-state events are available for Maintenance, MES, Planning and Notification consumers.
