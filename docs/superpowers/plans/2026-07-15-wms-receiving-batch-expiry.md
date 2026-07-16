# WMS Receiving Batch and Expiry Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close #926 and #927 by exposing persisted inbound-line dates and atomically applying receiving-station batch captures during inbound completion.

**Architecture:** Extend the existing WMS aggregate and complete-inbound contract instead of adding a parallel endpoint. BusinessGateway remains a two-hop facade; its OpenAPI snapshot and generated TypeScript client are refreshed after backend behavior is proven.

**Tech Stack:** .NET 10, NetCorePal/CleanDDD, EF Core, FastEndpoints, xUnit, FluentValidation, Vite+, pnpm 11.1.2, generated `@nerv-iip/api-client`.

## Global Constraints

- Preserve the existing `CompleteInboundOrder` idempotency key and endpoint operation ID.
- Keep complete requests without `lines` backward compatible.
- Do not add a database migration; the WMS schema already owns the three batch columns.
- Do not add PDA UI behavior from #813.
- Keep the existing facade-coverage rows declared `exposed`; do not create no-op JSON churn.
- Preserve the user's unrelated `skills-lock.json` modification.

---

### Task 1: WMS atomic line capture during completion

**Files:**
- Modify: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsInventoryBoundaryTests.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/InboundOrderAggregate/InboundOrder.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/WmsCommands.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Endpoints/Wms/WmsEndpoints.cs`
- Modify: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsEndpointContractTests.cs`

**Interfaces:**
- Produces: `InboundOrderLineCapture(string LineNo, string? LotNo, DateOnly? ProductionDate, DateOnly? ExpiryDate)`.
- Produces: `InboundOrder.Complete(string idempotencyKey, IReadOnlyCollection<InboundOrderLineCapture>? captures = null)`.
- Produces: optional `Lines` on `CompleteInboundOrderRequest` and `CompleteInboundOrderCommand`.

- [ ] **Step 1: Write failing aggregate/handler tests**

Add tests proving that a capture changes the line and resulting movement request, omitted captures preserve existing values, and duplicate/unknown line numbers or `expiryDate < productionDate` throw.

```csharp
var result = inbound.Complete("idem-001", [
    new InboundOrderLineCapture("10", "LOT-SCAN", new DateOnly(2026, 7, 1), new DateOnly(2027, 7, 1))
]);
Assert.Equal("LOT-SCAN", result.Single().LotNo);
Assert.Equal(new DateOnly(2027, 7, 1), result.Single().ExpiryDate);
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --filter "FullyQualifiedName~WmsInventoryBoundaryTests"
```

Expected: compile failure because `InboundOrderLineCapture` and the complete overload do not exist.

- [ ] **Step 3: Implement minimal aggregate behavior**

Normalize and validate captures before changing any line, reject duplicate/unknown lines and reversed dates, then apply all values while the order is open. Keep the existing overload behavior through an optional parameter.

```csharp
public sealed record InboundOrderLineCapture(
    string LineNo,
    string? LotNo,
    DateOnly? ProductionDate,
    DateOnly? ExpiryDate);
```

- [ ] **Step 4: Add endpoint binding tests, verify RED, then wire request to command**

The endpoint request uses optional lines and maps every field without generating a second idempotency key.

```csharp
public sealed record CompleteInboundOrderRequest(
    InboundOrderId InboundOrderId,
    string IdempotencyKey,
    IReadOnlyCollection<WmsInboundLineCaptureInput>? Lines = null);
```

- [ ] **Step 5: Run focused WMS tests and verify GREEN**

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj
```

Expected: all tests pass with zero warnings.

### Task 2: WMS quality-gate date projection

**Files:**
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Queries/WmsQueries.cs`
- Modify: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsEndpointContractTests.cs`

**Interfaces:**
- Produces: nullable `ProductionDate` and `ExpiryDate` on `ReceivingQualityGateFact`.

- [ ] **Step 1: Add a failing query assertion**

Create an inbound line with fixed dates and assert both values on the returned quality-gate item.

```csharp
Assert.Equal(new DateOnly(2026, 7, 1), item.ProductionDate);
Assert.Equal(new DateOnly(2027, 7, 1), item.ExpiryDate);
```

- [ ] **Step 2: Run the quality-gate test and verify RED**

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --filter "FullyQualifiedName~ReceivingQuality"
```

