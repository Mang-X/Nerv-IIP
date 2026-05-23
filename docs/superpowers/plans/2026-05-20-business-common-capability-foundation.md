# Business Common Capability Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the common business capabilities needed by later ERP, WMS and MES slices: Inventory, Quality, BarcodeLabel and BusinessApproval.

**Architecture:** Implement four focused Business services under `backend/services/Business`: Inventory owns stock balance and stock movement facts; Quality owns inspection and nonconformance facts; BarcodeLabel owns label and scan facts; Approval owns business approval chains. These services may exchange integration events and stable document references, but never share a database schema.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal integration events, xUnit.

---

## Scope

This is a multi-service foundation plan because the four services are tightly coupled prerequisites for WMS, MES and ERP but still maintain separate persistence boundaries.

This plan depends on `docs/superpowers/plans/2026-05-21-business-master-data-realignment.md`. Inventory must consume MasterData SKU traceability policy and UOM conversion; Quality must consume SKU, partner, device/work-center and reusable characteristic definitions without owning SKU or partner master facts; BarcodeLabel must consume SKU and default barcode policy; BusinessApproval may reference business organization attributes but must not copy IAM roles or permissions.

## Boundaries

1. Inventory is the only owner of stock balance and stock movement.
2. WMS and MES execution steps are not implemented here.
3. BusinessApproval does not replace Ops approval.
4. Quality does not directly change stock balance; it emits inspection results consumed by WMS or Inventory.
5. BarcodeLabel does not own business document status.
6. Inventory owns actual lot, serial, heat, expiry, location status and stock movement facts; MasterData owns SKU traceability policy and UOM rules.
7. Quality owns inspection standards, records, COA, nonconformance and release decisions; MasterData owns reusable reference definitions only when they are cross-domain.

## File Structure Map

```text
backend/services/Business/Inventory/
backend/services/Business/Quality/
backend/services/Business/BarcodeLabel/
backend/services/Business/Approval/

Each service:
  src/Nerv.IIP.Business.{Context}.Domain/
  src/Nerv.IIP.Business.{Context}.Infrastructure/
  src/Nerv.IIP.Business.{Context}.Web/
  tests/Nerv.IIP.Business.{Context}.Domain.Tests/
  tests/Nerv.IIP.Business.{Context}.Web.Tests/
```

## Task 1: Scaffold Four Common Capability Services

**Files:**

- Create: `backend/services/Business/Inventory/*`
- Create: `backend/services/Business/Quality/*`
- Create: `backend/services/Business/BarcodeLabel/*`
- Create: `backend/services/Business/Approval/*`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create service and test projects**

Run once per context with the exact context names `Inventory`, `Quality`, `BarcodeLabel`, `Approval`:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Inventory -o backend/services/Business/Inventory --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Inventory.Domain.Tests -o backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Inventory.Web.Tests -o backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests --framework net10.0
dotnet new netcorepal-web -n Nerv.IIP.Business.Quality -o backend/services/Business/Quality --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Quality.Domain.Tests -o backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Quality.Web.Tests -o backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests --framework net10.0
dotnet new netcorepal-web -n Nerv.IIP.Business.BarcodeLabel -o backend/services/Business/BarcodeLabel --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.BarcodeLabel.Domain.Tests -o backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.BarcodeLabel.Web.Tests -o backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests --framework net10.0
dotnet new netcorepal-web -n Nerv.IIP.Business.Approval -o backend/services/Business/Approval --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Approval.Domain.Tests -o backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Approval.Web.Tests -o backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests --framework net10.0
```

- [ ] **Step 2: Add all projects to the solution**

Run:

```powershell
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/Nerv.IIP.Business.Inventory.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Infrastructure/Nerv.IIP.Business.Inventory.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Nerv.IIP.Business.Inventory.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/Nerv.IIP.Business.Quality.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Nerv.IIP.Business.Quality.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Nerv.IIP.Business.Quality.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/Nerv.IIP.Business.BarcodeLabel.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Nerv.IIP.Business.BarcodeLabel.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Nerv.IIP.Business.BarcodeLabel.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/Nerv.IIP.Business.Approval.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Nerv.IIP.Business.Approval.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/Nerv.IIP.Business.Approval.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/Nerv.IIP.Business.Approval.Web.Tests.csproj
```

- [ ] **Step 3: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Inventory backend/services/Business/Quality backend/services/Business/BarcodeLabel backend/services/Business/Approval
git commit -m "feat: scaffold business common capability services"
```

## Task 2: Implement Inventory Stock Facts

**Files:**

- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLocationAggregate/StockLocation.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockLedgerAggregate/StockLedger.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockMovementAggregate/StockMovement.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Domain/AggregatesModel/StockCountTaskAggregate/StockCountTask.cs`
- Create: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/InventoryAggregateTests.cs`

- [ ] **Step 1: Write failing inventory tests**

Cover:

```csharp
StockLocation.Create("org-001", "env-dev", "WH-A", "A-01-01");
StockMovement.Post("org-001", "env-dev", "receipt", "SKU-RM-1000", 10m, "WH-A", "A-01-01", "PO-1000", "idem-001");
StockLedger.ApplyMovement(movement);
StockCountTask.Create("org-001", "env-dev", "COUNT-001", "WH-A").ConfirmVariance("SKU-RM-1000", "A-01-01", 8m, 10m, "approval-chain-001");
```

