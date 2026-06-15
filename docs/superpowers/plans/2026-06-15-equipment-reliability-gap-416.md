# Equipment Reliability Gap 416 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close GitHub issue #416 for IndustrialTelemetry and Maintenance reliability gaps: preventive-maintenance due work orders, spare-part inventory movement requests, SEMI E10-aligned OEE running-state mapping, alarm-clear work-order recovery marking, and MTBF/MTTR query coverage.

**Architecture:** Maintenance remains the owner of maintenance plans, work orders, inspections, spare-part demand and reliability metrics. IndustrialTelemetry remains the owner of device state, alarm and OEE input facts. Cross-service collaboration uses public contracts only: Maintenance consumes `Nerv.IIP.Contracts.IndustrialTelemetry.AlarmClearedIntegrationEvent` and publishes `Nerv.IIP.Contracts.Inventory.InventoryMovementRequestedIntegrationEvent` from Maintenance domain events; it does not reference Inventory service projects.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, EF Core, netcorepal domain/integration events, ADR 0011 envelopes, xUnit.

---

## File Map

- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/DomainEvents/MaintenanceDomainEvents.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceEntityTypeConfigurations.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Program.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceCommands.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/MaintenanceQueries.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventConverters/MaintenanceIntegrationEventConverters.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventHandlers/MarkWorkOrderAlarmClearedHandler.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/MaintenanceAggregateTests.cs`
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointContractTests.cs`
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventHandlerTests.cs`
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventTests.cs`
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSchemaConventionTests.cs`
- Modify: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Queries/IndustrialTelemetryQueries.cs`
- Modify: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/IndustrialTelemetryEndpointContractTests.cs`
- Modify: `docs/architecture/equipment-status-event-flow.md`
- Modify: `docs/architecture/implementation-readiness.md`

## Task 1: TDD Tests For Reliability Gaps

- [ ] **Step 1: Add Maintenance aggregate tests**

Add tests proving:

```csharp
var plan = MaintenancePlan.Create("org-001", "env-dev", "DEV-CNC-01", "PM-001", "P7D", new DateOnly(2026, 6, 1), "maintenance");
Assert.True(plan.IsDueOn(new DateOnly(2026, 6, 8)));

