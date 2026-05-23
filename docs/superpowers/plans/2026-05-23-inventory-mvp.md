# Inventory MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #131 by creating the Inventory service for stock locations, stock ledger, stock movements, availability queries and count adjustments.

**Architecture:** Inventory is a new CleanDDD business service under `backend/services/Business/Inventory`. It owns stock facts only and references MasterData by public codes or IDs. WMS, ERP, MES and DemandPlanning consume Inventory through APIs/events and never through shared tables.

**Tech Stack:** .NET 10, NetCorePal CleanDDD template, FastEndpoints, EF Core PostgreSQL, xUnit, CAP-style integration event conversion, `Nerv.IIP.Testing` schema convention helpers.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-inventory-mvp-design.md` as the domain contract for this plan.

## Files

- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/Nerv.IIP.Business.Inventory.Domain.csproj`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Nerv.IIP.Business.Inventory.Infrastructure.csproj`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLocationAggregate/StockLocation.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockMovementAggregate/StockMovement.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountTask.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/DomainEvents/InventoryDomainEvents.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockLocationEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockLedgerEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockMovementEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockCountTaskEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Auth/InventoryPermissionCodes.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockLocations/CreateStockLocationCommand.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockMovements/PostStockMovementCommand.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CreateStockCountTaskCommand.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/GetStockAvailabilityQuery.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEvents/InventoryIntegrationEvents.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventConverters/InventoryIntegrationEventConverters.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Endpoints/Inventory/InventoryEndpoints.cs`
- Create: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`
- Create: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`
- Create: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryIntegrationEventTests.cs`
- Create: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventorySchemaConventionTests.cs`

Shared files requested from #140:

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-inventory-mvp.ps1`

## Task 1: Scaffold Inventory Service Locally

- [ ] **Step 1: Create service projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Inventory -o backend/services/Business/Inventory --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Inventory.Domain.Tests -o backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Inventory.Web.Tests -o backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests --framework net10.0
```

Expected: Inventory Domain, Infrastructure, Web and test projects exist.

- [ ] **Step 2: Remove template demo code**

Delete template demo endpoints, sample aggregates, sample migrations, demo SignalR hubs and demo tests. Verify no file contains `OrderAggregate`, `DeliverRecord`, `LoginEndpoint`, `ChatHub` or `LockEndpoint`.

Run:

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/Inventory
```

Expected: no matches.

## Task 2: Implement Domain Model

- [ ] **Step 1: Write aggregate tests**

Create `InventoryAggregateTests.cs` with tests for:

1. Posting inbound movement increases on-hand quantity.
2. Posting outbound movement decreases on-hand quantity.
3. Duplicate idempotency key with the same payload returns the existing movement.
4. Duplicate idempotency key with different payload is rejected.
5. Outbound movement that would make on-hand negative is rejected.
6. Count adjustment creates an adjustment movement and updates ledger quantity.

Run:

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore
```

Expected before implementation: compile failure because Inventory aggregates do not exist.

- [ ] **Step 2: Implement aggregate roots**

Implement the aggregate files listed in the Files section. Required methods:

1. `StockLocation.CreateOrUpdate(...)`
2. `StockLedger.ApplyMovement(...)`
3. `StockMovement.Post(...)`
4. `StockCountTask.Create(...)`
5. `StockCountTask.ConfirmAdjustment(...)`

Use `Guid.CreateVersion7()` for entity IDs and keep all methods deterministic for unit tests.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore
```

Expected: Inventory domain tests pass.

## Task 3: Add Persistence And Events

- [ ] **Step 1: Configure DbContext**

Create `ApplicationDbContext.cs` and entity configurations using schema `inventory`. Configure `MigrationsHistoryTable("__EFMigrationsHistory", "inventory")`.

- [ ] **Step 2: Generate migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialInventorySchema --project backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Nerv.IIP.Business.Inventory.Infrastructure.csproj --startup-project backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj --output-dir Migrations
```

Expected: initial Inventory migration is created.

- [ ] **Step 3: Add event converter tests**

Create `InventoryIntegrationEventTests.cs` and verify event names:

1. `inventory.StockMovementPosted`
2. `inventory.StockCountVarianceConfirmed`
3. `inventory.StockAvailabilityChanged`

Run:

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore --filter FullyQualifiedName~InventoryIntegrationEventTests
```

Expected: event converter tests pass.

## Task 4: Add API Surface

- [ ] **Step 1: Add endpoint contract tests**

Create `InventoryEndpointContractTests.cs` for:

1. Internal authorization is required for all non-health endpoints.
2. `POST /api/inventory/v1/locations` creates a location.
3. `POST /api/inventory/v1/movements` posts a movement and returns movement ID.
4. `GET /api/inventory/v1/availability` returns on-hand, reserved and available.
5. `POST /api/inventory/v1/count-tasks` creates a count task.
6. `POST /api/inventory/v1/count-tasks/{countTaskId}/adjustments` posts adjustments.
7. OpenAPI operation IDs are stable.

- [ ] **Step 2: Implement commands, queries and FastEndpoints**

Implement files listed in the Files section. Use permission codes from the Inventory spec and `[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]` for internal APIs.

- [ ] **Step 3: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore
```

Expected: Inventory Web tests pass.

## Task 5: Handoff Shared Changes To #140

- [ ] **Step 1: Record shared changes**

In the PR body for this session, include:

```markdown
## Shared Changes Needed

- Add Inventory projects/tests to `backend/Nerv.IIP.sln`.
- Register Inventory in AppHost.
- Add Inventory permissions to IAM seed and `authorization-matrix.md`.
- Add `inventory` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-inventory-mvp.ps1`.
```

- [ ] **Step 2: Run final focused verification**

Run:

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore
```

Expected: both commands pass.
