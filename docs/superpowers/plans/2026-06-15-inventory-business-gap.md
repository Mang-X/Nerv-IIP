# Inventory Business Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close #412 by adding Inventory-owned stock status, reservation/allocation, valuation, count safety, and Quality event release behavior.

**Architecture:** Keep all stock facts in `Business.Inventory`. Use public Quality contracts for inspection result consumption. Do not reference WMS, MES, ERP, or Quality Domain/Infrastructure/Web projects from Inventory.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, EF Core PostgreSQL, CAP consumers, xUnit, `Nerv.IIP.Testing` schema convention helpers.

---

### Task 1: Domain Rules

**Files:**
- Modify: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockQualityStatus.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockReservationAggregate/StockReservation.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockMovementAggregate/StockMovement.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountTask.cs`

- [ ] Write failing tests for stock status normalization/rejection, reservation/release/allocation, moving-average valuation, and count freeze/version checks.
- [ ] Run `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore` and confirm the new tests fail.
- [ ] Implement minimal domain code to pass.
- [ ] Re-run the same domain test project and confirm it passes.

### Task 2: Commands, Queries, And Endpoints

**Files:**
- Modify: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Endpoints/Inventory/InventoryEndpoints.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Auth/InventoryPermissionCodes.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockMovements/PostStockMovementCommand.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockReservations/ReserveStockCommand.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockReservations/ReleaseStockReservationCommand.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/GetStockAvailabilityQuery.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CreateStockCountTaskCommand.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs`

- [ ] Write failing web tests for reservation endpoints, status-transfer endpoint, valuation response fields, count stale-version rejection, and endpoint contract metadata.
- [ ] Run focused web tests and confirm failures.
- [ ] Implement command handlers and endpoints using async EF Core queries with cancellation tokens.
- [ ] Re-run focused web tests and confirm they pass.

### Task 3: Quality Event Consumer

**Files:**
- Modify: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryMovementRequestedConsumerTests.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs`

- [ ] Write failing consumer tests for `quality.InspectionPassed` and `quality.InspectionRejected`.
- [ ] Implement a CAP consumer against `InspectionResultIntegrationEvent` from `Nerv.IIP.Contracts.Quality`.
- [ ] Re-run consumer tests.

### Task 4: Persistence And Docs

**Files:**
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/EntityConfigurations/StockReservationEntityTypeConfiguration.cs`
- Modify: existing Inventory EF configurations
- Generate: new Inventory EF migration and model snapshot
- Modify: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventorySchemaConventionTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] Write/update schema convention tests for new table/columns/check constraints.
- [ ] Generate EF migration with PostgreSQL profile.
- [ ] Update schema catalog/readiness with #412 behavior.
- [ ] Run Inventory web tests and schema tests.

### Task 5: Verification And PR

- [ ] Run `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore`.
- [ ] Run `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore`.
- [ ] Run `scripts/verify-business-inventory-mvp.ps1` if local prerequisites allow it.
- [ ] Run `git diff --check`.
- [ ] Commit, push `codex/issue-412-inventory-business-gap`, and create a PR with `Closes #412`.
