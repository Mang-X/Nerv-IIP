# WMS Business Gap #413 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close WMS posting-failure and task-execution loops for #413 while preserving the Inventory public-contract boundary.

**Architecture:** Inventory emits a public failed-posting integration event for valid movement requests that are rejected by Inventory business rules. WMS consumes that event to mark request/order status, and exposes task execution endpoints over existing WarehouseTask domain behavior. After rebasing onto #412, Inventory reservation APIs are available; WMS now reserves stock when creating outbound picking tasks and carries the reservation id into movement-requested so Inventory allocates during outbound posting. FEFO/FIFO, ASN strategy, directed putaway, LPN/HU and reservation release/cancel compensation remain documented public-contract follow-ups.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, EF Core, CAP integration events, xUnit.

---

## Tasks

- [x] Add `StockMovementPostingFailedIntegrationEvent` to `Nerv.IIP.Contracts.Inventory` and focused contract tests.
- [x] Catch business posting rejection in `InventoryMovementRequestedIntegrationEventHandlerForPostingMovement`, publish the failed event, and keep envelope validation failures on the existing DLQ path.
- [x] Add WMS command/consumer tests for `inventory.StockMovementPostingFailed`.
- [x] Implement WMS failed-request command and consumer; move inbound/outbound orders to `InventoryPostingFailed` when the request references them.
- [x] Add WMS task execution contract tests for progress and completion endpoints.
- [x] Implement `RecordWarehouseTaskProgressCommand`, `CompleteWarehouseTaskCommand`, endpoints and operation IDs.
- [x] Scope WCS complete/fail commands by organization and environment.
- [x] Rebase onto the Inventory #412 reservation model and add WMS-to-Inventory reservation client coverage for outbound picking.
- [x] Persist Inventory reservation id on WMS outbound lines and movement requests, and propagate it through `inventory.InventoryMovementRequested`.
- [x] Add Inventory consumer coverage proving outbound movement requests allocate the supplied reservation id.
- [x] Update readiness/docs to reflect the delivered slice and explicit deferred public contracts for reservation release/cancel compensation, FEFO, ASN, directed putaway and LPN/HU.
- [x] Run focused Inventory/WMS tests and final repository checks before committing.
