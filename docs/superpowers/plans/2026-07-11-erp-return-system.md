# ERP Return System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver MAN-397's auditable purchase-return and sales-RMA closure without crossing service data boundaries.

**Architecture:** WMS publishes completed physical return facts; ERP records immutable compensating documents and vouchers; Quality supplies the RMA credit decision. Public contracts carry stable business identifiers and each consumer uses its local inbox.

**Tech Stack:** .NET 10, CleanDDD/NetCorePal, FastEndpoints, EF Core migrations, CAP integration events, xUnit.

---

### Task 1: Freeze the accounting policy and public event contracts

**Files:**
- Create: `docs/architecture/erp-return-accounting-rules.md`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Erp/ErpIntegrationEvents.cs`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Wms/WmsIntegrationEvents.cs`
- Test: `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventContractTests.cs`

- [ ] **Step 1: Write failing serialization tests** for `erp.SalesReturnAuthorized` and the supplier-return WMS completion source metadata, asserting required envelope fields and line references.
- [ ] **Step 2: Run** `dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore` and verify the new contract tests fail because the types/constants do not exist.
- [ ] **Step 3: Add additive v1 contracts** with `RmaNo`, customer/site/line facts and WMS `SourceDocumentType`/`SourceDocumentId`; preserve existing fields and event versions.
- [ ] **Step 4: Re-run** the contract project and verify it passes.

### Task 2: Make WMS own both physical return executions

**Files:**
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Domain/AggregatesModel/SupplierReturnAggregate/SupplierReturnRequest.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/QualityInspectionResultIntegrationEventHandlerForReleaseWmsInboundGate.cs`
- Create: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventHandlers/ErpSalesReturnAuthorizedIntegrationEventHandler.cs`
- Modify: `backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Application/IntegrationEventConverters/WmsIntegrationEventConverters.cs`
- Test: `backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/WmsReturnIntegrationEventTests.cs`

- [ ] **Step 1: Write failing WMS tests** that a rejected supplier receipt creates one `purchase-receipt-return` outbound and that a replayed ERP RMA authorization creates one quality-gated inbound.
- [ ] **Step 2: Run** the named WMS test and verify it fails for missing return execution/consumer behavior.
- [ ] **Step 3: Implement only the tested behavior:** create outbound lines from the rejected receipt dimensions, consume the ERP RMA event through an inbox guard, and publish actual WMS completion facts with original source references.
- [ ] **Step 4: Re-run** the WMS test and verify it passes without inspecting another service database.

### Task 3: Add ERP compensating return and note aggregates

**Files:**
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseReturnAggregate/PurchaseReturn.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesReturnAuthorizationAggregate/SalesReturnAuthorization.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DebitNoteAggregate/DebitNote.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/CreditNoteAggregate/CreditNote.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountPayableAggregate/AccountPayable.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs`
- Test: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpReturnAggregateTests.cs`

- [ ] **Step 1: Write failing aggregate tests** for receipt-line return limits, AP debit-note application, AR credit-note application, and RMA quality states.
- [ ] **Step 2: Run** `dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore` and verify failure on the new aggregates/methods.
- [ ] **Step 3: Implement minimal immutable documents and AP/AR application counters**; reject over-return, over-credit, and repeated state transitions.
- [ ] **Step 4: Re-run** the ERP domain tests and verify they pass.

### Task 4: Persist and post ERP return compensation

**Files:**
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpProcurementEntityTypeConfigurations.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpSalesFinanceEntityTypeConfigurations.cs`
- Create: an EF-generated `AddErpReturnSystem` migration in `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/ErpFinanceCommands.cs`
- Test: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpReturnAccountingTests.cs`

- [ ] **Step 1: Write failing command tests** for un-invoiced GR/IR reversal, invoice-matched debit note/AP reduction, and credit-note/AR reduction with balanced vouchers.
- [ ] **Step 2: Run** the named ERP Web test and verify it fails before return posting exists.
- [ ] **Step 3: Add table mappings, explicit column comments/indexes, migration, and voucher factory methods:** purchase return uses Dr `GR-IR`/Cr `1401` for un-invoiced quantity; debit note uses Dr `2202`/Cr `1401`; credit note uses Dr `6001`/Cr `1122`.
- [ ] **Step 4: Re-run** the ERP Web tests and the ERP schema convention test.

### Task 5: Wire consumers, API governance, and real closure verification

**Files:**
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WmsReturnIntegrationEventHandlers.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/QualityRmaInspectionResultIntegrationEventHandler.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesFinanceIntegrationEventConverters.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpProcurementEndpoints.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpSalesFinanceEndpoints.cs`
- Modify: `docs/architecture/integration-event-consumption-matrix.md`
- Modify: `docs/architecture/facade-coverage-matrix.json`
- Modify: `docs/architecture/database-schema-catalog.md`
- Test: `backend/tests/Nerv.IIP.Business.FullChain.Tests/ErpReturnClosurePostgresAcceptanceTests.cs`

- [ ] **Step 1: Write a failing cross-boundary acceptance test** that drives RMA authorization -> WMS inbound completion -> Quality pass -> credit note/AR, plus supplier return WMS outbound completion -> purchase return/debit-or-GRIR compensation; replay both events and assert exactly one document/voucher effect.
- [ ] **Step 2: Run** the targeted test and verify failure because the event consumers do not exist.
- [ ] **Step 3: Implement guarded consumers and deferred facade rows**, update event/schema documentation, and generate an EF migration using the PostgreSQL profile. The endpoints remain deferred, so do not alter Gateway OpenAPI or generated client code.
- [ ] **Step 4: Run** ERP/WMS/contract tests, schema/facade gates, full backend solution tests, and the real PostgreSQL acceptance test when `NERV_IIP_TEST_POSTGRES` is configured.
