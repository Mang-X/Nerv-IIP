# Business WMS Execution MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build WMS MVP covering inbound, putaway, outbound, picking, pack review, count execution and WCS adapter task boundary.

**Architecture:** WMS owns warehouse execution state, not stock balance. Completed inbound and outbound work emits events or calls Inventory to post stock movements with idempotency keys. WCS is not implemented as a separate system; WMS owns adapter task mapping, external task identity, callback result and retry diagnostics.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal integration events, xUnit.

---

## Boundaries

1. WMS does not store stock balance fields.
2. WMS does not own purchase order, sales order or work order business state.
3. WCS adapter tasks are async and compensatable; external WCS is not part of the transaction.
4. Barcode scanning may be recorded through BarcodeLabel but WMS keeps its own execution facts.

## File Structure Map

```text
backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/
  AggregatesModel/InboundOrderAggregate/InboundOrder.cs
  AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs
  AggregatesModel/WarehouseTaskAggregate/WarehouseTask.cs
  AggregatesModel/WcsTaskAggregate/WcsTask.cs
  AggregatesModel/CountExecutionAggregate/CountExecution.cs

backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/
  Application/Commands/CreateInboundOrderCommand.cs
  Application/Commands/CompleteInboundOrderCommand.cs
  Application/Commands/CreateOutboundOrderCommand.cs
  Application/Commands/CompleteOutboundOrderCommand.cs
  Application/Commands/DispatchWcsTaskCommand.cs
  Application/Commands/CompleteWcsTaskCommand.cs
  Application/IntegrationEvents/WmsIntegrationEvents.cs
  Endpoints/Wms/WmsEndpoints.cs
```

## Task 1: Scaffold WMS Service

**Files:**

- Create: `backend/services/Business/Wms/*`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create service and tests**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Wms -o backend/services/Business/Wms --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Wms.Domain.Tests -o backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Wms.Web.Tests -o backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/Nerv.IIP.Business.Wms.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Nerv.IIP.Business.Wms.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Nerv.IIP.Business.Wms.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj
```

- [ ] **Step 2: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Wms
git commit -m "feat: scaffold wms service"
```

## Task 2: Implement Inbound and Outbound Execution

**Files:**

- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/InboundOrderAggregate/InboundOrder.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/WarehouseTaskAggregate/WarehouseTask.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WmsExecutionAggregateTests.cs`

- [ ] **Step 1: Write failing execution tests**

Cover:

```csharp
var inbound = InboundOrder.Create("org-001", "env-dev", "purchase-receipt", "PR-001", new[] { InboundLine.Create("SKU-RM-1000", 19m) });
inbound.CreatePutawayTask("SKU-RM-1000", 19m, "A-01-01");
inbound.Complete(new[] { PutawayResult.Create("SKU-RM-1000", 19m, "A-01-01") }, "idem-in-001");

var outbound = OutboundOrder.Create("org-001", "env-dev", "sales-delivery", "DO-001", new[] { OutboundLine.Create("SKU-FG-1000", 2m) });
outbound.Pick("SKU-FG-1000", 2m, "FG-01-01", "LOT-001");
outbound.CompletePackReview("pack-ok", "idem-out-001");
```

Assert completion requires idempotency key, picked quantity cannot exceed requested quantity, and completed orders are immutable.

- [ ] **Step 2: Implement events**

Create `InboundOrderCompletedDomainEvent`, `OutboundOrderCompletedDomainEvent`, `WarehouseTaskAssignedDomainEvent` and `CountExecutionCompletedDomainEvent`.

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore
git add backend/services/Business/Wms
git commit -m "feat: add wms warehouse execution model"
```

Expected: tests pass before commit.

## Task 3: Implement WCS Adapter Boundary

**Files:**

- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/WcsTaskAggregate/WcsTask.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WcsTaskAggregateTests.cs`

- [ ] **Step 1: Write failing WCS tests**

Cover:

```csharp
var task = WcsTask.Dispatch("org-001", "env-dev", "warehouse-task-001", "asrs", """{"source":"A-01-01","target":"STAGE-01"}""");
task.MarkCompleted("external-001", DateTimeOffset.UtcNow);
```

Assert dispatch is idempotent by warehouse task and adapter type, failed task stores diagnostic code and message, and completed task cannot be failed later.

- [ ] **Step 2: Implement WCS events**

Create `WcsTaskDispatchedDomainEvent`, `WcsTaskCompletedDomainEvent` and `WcsTaskFailedDomainEvent`.

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~WcsTaskAggregateTests
git add backend/services/Business/Wms
git commit -m "feat: add wms wcs adapter task boundary"
```

Expected: tests pass before commit.

## Task 4: Add Persistence, API, Events and Permissions

**Files:**

- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/*.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Queries/*.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEvents/WmsIntegrationEvents.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Endpoints/Wms/WmsEndpoints.cs`
- Create: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsEndpointTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Configure schema**

Use schema `wms`. Tables include `inbound_orders`, `outbound_orders`, `warehouse_tasks`, `wcs_tasks`, `count_executions`. No table contains `on_hand_quantity`, `available_quantity` or `stock_balance`.

- [ ] **Step 2: Add routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/wms/inbound-orders` | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/complete` | `business.wms.receipts.manage` |
| `GET /api/business/v1/wms/inbound-orders` | `business.wms.receipts.read` |
| `POST /api/business/v1/wms/outbound-orders` | `business.wms.shipments.manage` |
| `POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/complete` | `business.wms.shipments.manage` |
| `GET /api/business/v1/wms/outbound-orders` | `business.wms.shipments.read` |
| `POST /api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch` | `business.wms.automation.manage` |
| `POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/complete` | `business.wms.automation.manage` |

- [ ] **Step 3: Seed permissions and run tests**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

Expected: PASS.

- [ ] **Step 4: Commit API**

Run:

```powershell
git add backend/services/Business/Wms backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md
git commit -m "feat: expose wms execution api"
```

## Task 5: Add Verification and Readiness

**Files:**

- Create: `scripts/verify-business-wms-execution-mvp.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Add verification script and run**

Run:

```powershell
scripts/verify-business-wms-execution-mvp.ps1
git diff --check
```

Expected: script runs all WMS tests and exits `0`.

- [ ] **Step 2: Commit docs**

Run:

```powershell
git add scripts/verify-business-wms-execution-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record wms execution readiness"
```

## Self-Review Checklist

1. WMS stores execution state only.
2. Completed inbound/outbound operations carry idempotency keys.
3. WCS adapter failures are diagnosable and compensatable.
4. Inventory balance still belongs only to Inventory.
