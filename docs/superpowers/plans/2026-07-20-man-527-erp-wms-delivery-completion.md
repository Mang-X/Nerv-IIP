# MAN-527 ERP-WMS Delivery Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Project actual WMS outbound quantities into the matching ERP DeliveryOrder so its public read model distinguishes released, partially shipped, and completed deliveries without inferring shipment from AR creation.

**Architecture:** Keep WMS as execution-fact owner and ERP as the delivery-status projection owner. The existing `wms.OutboundOrderCompleted` V1 payload already carries the complete emitted line set and quantities; ERP will validate that set against `organizationId + environmentId + PublicReference`, apply each distinct event as a shipment delta, and create one AR only when the DeliveryOrder first reaches full completion. The existing ERP list endpoint and BusinessGateway facade remain the stable two-hop read path, extended additively with shipment quantities and timestamps.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, DotNetCore.CAP Redis transport, FastEndpoints/OpenAPI, PowerShell 7 governed verification scripts, pnpm/Hey API.

## Global Constraints

- Scope is only Linear MAN-527 / GitHub #971 on baseline `d534d7ed61b7c07c72741d979b83f306dee4cdd1`.
- Use test-first red-green-refactor for every behavior change.
- Do not introduce cross-schema foreign keys or make ERP query WMS storage.
- Keep `wms.OutboundOrderCompleted` V1 backward compatible; only correct producer facts already represented by existing fields.
- Existing ERP delivery-order GET remains `exposed` through BusinessGateway; update OpenAPI and generated api-client, never hand-edit generated artifacts.
- PostgreSQL schema changes require an EF migration, comments, model snapshot, and database schema catalog update.
- Real acceptance must use disposable PostgreSQL, Redis CAP transport, separate ERP/WMS processes, public HTTP endpoints, stable DeliveryOrderNo evidence, and deterministic cleanup.
- Create a ready PR with Linear MAN-527 in the body and `Fixes #971`; do not merge.

---

### Task 1: DeliveryOrder shipment state machine

**Files:**
- Modify: `backend/services/Business/ERP/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs`
- Test: `backend/services/Business/ERP/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpSalesFinanceAggregateTests.cs`

**Interfaces:**
- Consumes: shipment deltas keyed by sales-order line number.
- Produces: `DeliveryOrder.ApplyShipment(IReadOnlyCollection<DeliveryOrderShipmentLine>, DateTime)`, header `ShippedAtUtc`/`CompletedAtUtc`, and line `ShippedQuantity`.

- [x] **Step 1: Write failing aggregate tests** for partial single-line shipment, multi-event completion, multi-line partial shipment, duplicate/over-quantity rejection, and cancellation after shipment rejection.
- [x] **Step 2: Run the focused aggregate tests and confirm RED** because shipment APIs/properties do not exist.
- [x] **Step 3: Implement the minimal state machine**: first positive shipment sets `partially-shipped` and `ShippedAtUtc`; completion requires every line shipped quantity to equal requested quantity and then sets `completed` plus `CompletedAtUtc`; quantities are deltas and may not exceed remaining quantity.
- [x] **Step 4: Run focused aggregate tests and confirm GREEN**, then refactor names without changing behavior.

### Task 2: WMS event facts and ERP completion consumer

**Files:**
- Modify: `backend/services/Business/WMS/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/WmsOutboundOrderRequestedIntegrationEventHandler.cs`
- Modify: `backend/services/Business/WMS/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventConverters/WmsIntegrationEventConverters.cs`
- Modify: `backend/services/Business/ERP/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WmsOutboundOrderCompletedIntegrationEventHandlerForCreateAccountReceivable.cs`
- Test: `backend/services/Business/WMS/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsOutboundOrderRequestedConsumerTests.cs`
- Test: `backend/services/Business/WMS/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsIntegrationEventTests.cs`
- Test: `backend/services/Business/ERP/tests/Nerv.IIP.Business.Erp.Web.Tests/WmsOutboundCompletedAccountReceivableConsumerTests.cs`

**Interfaces:**
- Consumes: WMS V1 payload `PublicReference`, `Lines`, `IdempotencyKey`, and `OccurredAtUtc`.
- Produces: ERP projection and one AR after first transition to `completed`.

- [x] **Step 1: Write failing WMS tests** proving ERP-requested WMS orders retain DeliveryOrderNo as `SourceDocumentId`, and completion publishes that stable public reference plus all actual line quantities.
- [x] **Step 2: Run WMS focused tests and confirm RED** on the current SalesOrderNo/OutboundOrderNo mapping.
- [x] **Step 3: Correct the WMS mapping minimally** while preserving the V1 payload shape and non-ERP outbound behavior.
- [x] **Step 4: Write failing ERP consumer tests** proving partial shipment does not complete or accrue AR, a second distinct shipment completes and accrues once, multi-line payloads do not trust the first line, exact replay is idempotent, and malformed/over-quantity payloads dead-letter without mutation.
- [x] **Step 5: Run ERP focused tests and confirm RED** on current order-level AR behavior.
- [x] **Step 6: Update the consumer** to validate all payload lines, record the inbox key, apply shipment deltas, and create AR only after full completion; retain the existing unmatched-event and dead-letter policies.
- [x] **Step 7: Run ERP/WMS focused tests and confirm GREEN**.

