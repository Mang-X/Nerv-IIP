# MAN-599 Outbound Inventory Posting Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make ERP delivery completion and finance depend on a successful WMS-to-Inventory outbound posting using the exact finished-goods accounting key, with public failure and retry facts.

**Architecture:** ERP snapshots and publishes the sales site plus explicit line location/lot. WMS holds the outbound in a posting-pending state until the latest request for every line is posted, then emits its existing completion event; BusinessGateway exposes the detailed posting read and retry without owning business state.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, CAP over Redis Streams, FastEndpoints, xUnit, BusinessGateway OpenAPI, Hey API/pnpm, Playwright full-stack evidence.

---

### Task 1: Lock the ERP accounting key with failing tests

**Files:**
- Modify: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpSalesFinanceAggregateTests.cs`
- Modify: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpBusinessGapClosureTests.cs`
- Modify: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSalesFinanceEndpointContractTests.cs`

- [ ] **Step 1: Add tests for the site snapshot and event key**

Add assertions equivalent to:

```csharp
Assert.Equal("finished-goods", delivery.SiteCode);
Assert.Equal("finished-goods", integrationEvent.Payload.SiteCode);
Assert.Equal("receiving", line.LocationCode);
Assert.Equal("LOT-FG-001", line.LotNo);
```

- [ ] **Step 2: Add a delivery-list contract test**

Persist a released delivery and assert its response returns header `SiteCode`
and line `SkuCode`, `UomCode`, `LocationCode`, and `LotNo`.

- [ ] **Step 3: Run the targeted tests and observe RED**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --filter "FullyQualifiedName~ErpSalesFinanceAggregateTests"
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --filter "FullyQualifiedName~ErpBusinessGapClosureTests|FullyQualifiedName~ErpSalesFinanceEndpointContractTests"
```

Expected: failures identify the missing site/event/read fields.

### Task 2: Implement the ERP snapshot and public line contract

