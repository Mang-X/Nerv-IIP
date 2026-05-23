# Business Full-Chain Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #77 by adding end-to-end acceptance coverage for the seven critical business chains after ERP is complete.

**Architecture:** Acceptance tests live outside individual services under `backend/tests/Nerv.IIP.Business.Acceptance.Tests`. They use public HTTP APIs and integration-event-visible outcomes. They do not read service databases for primary assertions.

**Tech Stack:** .NET 10, xUnit, ASP.NET Core test hosts, HttpClient, existing service WebApplicationFactory patterns, governed PowerShell scripts.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-business-full-chain-acceptance-design.md`.

## Prerequisites

Do not start this plan until these pass:

1. `scripts/verify-business-wave1-foundation.ps1`
2. `scripts/verify-business-wave2-execution.ps1`
3. `scripts/verify-business-equipment-reliability.ps1`
4. `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`
5. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`

## Files

- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessAcceptanceFixture.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessApiClients.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/BusinessAcceptanceEventRecorder.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/EngineeringToManufacturingAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/PlanToProcureProduceAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/ProcureToPayAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/OrderToCashAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/ProductionToCostAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/EquipmentToMaintenanceAcceptanceTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Acceptance.Tests/WcsAdapterAcceptanceTests.cs`
- Modify: `backend/Nerv.IIP.sln`
- Create: `scripts/verify-business-full-chain-acceptance.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `README.md`

## Task 1: Create Acceptance Harness

- [ ] **Step 1: Create test project**

Run:

```powershell
dotnet new xunit -n Nerv.IIP.Business.Acceptance.Tests -o backend/tests/Nerv.IIP.Business.Acceptance.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj
```

- [ ] **Step 2: Add references**

Reference service Web projects and shared test helpers according to existing backend test patterns. Do not reference service Domain/Infrastructure projects from another service for behavior assertions.

- [ ] **Step 3: Implement fixture**

`BusinessAcceptanceFixture` should provide authorized clients for MasterData, ProductEngineering, Inventory, Quality, MES, DemandPlanning, WMS, IndustrialTelemetry, Maintenance and ERP.

- [ ] **Step 4: Implement event recorder**

Use integration event converter outputs, test bus hooks or visible service outcomes. The recorder must capture event type, version, source service, source document ID and correlation ID when available.

- [ ] **Step 5: Run empty harness**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore
```

Expected: empty harness passes.

## Task 2: Add Engineering And Planning Chains

- [ ] **Step 1: Engineering to manufacturing**

Test flow:

1. Create SKU, work center and resource references.
2. Create engineering document, EBOM, MBOM, Routing and ProductionVersion.
3. Run MRP for finished goods demand.
4. Accept planned work order suggestion in MES.
5. Assert MES work order references released ProductionVersion, MBOM and Routing facts by public IDs.

- [ ] **Step 2: Plan to procure/produce**

Test flow:

1. Create demand source.
2. Seed availability lower than demand.
3. Run MRP.
4. Assert one planned purchase suggestion and one planned work order suggestion.
5. Accept purchase suggestion in ERP and work order suggestion in MES.
6. Assert DemandPlanning marks both suggestions accepted with downstream document references.

- [ ] **Step 3: Run focused tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~EngineeringToManufacturingAcceptanceTests|FullyQualifiedName~PlanToProcureProduceAcceptanceTests"
```

Expected: focused tests pass.

## Task 3: Add Procurement, Sales And Production Chains

- [ ] **Step 1: Procure to pay**

Flow: ERP purchase requisition -> RFQ -> supplier quotation -> purchase order -> purchase receipt -> Quality inspection passed -> WMS inbound complete -> Inventory movement -> ERP AP candidate.

Assert stock increases and AP candidate amount equals receipt quantity times unit price.

- [ ] **Step 2: Order to cash**

Flow: ERP opportunity -> quotation -> sales order -> delivery order -> WMS outbound complete -> Inventory movement -> ERP AR candidate.

Assert stock decreases and AR candidate amount equals shipped quantity times sales price.

- [ ] **Step 3: Production to cost**

Flow: MES work order -> operation task -> production report -> Quality operation inspection -> finished goods receipt request -> WMS inbound complete -> Inventory movement -> ERP cost candidate.

Assert cost candidate references report ID, work order ID and movement/completion source ID.

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~ProcureToPayAcceptanceTests|FullyQualifiedName~OrderToCashAcceptanceTests|FullyQualifiedName~ProductionToCostAcceptanceTests"
```

Expected: focused tests pass.

## Task 4: Add Equipment And WCS Chains

- [ ] **Step 1: Equipment to maintenance to capacity**

Flow: MasterData device asset -> IndustrialTelemetry tag -> alarm raised -> Maintenance work order opened -> asset unavailable -> work order completed -> asset restored -> MES scheduling constraint updated.

Assert `industrialTelemetry.AlarmRaised`, `maintenance.AssetUnavailable` and `maintenance.AssetRestored` are visible through public contracts or service-visible outcomes.

- [ ] **Step 2: WMS to WCS adapter**

Flow: WMS warehouse task -> WCS dispatch -> failure callback -> diagnostics visible -> retry dispatch -> success callback -> warehouse task complete.

Assert WMS does not create Inventory movement request before successful warehouse completion.

- [ ] **Step 3: Run focused tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj --no-restore --filter "FullyQualifiedName~EquipmentToMaintenanceAcceptanceTests|FullyQualifiedName~WcsAdapterAcceptanceTests"
```

Expected: focused tests pass.

## Task 5: Add Full Verification Script And Readiness Update

- [ ] **Step 1: Create script**

Create `scripts/verify-business-full-chain-acceptance.ps1`. It must dot-source `scripts/lib/ScriptAutomation.ps1` and run all prerequisite scripts before the acceptance test project.

- [ ] **Step 2: Run final verification**

Run:

```powershell
scripts/verify-business-full-chain-acceptance.ps1
scripts/check-script-governance.ps1
git diff --check
```

Expected: all checks pass.

- [ ] **Step 3: Update readiness and README**

Record that Full-chain acceptance passes, the verify script exists, and #77 can be closed only after the target profile run passes.

## Self-Review Checklist

1. Each of the seven chains has at least one test.
2. Primary assertions use public APIs and integration-event-visible facts.
3. Test failures print chain name, source document ID, downstream document ID and event type.
4. The verify script follows script governance helper rules.