### Task 3: PostgreSQL persistence and public two-hop contract

**Files:**
- Modify: `backend/services/Business/ERP/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpSalesFinanceEntityTypeConfigurations.cs`
- Create: `backend/services/Business/ERP/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/<timestamp>_AddErpDeliveryShipmentProjection.cs`
- Create: `backend/services/Business/ERP/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/<timestamp>_AddErpDeliveryShipmentProjection.Designer.cs`
- Modify: `backend/services/Business/ERP/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `backend/services/Business/ERP/src/Nerv.IIP.Business.Erp.Web/Application/Queries/SalesFinance/ErpSalesFinanceQueries.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Test: `backend/services/Business/ERP/tests/Nerv.IIP.Business.Erp.Web.Tests/WmsOutboundCompletedAccountReceivableConsumerTests.cs`
- Test: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

**Interfaces:**
- Consumes: the existing ERP delivery-order list endpoint and BusinessGateway pass-through.
- Produces: additive `shippedQuantity`, `shippedAtUtc`, and `completedAtUtc` response fields.

- [x] **Step 1: Extend failing query/proxy assertions** for header timestamps and per-line shipped quantity.
- [x] **Step 2: Run focused ERP and BusinessGateway tests and confirm RED**.
- [x] **Step 3: Add EF mappings and query/facade DTO fields**, preserving the existing endpoint path, operationId, authorization, and facade coverage row.
- [x] **Step 4: Generate the EF migration** with the PostgreSQL profile; migration adds nullable header timestamps and non-null line shipped quantity defaulted to zero, with comments.
- [x] **Step 5: Update the schema catalog** to document released/partially-shipped/completed and line accumulation semantics.
- [x] **Step 6: Run focused tests, migration/schema convention tests, and confirm GREEN**.
- [x] **Step 7: Export BusinessGateway OpenAPI and run `pnpm -C frontend generate:api`**, then run the OpenAPI/client drift gate.

### Task 4: Real PostgreSQL plus Redis cross-process acceptance

**Files:**
- Create: `scripts/verify-erp-wms-delivery-completion.ps1`
- Create: `scripts/tests/erp-wms-delivery-completion-verify-script.Tests.ps1`

**Interfaces:**
- Consumes: `NERV_IIP_TEST_POSTGRES`, `NERV_IIP_TEST_REDIS`, ERP/WMS public HTTP APIs, Redis CAP, disposable database.
- Produces: `artifacts/acceptance/man527/erp-wms-delivery-completion-evidence.json` and failure diagnostics.

- [x] **Step 1: Write the failing script contract test** requiring governance metadata, helper usage, separate ERP/WMS processes, PostgreSQL/Redis configuration, stable DeliveryOrderNo, duplicate completion/read assertions, evidence output, and cleanup.
- [x] **Step 2: Run the contract test and confirm RED** because the verify script is absent.
- [x] **Step 3: Implement the governed verify script** by creating a disposable database, starting required services as managed child processes, releasing a seeded sales order delivery over HTTP, waiting for Redis-created WMS outbound, completing it over HTTP, polling ERP delivery state, replaying the completion command/event path, and asserting one AR plus stable completed quantity/time.
- [x] **Step 4: Run script governance and the contract test, then execute the real acceptance** and inspect its evidence JSON.

### Task 5: Completion gates and PR

**Files:**
- Modify generated OpenAPI/api-client artifacts only through governed generators.
- Modify this plan's checkboxes to reflect executed evidence.

**Interfaces:**
- Consumes: all changed files and project gates.
- Produces: ready GitHub PR linked to MAN-527 and closing #971.

- [x] **Step 1: Run fresh targeted tests** for ERP domain/web, WMS web, BusinessGateway, migration/schema conventions, and script contracts.
- [x] **Step 2: Run `dotnet test backend/Nerv.IIP.sln`**, script governance, OpenAPI/client drift, and `git diff --check`.
- [x] **Step 3: Run `$env:Messaging__Provider='Redis'; .\nerv.ps1 fullstack run -Scenario smoke`** and confirm its session cleanup.
- [x] **Step 4: Review `git status` and `git diff`** for only MAN-527/#971 changes and no generated/manual drift.
- [x] **Step 5: Commit, push, and create a ready PR** whose body covers behavior, root cause, tests, risk, schema/OpenAPI/facade declaration, product-doc impact, Linear MAN-527, and `Fixes #971`.

## Self-Review

- Spec coverage: aggregate state, all-line validation, distinct-event accumulation, inbox/AR idempotency, public two-hop response, migration/catalog, Redis/PostgreSQL evidence, and ready PR are each mapped to a task.
- Placeholder scan: the migration timestamp is intentionally generated by EF at execution; every behavioral interface and verification command is otherwise explicit.
- Type consistency: shipment quantities use `decimal`; timestamps use UTC `DateTime` in the ERP model/DTO and event `OccurredAtUtc.UtcDateTime` at the boundary.
