# BarcodeLabel Business Scan GS1 Traceability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close #418 by adding GS1 parsing, serialized EPCIS traceability and a real inventory scan business-action route to BarcodeLabel.

**Architecture:** BarcodeLabel keeps ownership of label and scan facts, publishes a shared barcode scan envelope, and translates explicitly-supported inventory workflows into Inventory's existing movement-requested integration event. Inventory remains the inventory fact owner; no UI-only flow or cross-schema coupling is introduced.

**Tech Stack:** .NET 10, CleanDDD/NetCorePal, FastEndpoints, EF Core PostgreSQL, CAP integration events, xUnit, `Nerv.IIP.Contracts.Inventory`.

---

## Specification

Use `docs/superpowers/specs/2026-06-15-barcode-label-business-scan-gs1-traceability-design.md`.

## Tasks

### Task 1: Shared Barcode Contracts

- [ ] Create `backend/common/Contracts/Nerv.IIP.Contracts.BarcodeLabel` with `BarcodeScanAcceptedIntegrationEvent`, event type constants and payload records.
- [ ] Add the project to `backend/Nerv.IIP.sln`.
- [ ] Reference the contract from BarcodeLabel Web tests and Web project.
- [ ] Add tests verifying event type, version and envelope fields.

### Task 2: GS1 Domain Model

- [ ] Add failing domain tests for GS1 mod-10, GS1 AI parsing and serialized label generation.
- [ ] Implement `Gs1BarcodeValue`, `Gs1ApplicationIdentifierParser` and GS1 helpers under BarcodeLabel Domain.
- [ ] Extend `BarcodeRule` to support `gs1-128`, `gs1-datamatrix` and `gs1-mod10`.
- [ ] Extend `LabelPrintItem` and print batch creation to persist `gtin`, `lotNo`, `serialNumber` and `epcUri`.
- [ ] Run BarcodeLabel Domain tests and keep existing deterministic custom-code tests green.

### Task 3: EPCIS Persistence

- [ ] Add failing tests for commissioning and object-event EPCIS facts.
- [ ] Add `EpcisEvent` aggregate/entity and `DbSet`.
- [ ] Configure `epcis_events` and new label/scan columns with comments and indexes.
- [ ] Add EF migration and update `docs/architecture/database-schema-catalog.md`.
- [ ] Run schema convention tests.

### Task 4: Scan Command Routing

- [ ] Add failing Web command tests for accepted `inventory.receipt` GS1 scans publishing `InventoryMovementRequestedIntegrationEvent`.
- [ ] Extend `RecordScanCommand` and endpoint request with optional inventory context: `SkuCode`, `UomCode`, `SiteCode`, `LocationCode`, `QualityStatus`, `OwnerType`, `OwnerId`, `Quantity`.
- [ ] Parse GS1 data into scan record fields.
- [ ] Publish shared `BarcodeScanAcceptedIntegrationEvent` for accepted scans.
- [ ] Publish `InventoryMovementRequestedIntegrationEvent` only for supported inventory workflows.
- [ ] Reject unsupported accepted workflows and missing inventory context with `KnownException`.
- [ ] Preserve rejected scan logging without downstream business action events.

### Task 5: API, Docs And Verification

- [ ] Update BarcodeLabel endpoint contract tests for new request fields without changing route shape.
- [ ] Update `docs/architecture/business-platform-domain-architecture.md`, `docs/architecture/api-contract-and-codegen.md` and `docs/architecture/implementation-readiness.md`.
- [ ] Run:
  - `dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --no-restore`
  - `dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --no-restore`
  - `dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore --filter FullyQualifiedName~InventoryMovementRequestedConsumerTests`
  - `pwsh scripts/verify-business-barcode-label-mvp.ps1`

## Self-Review

Spec coverage: all #418 requirements map to tasks. Placeholder scan: none. Type consistency: event and command names match existing service conventions and the Inventory movement-requested contract.