Expected: compile failure because the fact lacks the date properties.

- [ ] **Step 3: Extend the fact and EF projection**

Add both nullable `DateOnly` properties beside lot/serial fields and select `x.line.ProductionDate` and `x.line.ExpiryDate`.

- [ ] **Step 4: Re-run WMS tests and verify GREEN**

Run the complete WMS Web test project; expect zero failures.

### Task 3: BusinessGateway two-hop facade

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleWmsModels.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayWmsTests.cs`

**Interfaces:**
- Consumes: WMS complete `lines` and quality-gate date fields from Tasks 1-2.
- Produces: Gateway create-line dates, complete-line capture DTOs, and quality-gate item dates.

- [ ] **Step 1: Write failing proxy tests**

Assert create-inbound forwards `productionDate`/`expiryDate`, complete-inbound forwards line captures, and quality-gate JSON returns ISO dates.

```csharp
Assert.Equal(new DateOnly(2026, 7, 1), request.Lines.Single().ProductionDate);
Assert.Equal("2027-07-01", jsonItem.GetProperty("expiryDate").GetString());
```

- [ ] **Step 2: Run BusinessGateway WMS tests and verify RED**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter "FullyQualifiedName~BusinessGatewayWmsTests"
```

Expected: compile failures for missing DTO properties.

- [ ] **Step 3: Extend Gateway records minimally**

Add nullable create-line dates, a `BusinessConsoleWmsInboundLineCaptureInput` record, optional complete `Lines`, and nullable quality-gate dates. The HTTP client continues direct serialization/deserialization.

- [ ] **Step 4: Re-run BusinessGateway tests and verify GREEN**

Run the full BusinessGateway Web test project; expect zero failures.

### Task 4: OpenAPI and generated client synchronization

**Files:**
- Generate: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Generate: `frontend/packages/api-client/src/generated/business-console/**`
- Modify if required: `frontend/packages/api-client/src/business-console.ts`

**Interfaces:**
- Produces: stable generated TypeScript types for create dates, complete captures, and quality-gate dates.

- [ ] **Step 1: Export OpenAPI using governed automation**

```powershell
scripts/export-gateway-openapi.ps1
```

Expected: BusinessGateway snapshot contains nullable `string`/`date` fields and complete `lines`.

- [ ] **Step 2: Regenerate the API client**

```powershell
pnpm -C frontend generate:api
```

Expected: generated business-console types and schemas update without manual edits.

- [ ] **Step 3: Verify stable exports and generated drift**

Use `rg` to confirm the relevant types are reachable from `src/business-console.ts`; add only missing stable type exports, then run API-client tests/typecheck.

### Task 5: Full verification, review, and delivery

**Files:**
- Verify: all changed files
- Preserve: `skills-lock.json`

**Interfaces:**
- Consumes: all prior tasks.
- Produces: one reviewed commit series and one PR closing #926 and #927.

- [ ] **Step 1: Run backend gates**

```powershell
dotnet test backend/Nerv.IIP.sln
```

Expected: zero failures, including facade coverage.

- [ ] **Step 2: Run frontend gates**

```powershell
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

Expected: zero failures. Report any documented pre-existing baseline failure separately with evidence.

- [ ] **Step 3: Review the complete diff**

Run `git diff --check`, inspect `git diff origin/main...HEAD`, and use the repository code-review workflow. Fix any correctness finding and repeat affected tests.

- [ ] **Step 4: Commit only task files**

Stage explicit paths so the unrelated `skills-lock.json` change remains unstaged. Use focused commits for behavior and generated contracts.

- [ ] **Step 5: Push and create one PR**

Create a PR with `Closes #926` and `Closes #927`, declare changed WMS endpoints `exposed`, state that existing facade-matrix rows remain valid, and include the product-docs impact checklist.