Assert that `idempotencyKey` is required, negative movement quantity is rejected, and available quantity never becomes negative.

- [ ] **Step 2: Implement domain and events**

Create `StockMovementPostedDomainEvent`, `StockCountTaskCreatedDomainEvent` and `StockCountVarianceConfirmedDomainEvent`. `StockLedger` exposes `OnHandQuantity`, `AvailableQuantity` and `FrozenQuantity`.

- [ ] **Step 3: Add Inventory persistence and API**

Use schema `inventory` and routes:

| Route | Permission |
| --- | --- |
| `POST /api/inventory/v1/locations` | `business.inventory.locations.manage` |
| `POST /api/inventory/v1/movements` | `business.inventory.movements.create` |
| `GET /api/inventory/v1/availability` | `business.inventory.ledger.read` |
| `POST /api/inventory/v1/count-tasks` | `business.inventory.counts.manage` |
| `POST /api/inventory/v1/count-tasks/{countTaskId}/adjustments` | `business.inventory.counts.manage` |

Run:

```powershell
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj --no-restore
```

Expected: PASS.

- [ ] **Step 4: Commit Inventory**

Run:

```powershell
git add backend/services/Business/Inventory docs/architecture/database-schema-catalog.md
git commit -m "feat: add inventory stock facts"
```

## Task 3: Implement Quality Inspection Facts

**Files:**

- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionPlanAggregate/InspectionPlan.cs`
- Create: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/AggregatesModel/InspectionRecordAggregate/InspectionRecord.cs`
- Create: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/QualityAggregateTests.cs`

- [ ] **Step 1: Write failing quality tests**

Cover:

```csharp
var plan = InspectionPlan.Create("org-001", "env-dev", "receiving", "PO-RECEIPT-001", "SKU-RM-1000");
var record = plan.RecordResult("inspector-001", "passed", 10m, 0m, Array.Empty<string>());
```

Assert that inspection result is one of `passed`, `rejected`, `conditional-release`; rejected records require a disposition reason; attachment IDs are file references only.

- [ ] **Step 2: Implement schema, events and endpoints**

Use schema `quality`, events `InspectionPassedDomainEvent`, `InspectionRejectedDomainEvent`, `NonconformanceDispositionCompletedDomainEvent`, and routes:

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/quality/inspection-plans` | `business.quality.inspection-plans.manage` |
| `POST /api/business/v1/quality/inspection-records` | `business.quality.inspection-records.create` |
| `GET /api/business/v1/quality/inspection-records` | `business.quality.inspection-records.read` |

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj --no-restore
git add backend/services/Business/Quality docs/architecture/database-schema-catalog.md
git commit -m "feat: add quality inspection facts"
```

Expected: tests pass before commit.

## Task 4: Implement Barcode and Approval Facts

**Files:**

- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/BarcodeRuleAggregate/BarcodeRule.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelPrintBatchAggregate/LabelPrintBatch.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/ScanRecordAggregate/ScanRecord.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalTemplateAggregate/ApprovalTemplate.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`

- [ ] **Step 1: Write failing tests**

Barcode tests cover deterministic label generation from a template, scan idempotency by source device and idempotency key, and rejection of blank barcode values. Approval tests cover chain creation, ordered step approval, rejection with comment and prevention of duplicate approver actions.

- [ ] **Step 2: Implement schemas and endpoints**

Use schemas `barcode` and `business_approval`. Add routes:

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/barcodes/templates` | `business.barcodes.templates.manage` |
| `POST /api/business/v1/barcodes/print-batches` | `business.barcodes.print` |
| `POST /api/business/v1/barcodes/scans` | `business.barcodes.scans.write` |
| `POST /api/business/v1/approvals/chains` | `business.approvals.manage` |
| `POST /api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/resolve` | `business.approvals.manage` |
| `GET /api/business/v1/approvals/chains/{chainId}` | `business.approvals.read` |

- [ ] **Step 3: Run tests and commit**

Run:

```powershell
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/Nerv.IIP.Business.Approval.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/Nerv.IIP.Business.Approval.Web.Tests.csproj --no-restore
git add backend/services/Business/BarcodeLabel backend/services/Business/Approval docs/architecture/database-schema-catalog.md
git commit -m "feat: add barcode and business approval capabilities"
```

Expected: all tests pass.

## Task 5: Seed Permissions and Add Verification

**Files:**

- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Create: `scripts/verify-business-common-capability-foundation.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Seed permissions**

Add permissions from `business.inventory.*`, `business.quality.*`, `business.barcodes.*` and `business.approvals.*` listed in `docs/architecture/authorization-matrix.md` to the IAM seed admin role.

- [ ] **Step 2: Create verification script**

The script runs all Domain and Web tests under `Inventory`, `Quality`, `BarcodeLabel` and `Approval`, then runs IAM seed tests.

- [ ] **Step 3: Run final verification**

Run:

```powershell
scripts/verify-business-common-capability-foundation.ps1
git diff --check
```

Expected: both commands exit `0`.

- [ ] **Step 4: Commit verification**

Run:

```powershell
git add backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs scripts/verify-business-common-capability-foundation.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "docs: record common capability readiness"
```

## Self-Review Checklist

1. Inventory is the only service with stock balance fields.
2. Quality results do not mutate stock balance directly.
3. Barcode scan and inventory movement commands require idempotency keys.
4. Approval service documentation clearly says it is for business documents, not Ops tasks.
