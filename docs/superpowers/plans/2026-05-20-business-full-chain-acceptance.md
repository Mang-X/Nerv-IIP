# Business Full Chain Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add end-to-end acceptance coverage for the seven critical Business Platform chains after Slice 1 through Slice 9 are implemented.

**Architecture:** Acceptance tests live outside individual services under `backend/tests/Nerv.IIP.Business.Acceptance.Tests`. They exercise public HTTP APIs and integration-event-visible outcomes only. The tests must not read service databases directly except for optional diagnostic assertions behind explicit helpers.

**Tech Stack:** .NET 10, xUnit, ASP.NET Core test hosts, HttpClient, PostgreSQL profile tests, PowerShell verification script.

---

## Prerequisites

0. `scripts/verify-business-main-platform-integration-readiness.ps1`
1. `scripts/verify-business-master-data-foundation.ps1`
2. `scripts/verify-business-product-engineering-mvp.ps1`
3. `scripts/verify-business-common-capability-foundation.ps1`
4. `scripts/verify-business-demand-planning-mvp.ps1`
5. `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`
6. `scripts/verify-business-wms-execution-mvp.ps1`
7. `scripts/verify-business-mes-execution-mvp.ps1`
8. `scripts/verify-business-industrial-telemetry-mvp.ps1`
9. `scripts/verify-business-maintenance-mvp.ps1`

Each prerequisite script must pass before this plan starts.

## Acceptance Chains

| Chain | Required outcome |
| --- | --- |
| Engineering to manufacturing | Work order references released MBOM and routing. |
| Plan to procure/produce | MRP suggestions can be accepted by ERP and MES. |
| Procure to inventory to payable | Purchase receipt triggers inspection, inbound, stock movement and AP candidate. |
| Order to delivery to receivable | Sales order releases outbound, stock movement and AR candidate. |
| Production execution to cost | Operation report and finished receipt produce cost candidate. |
| Equipment to maintenance to capacity | Alarm opens maintenance work order and emits asset unavailable/restored facts. |
| WMS to WCS adapter | WCS dispatch, callback failure and retry diagnostics are visible. |

## File Structure Map

```text
backend/tests/Nerv.IIP.Business.Acceptance.Tests/
  Nerv.IIP.Business.Acceptance.Tests.csproj
  BusinessAcceptanceFixture.cs
  BusinessApiClients.cs
  EngineeringToManufacturingAcceptanceTests.cs
  PlanToProcureProduceAcceptanceTests.cs
  ProcureToPayAcceptanceTests.cs
  OrderToCashAcceptanceTests.cs
  ProductionToCostAcceptanceTests.cs
  EquipmentToMaintenanceAcceptanceTests.cs
  WcsAdapterAcceptanceTests.cs

scripts/verify-business-full-chain-acceptance.ps1
docs/architecture/implementation-readiness.md
README.md
```

## Task 1: Create Acceptance Test Project

**Files:**

- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessAcceptanceFixture.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessApiClients.cs`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create project**

Run:

```powershell
dotnet new xunit -n Nerv.IIP.Business.Acceptance.Tests -o backend/tests/Nerv.IIP.Business.Acceptance.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj
```

- [ ] **Step 2: Add service references**

Add references to each Business `.Web` project and `backend/common/Testing/Nerv.IIP.Testing/Nerv.IIP.Testing.csproj`.

- [ ] **Step 3: Implement fixture**

`BusinessAcceptanceFixture` starts test hosts for IAM and every implemented Business service, seeds admin permissions, exposes authorized `HttpClient` instances and resets data between tests using service public cleanup helpers or isolated test database names.

- [ ] **Step 4: Run empty project**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore
```

Expected: PASS with the default template test.

- [ ] **Step 5: Commit test harness**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: add business acceptance harness"
```

## Task 2: Add Engineering and Planning Acceptance

**Files:**

- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/EngineeringToManufacturingAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/PlanToProcureProduceAcceptanceTests.cs`

- [ ] **Step 1: Write engineering-to-manufacturing test**

The test must:

