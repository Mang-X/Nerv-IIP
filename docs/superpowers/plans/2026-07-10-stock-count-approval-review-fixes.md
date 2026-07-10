# Stock Count Approval Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Inventory approval-completion consumer persist approved and rejected stock-count adjustments through the established command unit-of-work path.

**Architecture:** The CAP consumer remains responsible for validating the approval envelope and routing only Inventory `inventory-count-variance` documents. It sends a new internal command that loads the pending adjustment, task, and ledger in the application layer; the existing command pipeline persists state and dispatches the resulting domain event. Duplicate delivery becomes a no-op after the adjustment leaves `pending-approval`.

**Tech Stack:** .NET 10, CleanDDD command handlers, MediatR `ISender`, EF Core, CAP, xUnit.

## Global Constraints

- Keep the change inside Inventory and retain actual-ledger invariants.
- Do not add or change HTTP endpoints, schemas, OpenAPI snapshots, or generated clients.
- Use async EF Core APIs and let the command unit-of-work own persistence and domain-event dispatch.
- Preserve the existing `IntegrationEventConsumerGuard` source/type/version validation and dead-letter behavior.

---

### Task 1: Capture the real CAP consumer path

**Files:**
- Modify: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/StockCountApprovalTests.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/ApprovalCompletedIntegrationEventHandlerForStockCountAdjustment.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CompleteStockCountAdjustmentApprovalCommand.cs`

**Interfaces:**
- Consumes: `ApprovalCompletedIntegrationEvent`, `ISender`, `ApplicationDbContext`.
- Produces: `CompleteStockCountAdjustmentApprovalCommand` with organization ID, environment ID, count-task code, approval-chain ID, and completion result.

- [ ] **Step 1: Write failing consumer tests**

Replace direct `DbContext` persistence in the approval-completion tests with a command-executing sender. Assert approved delivery changes the persisted adjustment to `posted`, creates one movement, unfreezes the ledger, and changes the task to `confirmed`; assert rejected/returned delivery voids the adjustment, leaves on-hand unchanged, unfreezes the ledger, and changes the task to `recount-required`.

- [ ] **Step 2: Run the targeted test to verify it fails**

Run: `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --filter FullyQualifiedName~StockCountApprovalTests`

Expected: FAIL because the existing consumer has no `ISender` dependency and does not issue a command that owns persistence.

- [ ] **Step 3: Add the completion command and route the consumer through it**

Define `CompleteStockCountAdjustmentApprovalCommand : ICommand<CompleteStockCountAdjustmentApprovalResult>`. Its handler selects the adjustment by organization, environment, count-task code, and chain ID; returns a no-op result unless it is `pending-approval`; loads the exact ledger dimensions; approves by calling `ConfirmApprovedAdjustment`, adding the movement, and calling `MarkPosted`; rejects/returns by calling `RequireRecountAfterApprovalRejection` and `VoidAfterApprovalRejection`. The CAP handler validates source/document and invokes `sender.Send(...)`.

- [ ] **Step 4: Run the targeted test to verify it passes**

Run: `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --filter FullyQualifiedName~StockCountApprovalTests`

Expected: PASS with persisted approved and rejected outcomes through the command-executing sender.

### Task 2: Eliminate the approval-client silent fallback

**Files:**
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Approval/StockCountApprovalClient.cs`
- Modify: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`

**Interfaces:**
- Consumes: required `IStockCountApprovalClient` DI registration from `Program.cs`.
- Produces: fail-fast construction when a caller tries to create an above-threshold handler without an approval client.

- [ ] **Step 1: Write a failing constructor/above-threshold test**

Add or adjust a test so a handler configured to require approval must receive an `IStockCountApprovalClient`; the test must not accept a fabricated approval-chain ID.

- [ ] **Step 2: Run the target test to verify it fails**

Run: `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --filter FullyQualifiedName~InventoryEndpointContractTests`

Expected: FAIL while `GeneratedStockCountApprovalClient` still fabricates a chain ID.

- [ ] **Step 3: Make the client dependency required and delete the stub**

Require `IStockCountApprovalClient` in `ConfirmStockCountAdjustmentCommandHandler`; retain the optional options parameter only where test construction needs it. Delete `GeneratedStockCountApprovalClient` and update direct test construction to inject a real test double.

- [ ] **Step 4: Run focused command tests**

Run: `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --filter "FullyQualifiedName~StockCountApprovalTests|FullyQualifiedName~InventoryEndpointContractTests"`

Expected: PASS without a production fallback path.

### Task 3: Restore the existing count-adjustment fact invariant and verify

**Files:**
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountAdjustmentAggregate/StockCountAdjustment.cs`
- Modify: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs` only if the invariant test needs correction.

**Interfaces:**
- Consumes: `StockCountAdjustment.Record(StockCountTask, StockMovement, string)`.
- Produces: posted adjustment facts reject a movement without an assigned identifier; pending approval facts remain the only null-movement state.

- [ ] **Step 1: Run the failing CI-domain test locally**

Run: `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --filter FullyQualifiedName~Count_adjustment_fact_requires_assigned_movement_id`

Expected: FAIL because the branch currently permits `Record` to accept a movement whose ID has not been assigned.

- [ ] **Step 2: Restore the minimal invariant**

Make `StockCountAdjustment.Record` reject `movement.Id is null` before constructing a posted fact. Do not alter the pending-approval factory, which intentionally has no movement.

- [ ] **Step 3: Run focused and required regression gates**

Run: `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --filter FullyQualifiedName~Count_adjustment_fact_requires_assigned_movement_id`

Run: `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj`

Run: `dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj`

Expected: all tests pass; no OpenAPI, schema, or frontend regeneration is required because this follow-up changes no public endpoint or schema.
