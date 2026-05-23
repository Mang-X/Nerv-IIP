# WMS Execution MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #136 by creating WMS for inbound, outbound, putaway, picking, count execution and WCS adapter task boundaries.

**Architecture:** WMS is a CleanDDD business service under `backend/services/Business/Wms`. It owns warehouse execution state and inventory movement request metadata, but Inventory remains the only service that owns stock ledgers and movement facts. WMS integrates through public API/event boundaries.

**Tech Stack:** .NET 10, NetCorePal CleanDDD template, FastEndpoints, EF Core PostgreSQL, xUnit, ADR 0011 integration event conversion, `Nerv.IIP.Testing` schema convention helpers.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-wms-execution-mvp-design.md` as the domain contract for this plan.

## Files

- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/Nerv.IIP.Business.Wms.Domain.csproj`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Nerv.IIP.Business.Wms.Infrastructure.csproj`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/InboundOrderAggregate/InboundOrder.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/WarehouseTaskAggregate/WarehouseTask.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/CountExecutionAggregate/CountExecution.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/WcsTaskAggregate/WcsTask.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/InventoryMovementRequestAggregate/InventoryMovementRequest.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/DomainEvents/WmsDomainEvents.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Auth/WmsPermissionCodes.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Inventory/IInventoryMovementClient.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/*.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Queries/*.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEvents/WmsIntegrationEvents.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventConverters/WmsIntegrationEventConverters.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Endpoints/Wms/WmsEndpoints.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WmsExecutionAggregateTests.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WcsTaskAggregateTests.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsEndpointContractTests.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsInventoryBoundaryTests.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsIntegrationEventTests.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsSchemaConventionTests.cs`

Shared files requested from WAVE2-INTEG:

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-wms-execution-mvp.ps1`

## Task 1: Scaffold WMS Service Locally

- [ ] **Step 1: Create service projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Wms -o backend/services/Business/Wms --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Wms.Domain.Tests -o backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Wms.Web.Tests -o backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests --framework net10.0
```

- [ ] **Step 2: Remove template demo code**

Run:

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/Wms
```

Expected: no matches.

## Task 2: Implement Inbound And Outbound Execution

- [ ] **Step 1: Write failing execution tests**

Cover:

1. Inbound order creation with source document reference and lines.
2. Putaway task quantity cannot exceed inbound line quantity.
3. Inbound completion requires idempotency key and creates movement request metadata.
4. Outbound order creation with source document reference and lines.
5. Pick quantity cannot exceed outbound line quantity.
6. Pack review completion requires idempotency key and creates movement request metadata.
7. Completed inbound/outbound orders are immutable.

- [ ] **Step 2: Implement aggregate roots and domain events**

Implement inbound, outbound, warehouse task, count execution and inventory movement request aggregates.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore
```

Expected: WMS domain tests pass.

## Task 3: Implement WCS Adapter Boundary

- [ ] **Step 1: Write failing WCS tests**

Cover:

1. Dispatch is idempotent by warehouse task and adapter type.
2. Completed task cannot later fail.
3. Failed task stores diagnostic code and message.
4. Retry increments attempt count without changing original warehouse task reference.

- [ ] **Step 2: Implement WCS aggregate and events**

Implement `WcsTask` and events for dispatched, completed and failed states.

- [ ] **Step 3: Run WCS tests**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~WcsTaskAggregateTests
```

Expected: WCS tests pass.

## Task 4: Add Persistence, API And Inventory Boundary

- [ ] **Step 1: Configure DbContext**

Use schema `wms` and migrations history `wms.__EFMigrationsHistory`. No table may contain `on_hand_quantity`, `available_quantity` or `stock_balance`.

- [ ] **Step 2: Generate migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialWmsSchema --project backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Nerv.IIP.Business.Wms.Infrastructure.csproj --startup-project backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj --output-dir Migrations
```

- [ ] **Step 3: Add endpoint and inventory-boundary tests**

Create tests covering route shape, permission codes, operation IDs, inventory movement request payloads and idempotency keys.

- [ ] **Step 4: Implement commands, queries and FastEndpoints**

Implement endpoints from the spec under `Endpoints/Wms`. Keep Inventory posting behind `IInventoryMovementClient` so Web tests use a fake client.

- [ ] **Step 5: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --no-restore
```

Expected: WMS Web tests pass.

## Task 5: Add Events And Schema Guardrails

- [ ] **Step 1: Add event converter tests**

Verify event names:

1. `wms.InboundOrderCompleted`
2. `wms.OutboundOrderCompleted`
3. `wms.CountExecutionCompleted`
4. `wms.WcsTaskDispatched`
5. `wms.WcsTaskFailed`

- [ ] **Step 2: Add schema convention tests**

In addition to standard schema convention assertions, include a WMS-specific assertion that no mapped table/column name suggests stock balance ownership.

## Task 6: Handoff Shared Changes To WAVE2-INTEG

- [ ] **Step 1: Record shared changes**

In the PR/session summary, include:

```markdown
## Shared Changes Needed

- Add WMS projects/tests to `backend/Nerv.IIP.sln`.
- Register WMS in AppHost.
- Add WMS permissions to IAM seed and `authorization-matrix.md`.
- Add `wms` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-wms-execution-mvp.ps1`.
- Add Inventory base URL environment variable if the first WMS adapter calls Inventory over HTTP.
```

- [ ] **Step 2: Run final focused verification**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --no-restore
```

Expected: both commands pass.

