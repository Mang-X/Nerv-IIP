# Inventory Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Address PR #422 review findings with code-fact-based fixes for Inventory consumer reliability, count freeze lifecycle, status-transfer safety, valuation consistency, and public contract compatibility.

**Architecture:** Keep Inventory ownership inside the Inventory service. Quality-to-Inventory automation uses only the public Quality integration event and Inventory ledgers, guarded by the shared CAP consumer reliability layer. Count concurrency uses explicit freeze/cancel lifecycle, with recount as a structured domain result instead of message-text control flow.

**Tech Stack:** .NET 10, FastEndpoints, EF Core, NetCorePal CleanDDD, CAP integration events, xUnit.

---

### Task 1: Quality Consumer Guard And Retry Idempotency

**Files:**
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForStockStatusTransfer.cs`
- Test: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryMovementRequestedConsumerTests.cs`

- [x] Add failing tests proving unsupported Quality event types are rejected into `IIntegrationEventDeadLetterStore` and no command is sent.
- [x] Add failing test proving a redelivered Quality event with existing `status-transfer-*` movements returns before ledger candidate lookup.
- [x] Implement `IntegrationEventConsumerGuard<InspectionResultIntegrationEvent>` with supported V1 passed/rejected event types.
- [x] Add pre-candidate idempotency check against `StockMovements` by `IdempotencyKey:out` and `IdempotencyKey:in`.

### Task 2: Count Freeze Lifecycle

**Files:**
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountTask.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountRecountRequiredException.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/CancelStockCountTaskCommand.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockCounts/ConfirmStockCountAdjustmentCommand.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Endpoints/Inventory/InventoryEndpoints.cs`
- Test: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`
- Test: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`

- [x] Add failing domain test proving cancel changes the task to `cancelled` and releases the ledger freeze.
- [x] Add failing command/contract tests for `POST /api/inventory/v1/count-tasks/{countTaskId}/cancel`.
- [x] Replace message-text recount handling with `StockCountRecountRequiredException`.
- [x] Implement cancel command and endpoint using the existing CountsManage permission.

### Task 3: Status Transfer Safety

**Files:**
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Commands/StockStatusTransfers/PostStockStatusTransferCommand.cs`
- Test: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryEndpointContractTests.cs`

- [x] Add failing tests proving status transfer rejects quantities above `AvailableQuantity`.
- [x] Add failing tests proving frozen source ledgers return `KnownException` instead of an uncaught invalid operation.
- [x] Add available-quantity guard and translate domain invalid-operation failures to `KnownException`.

### Task 4: Valuation And Contract Compatibility

**Files:**
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Inventory/InventoryIntegrationEvents.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/IntegrationEventConverters/InventoryIntegrationEventConverters.cs`
- Modify: affected tests that construct positional payloads.

- [x] Add failing domain test proving outbound movement ignores external unit cost and uses current moving average.
- [x] Move newly added payload cost/value fields to the end of positional records.
- [x] Update constructors and event converters to match the safer positional order.

### Task 5: Verification And PR Update

- [x] Run focused Inventory Domain/Web tests.
- [x] Run governed `pwsh scripts/verify-business-inventory-mvp.ps1`.
- [x] Run `git diff --check`.
- [ ] Commit and push to PR branch `codex/issue-412-inventory-business-gap`.
- [ ] Reply to review threads with concise code-fact outcomes.