**Files:**
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpSalesFinanceEntityTypeConfigurations.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesFinanceIntegrationEventConverters.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/SalesFinance/ErpSalesFinanceQueries.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260723164736_AddDeliveryOrderSiteCode.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`

- [ ] **Step 1: Snapshot and map the site**

Implement:

```csharp
SiteCode = order.SiteCode;
public string SiteCode { get; private set; } = string.Empty;
```

Map it as `site_code`, required, length 100, with a comment.

- [ ] **Step 2: Publish the real site**

Replace the outbound payload's `null` site argument with:

```csharp
delivery.SiteCode
```

- [ ] **Step 3: Expand the ERP read DTO**

Use these additive shapes:

```csharp
public sealed record DeliveryOrderListItem(
    string DeliveryOrderNo,
    string SalesOrderNo,
    string CustomerCode,
    string SiteCode,
    string Status,
    IReadOnlyCollection<DeliveryOrderLineListItem> Lines,
    DateTime ReleasedAtUtc,
    DateTime? ShippedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record DeliveryOrderLineListItem(
    string SalesOrderLineNo,
    string SkuCode,
    string UomCode,
    string LocationCode,
    string? LotNo,
    decimal Quantity,
    decimal ShippedQuantity);
```

- [ ] **Step 4: Generate and complete the migration**

Run the governed EF command with the PostgreSQL provider. Keep generated model
metadata and add a same-schema backfill from `erp.sales_orders.site_code` before
making the new column non-empty for existing rows.

- [ ] **Step 5: Re-run Task 1 tests and observe GREEN**

Expected: all targeted ERP tests pass with no warnings.

### Task 3: Lock WMS posting state and exact owner behavior with failing tests

**Files:**
- Modify: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/WmsExecutionAggregateTests.cs`
- Modify: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsOutboundOrderRequestedConsumerTests.cs`
- Modify: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsStockMovementPostedConsumerTests.cs`
- Modify: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsInventoryBoundaryTests.cs`

- [ ] **Step 1: Add pending-before-posted domain tests**

After pack review, assert:

```csharp
Assert.Equal(OutboundOrderStatus.InventoryPostingPending, outbound.Status);
Assert.DoesNotContain(outbound.GetDomainEvents(), x => x is OutboundOrderCompletedDomainEvent);
```

After `MarkInventoryPostingCompleted()`, assert `Completed`, a non-null
`CompletedAtUtc`, and one completion event.

- [ ] **Step 2: Add consumer exact-key tests**

Assert the ERP event creates WMS with site `finished-goods`, location
`receiving`, lot `LOT-FG-001`, owner `production`, and null owner id. Add a
blank-site test that fails rather than creating a `default` outbound.

- [ ] **Step 3: Add posted/failure/retry tests**

For two lines, assert the first posted callback keeps the order pending and the
second completes it. Assert a rejection exposes the failed request and keeps
completion absent; a retry returns to pending and completes only after the retry
request posts.

- [ ] **Step 4: Run targeted WMS tests and observe RED**

Run:

```powershell
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj
dotnet test backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj --filter "FullyQualifiedName~WmsOutboundOrderRequestedConsumerTests|FullyQualifiedName~WmsStockMovementPostedConsumerTests|FullyQualifiedName~WmsInventoryBoundaryTests"
```

Expected: failures identify premature completion, wrong owner, fallback site,
and missing current-attempt aggregation.

### Task 4: Implement WMS posting-gated completion and failure read

**Files:**
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/OutboundOrderAggregate/OutboundOrder.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Commands/WmsCommands.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/WmsOutboundOrderRequestedIntegrationEventHandler.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/Queries/WmsQueries.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Infrastructure/Migrations/20260724015043_AddOutboundOrderConcurrencyToken.cs`

- [ ] **Step 1: Add the non-renumbering pending state**

Implement:

```csharp
Open = 0,
Completed = 1,
InventoryPostingFailed = 2,
Cancelled = 3,
InventoryPostingPending = 4,
```

Pack review and retry set `InventoryPostingPending`; neither emits
`OutboundOrderCompleted`.

- [ ] **Step 2: Complete only from a posted current attempt**

Implement an aggregate method that only accepts pending, sets `Completed`,
records `CompletedAtUtc`, and adds `OutboundOrderCompletedDomainEvent`.
In `MarkInventoryMovementRequestPostedCommandHandler`, group all requests for
the outbound by source line, select the newest request per line, and invoke the
aggregate method only when all current requests are posted.

Persist and advance an optimistic concurrency token for every outbound
aggregate mutation. A concurrent posted callback that observed a stale sibling
request must conflict and be retried so the all-lines-posted decision is
re-evaluated from current state.

- [ ] **Step 3: Make failure transitions accurate**

Allow `MarkInventoryPostingFailed` only from pending, clear legacy premature
completion timestamps, and leave failed request code/message intact. Retry
moves back to pending.

- [ ] **Step 4: Use the exact finished-goods owner key**

Reject blank ERP `SiteCode`; create ERP-origin outbound lines with:

```csharp
QualityStatus: "unrestricted",
OwnerType: "production",
OwnerId: null
```

- [ ] **Step 5: Return current line posting facts**

Expand the outbound list DTO to return site, completion time, a derived
`not-started|pending|failed|posted` posting status, and each line's exact key
plus latest request status/failure fields.

- [ ] **Step 6: Re-run Task 3 tests and observe GREEN**

Expected: all targeted WMS tests pass.

### Task 5: Lock and implement BusinessGateway facade changes

**Files:**
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayWmsTests.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleWmsModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleWmsClient.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Wms/BusinessConsoleWmsEndpoints.cs`

- [ ] **Step 1: Add failing proxy and OpenAPI tests**

Assert delivery writes forward `locationCode` and `lotNo`, delivery reads keep
site/SKU/lot, WMS reads keep posting failure facts, and OpenAPI contains:

```text
retryBusinessConsoleWmsOutboundInventoryPosting
```

- [ ] **Step 2: Run BusinessGateway tests and observe RED**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --filter "FullyQualifiedName~BusinessGatewayOpenApiTests|FullyQualifiedName~BusinessGatewayProxyTests|FullyQualifiedName~BusinessGatewayWmsTests"
```

- [ ] **Step 3: Add the delivery and WMS additive DTO fields**

Mirror the downstream additive fields exactly. Extend
`BusinessConsoleErpDeliveryOrderLine` with optional `LocationCode` and `LotNo`.

- [ ] **Step 4: Add retry client and endpoint**

Create a route/query/body request model and forward to:

```text
/api/business/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry
```

Authorize with `BusinessGatewayPermissions.WmsShipmentsManage`.

- [ ] **Step 5: Re-run BusinessGateway tests and observe GREEN**

Expected: targeted proxy, authorization, and OpenAPI tests pass.

### Task 6: Govern the facade, generated contract, and documentation

**Files:**
- Modify: `docs/architecture/facade-coverage-matrix.json`
- Modify: `docs/architecture/facade-coverage-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Regenerate: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Regenerate: `frontend/packages/api-client/src/generated/business-console/**`
- Modify: `frontend/packages/api-client/src/business-console.ts` only if the new operation is not already covered by existing stable exports

- [ ] **Step 1: Change retry classification to exposed**

Replace the deferred WMS outbound retry row with:

```json
{
  "service": "Wms",
  "method": "POST",
  "route": "/api/business/v1/wms/outbound-orders/{outboundOrderId}/inventory-posting/retry",
  "operationId": "retryWmsOutboundInventoryPosting",
  "classification": "exposed",
  "gateways": ["business"]
}
```

- [ ] **Step 2: Update schema and readiness narratives**

Document `delivery_orders.site_code`, the pending/failed/posted WMS semantics,
and the finance-after-posting invariant.

- [ ] **Step 3: Export and generate**

Export the BusinessGateway OpenAPI from the running backend process using the
repository script, then run:

```powershell
pnpm -C frontend generate:api
```

Do not hand-edit the snapshot or generated TypeScript.

- [ ] **Step 4: Run facade and generated-contract gates**

Run the facade coverage tests, frontend api-client tests/typecheck, and confirm
the generated retry helper and additive DTO fields exist.

### Task 7: Prove the PostgreSQL and Redis main chain

**Files:**
- Modify: `frontend/apps/business-console/e2e/leader-demo-main-chain.spec.ts`

- [ ] **Step 1: Add exact key and ordering assertions**

Release the delivery with `siteCode=finished-goods`,
`locationCode=receiving`, and the authoritative produced lot. After WMS
completion is requested, require a public WMS `posted` result and Inventory
on-hand `0` before accepting ERP `completed`, AR, or voucher facts.

- [ ] **Step 2: Run static and targeted gates**

Run affected backend tests, `dotnet test backend/Nerv.IIP.sln`, frontend
typecheck/test/build, touched-file formatting, and `git diff --check`.

- [ ] **Step 3: Run the managed real stack**

Run:

```powershell
.\nerv.ps1 fullstack run -Scenario leader-demo-main-chain
```

Expected: PostgreSQL and Redis are recorded in the session manifest; the exact
outbound key posts successfully; stock decreases; WMS completes before ERP/AR/
voucher; cleanup reports no owned resources remaining.

### Task 8: Review, commit, and create the ready PR

**Files:**
- Review every file in `git diff --name-only origin/main...HEAD`

- [ ] **Step 1: Self-review scope and generated overlap**

Confirm no #1087 behavior was added and generated-file overlap is limited to
the required BusinessGateway OpenAPI/codegen output.

- [ ] **Step 2: Commit and push**

Create scoped commits, push `codex/man-599-inventory-posting-closure`, and do
not merge.

- [ ] **Step 3: Create a ready PR**

Use `gh pr create` with `Fixes #1083`, mention MAN-599, list each changed
business endpoint classification, state the product-doc impact, and include
the exact verification evidence. Ensure the PR is not draft.

- [ ] **Step 4: Verify remote handoff**

Check PR state, head SHA, checks, changed files, and unresolved review threads.
Stop with the ready PR awaiting user review.