1. Create SKU, work center and device asset.
2. Register engineering document.
3. Release MBOM and routing.
4. Create MRP demand and run MRP.
5. Accept planned work order suggestion in MES.
6. Assert MES work order contains the released MBOM and routing references.

- [ ] **Step 2: Write plan-to-procure/produce test**

The test must:

1. Create sales demand for finished goods.
2. Seed available inventory lower than demand.
3. Run MRP.
4. Assert one planned purchase suggestion and one planned work order suggestion.
5. Accept purchase suggestion in ERP and work order suggestion in MES.
6. Assert both accepted suggestions are closed in DemandPlanning and formal document IDs are returned.

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~EngineeringToManufacturingAcceptanceTests|FullyQualifiedName~PlanToProcureProduceAcceptanceTests"
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: cover engineering and planning business chains"
```

Expected: tests pass.

## Task 3: Add Procurement, Sales and Production Acceptance

**Files:**

- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/ProcureToPayAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/OrderToCashAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/ProductionToCostAcceptanceTests.cs`

- [ ] **Step 1: Write procure-to-pay test**

Flow: purchase requisition -> RFQ -> supplier quotation -> purchase order -> purchase receipt -> quality inspection passed -> WMS inbound complete -> Inventory stock movement -> AP candidate. Assert stock increases and AP candidate amount equals receipt quantity times unit price.

- [ ] **Step 2: Write order-to-cash test**

Flow: opportunity -> quotation -> sales order -> delivery order -> WMS outbound complete -> Inventory stock movement -> AR candidate. Assert stock decreases and AR candidate amount equals shipped quantity times sales price.

- [ ] **Step 3: Write production-to-cost test**

Flow: MES work order -> operation task -> report operation -> quality operation inspection -> finished goods receipt request -> WMS inbound complete -> Inventory movement -> ERP cost candidate. Assert cost candidate references report ID, work order ID and inventory movement ID.

- [ ] **Step 4: Run and commit**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~ProcureToPayAcceptanceTests|FullyQualifiedName~OrderToCashAcceptanceTests|FullyQualifiedName~ProductionToCostAcceptanceTests"
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: cover procure sales and production business chains"
```

Expected: tests pass.

## Task 4: Add Equipment and WCS Acceptance

**Files:**

- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/EquipmentToMaintenanceAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/WcsAdapterAcceptanceTests.cs`

- [ ] **Step 1: Write equipment-to-maintenance test**

Flow: create device asset -> create telemetry tag -> raise alarm -> Maintenance opens work order -> mark asset unavailable -> complete work order -> asset restored event visible. Assert `maintenance.AssetUnavailable` and `maintenance.AssetRestored` events are emitted with device asset ID, reason, start time and restored time so MES and Planning can consume them.

- [ ] **Step 2: Write WCS adapter test**

Flow: WMS creates warehouse task -> dispatch WCS task -> external failure callback -> diagnostics visible -> retry dispatch -> success callback -> warehouse task complete. Assert WMS never posts inventory movement before successful warehouse completion.

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~EquipmentToMaintenanceAcceptanceTests|FullyQualifiedName~WcsAdapterAcceptanceTests"
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests
git commit -m "test: cover equipment maintenance and wcs chains"
```

Expected: tests pass.

## Task 5: Add Full Verification Script and Readiness Update

**Files:**

- Create: `scripts/verify-business-full-chain-acceptance.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

- [ ] **Step 1: Create verification script**

The script runs all prerequisite business slice verification scripts in order, then runs:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore
```

- [ ] **Step 2: Run final verification**

Run:

```powershell
scripts/verify-business-full-chain-acceptance.ps1
git diff --check
```

Expected: both commands exit `0`.

- [ ] **Step 3: Commit acceptance readiness**

Run:

```powershell
git add backend/tests/Nerv.IIP.Business.Acceptance.Tests scripts/verify-business-full-chain-acceptance.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "test: add business full chain acceptance"
```

## Self-Review Checklist

1. Tests use public APIs and authorized clients.
2. No acceptance test reaches into service databases for primary assertions.
3. Each critical chain from the spec has at least one test.
4. Failures print document IDs, suggestion IDs, movement IDs and event names for diagnosis.