var order = MaintenanceWorkOrder.OpenFromAlarm("org-001", "env-dev", "DEV-CNC-01", "alarm-001", "critical");
order.MarkAlarmCleared(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
Assert.True(order.AlarmCleared);
```

Run:

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj --no-restore
```

Expected RED: missing due-generation and alarm-clear members.

- [ ] **Step 2: Add Maintenance web tests**

Add tests proving:

```csharp
await new GenerateDueMaintenanceWorkOrdersCommandHandler(dbContext).Handle(
    new GenerateDueMaintenanceWorkOrdersCommand("org-001", "env-dev", new DateOnly(2026, 6, 8), "system:pm"),
    CancellationToken.None);

var reliability = await new QueryAssetReliabilityQueryHandler(dbContext).Handle(
    new QueryAssetReliabilityQuery("org-001", "env-dev", "DEV-CNC-01", windowStart, windowEnd),
    CancellationToken.None);
```

Also assert `AlarmClearedIntegrationEvent` marks the matching open work order, and `MaintenanceSparePartIssuedDomainEvent` converts to `InventoryMovementRequestedIntegrationEvent` with a negative outbound quantity and idempotency key `maintenance:{org}:{env}:{workOrderId}:{sparePartLineId}`.

Run:

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
```

Expected RED: command/query/handler/converter do not exist.

- [ ] **Step 3: Add IndustrialTelemetry OEE test**

Add a test that a window split between `running` and `standby` reports availability from productive running time only, while runtime availability can still classify `standby` as available:

```csharp
Assert.Equal(0.5m, response.AvailabilityRate);
```

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter FullyQualifiedName~Oee
```

Expected RED: current OEE includes `standby` as running time.

## Task 2: Implement Maintenance Domain And Commands

- [ ] **Step 1: Extend `MaintenancePlan`**

Add nullable generation state:

```csharp
public DateOnly? LastGeneratedOn { get; private set; }
public DateOnly NextDueOn { get; private set; }
public bool IsDueOn(DateOnly businessDate) => NextDueOn <= businessDate;
public void MarkGenerated(DateOnly generatedOn)
{
    LastGeneratedOn = generatedOn;
    NextDueOn = generatedOn.AddDays(ParseIsoDayInterval(Interval));
}
```

P0 supports ISO day intervals like `P7D`; invalid intervals throw `KnownException` in command validation.

- [ ] **Step 2: Extend `MaintenanceWorkOrder`**

Add alarm-clear marker and spare-part issue event:

```csharp
public bool AlarmCleared { get; private set; }
public DateTimeOffset? AlarmClearedAtUtc { get; private set; }
public void MarkAlarmCleared(DateTimeOffset clearedAtUtc) { ... }
```

When `Complete` replaces spare-part lines, raise one `MaintenanceSparePartIssuedDomainEvent` per line after line creation.

- [ ] **Step 3: Add commands**

Add `GenerateDueMaintenanceWorkOrdersCommand` scanning due plans for one organization/environment/date, creating one open manual work order per due plan, marking the generated plan, and returning generated count. Use the plan code as source context and rely on plan state for idempotency.

Add `MarkMaintenanceWorkOrderAlarmClearedCommand` matching `SourceAlarmId`, organization and environment, then calling `MarkAlarmCleared`.

- [ ] **Step 4: Register alarm-clear consumer**

Add `MarkWorkOrderAlarmClearedHandler` using `IntegrationEventConsumerGuard<AlarmClearedIntegrationEvent>`, the same inbox helper, and no direct IndustrialTelemetry implementation references.

## Task 3: Inventory Movement Requests

- [ ] **Step 1: Reference Inventory contracts**

Add a Maintenance Web project reference to `backend/common/Contracts/Nerv.IIP.Contracts.Inventory/Nerv.IIP.Contracts.Inventory.csproj`.

- [ ] **Step 2: Add converter**

Convert `MaintenanceSparePartIssuedDomainEvent` to `InventoryMovementRequestedIntegrationEvent`:

```csharp
new InventoryMovementRequestedPayload(
    MovementType: "outbound",
    SourceService: "maintenance",
    SourceDocumentId: workOrder.Id.ToString(),
    SourceDocumentLineId: line.Id.ToString(),
    IdempotencyKey: idempotencyKey,
    SkuCode: line.SkuCode,
    UomCode: line.UomCode ?? "EA",
    SiteCode: "maintenance",
    LocationCode: "maintenance-spares",
    LotNo: null,
    SerialNo: null,
    QualityStatus: "available",
    OwnerType: "maintenance",
    OwnerId: null,
    Quantity: -Math.Abs(line.Quantity),
    RequestedAtUtc: workOrder.CompletedAtUtc ?? DateTimeOffset.UtcNow);
```

The chosen `SiteCode`/`LocationCode` are explicit P0 defaults documented as configurable follow-up because Maintenance does not own warehouse master data.

## Task 4: Queries And Endpoints

- [ ] **Step 1: Reliability metrics query**

Add `QueryAssetReliabilityQuery` returning:

```csharp
public sealed record AssetReliabilityResponse(
    string OrganizationId,
    string EnvironmentId,
    string DeviceAssetId,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    int FailureCount,
    int RepairCount,
    decimal MtbfHours,
    decimal MttrMinutes);
```

Failure count is completed or open work orders with `SourceAlarmId != null` in the window. MTTR is average `CompletedAtUtc - OpenedAtUtc` for completed fault orders. MTBF P0 uses elapsed query-window hours divided by failure count, because runtime-hour integration remains a later IndustrialTelemetry adapter.

- [ ] **Step 2: Endpoint contracts**

Add:

```text
POST /api/business/v1/maintenance/plans/generate-due
GET /api/business/v1/maintenance/assets/{deviceAssetId}/reliability
```

Both require `InternalServiceAuthorizationPolicy`; generation uses `business.maintenance.plans.manage`, reliability uses `business.maintenance.work-orders.read`.

## Task 5: IndustrialTelemetry OEE Mapping

- [ ] **Step 1: Centralize state classifier**

Keep runtime availability states permissive (`available`, `idle`, `ready`, `running`, `standby`) but narrow OEE productive states to `running` and `productive`. Add comments/tests tying this to SEMI E10 Productive vs Standby.

- [ ] **Step 2: Run IndustrialTelemetry focused test**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter FullyQualifiedName~Oee
```

Expected GREEN.

## Task 6: Docs And Verification

- [ ] **Step 1: Update docs**

Update `equipment-status-event-flow.md` and `implementation-readiness.md` with:

1. alarm clear marks Maintenance work orders as restored-pending-confirmation;
2. PM due generation is available as bounded command/API, not a long-running scheduler until deployment policy is finalized;
3. spare-part issue publishes Inventory movement requests with P0 maintenance stock defaults;
4. MTBF/MTTR P0 uses work-order elapsed time and query-window hours.

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/Nerv.IIP.Business.Maintenance.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --no-restore --filter FullyQualifiedName~Oee
dotnet build backend/Nerv.IIP.sln --no-restore
git diff --check
```

Expected: all pass with no new warnings.

## Self-Review

- #416 P0 PM generation is covered by plan state and command-level idempotency.
- #416 P0 spare-part inventory movement uses `Nerv.IIP.Contracts.Inventory` only.
- #416 P1 OEE mapping separates productive time from standby/idle availability.
- #416 P1 alarm clear is consumed by Maintenance and does not auto-complete work orders.
- #416 P1 MTBF/MTTR is exposed from Maintenance with P0 limitations documented.
