# Issue 411 ERP Business Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the backend ERP business logic gaps called out in GitHub issue #411 with a minimal but real procure-to-pay and order-to-cash closure.

**Architecture:** Keep ERP as the source for procurement, sales and finance documents; keep Inventory as the only stock ledger; keep WMS as outbound execution authority. ERP will publish public Inventory/WMS contract events and will create matched AP/AR and subledger journal vouchers inside its own boundary.

**Tech Stack:** .NET 10, CleanDDD/NetCorePal, FastEndpoints, EF Core PostgreSQL migrations, CAP integration events, xUnit.

---

## Scope

This plan intentionally closes the P0/P1 gaps named by #411 and leaves P2 tax/multi-currency/returns/ATP as separate future issues:

1. Supplier invoice + three-way match before AP creation.
2. Purchase receipt publishes `InventoryMovementRequested` with SKU/UOM/site/location/quantity.
3. Delivery order publishes a WMS outbound request contract with SKU/UOM/site/location/quantity.
4. AP/AR due dates, aging buckets and clearing endpoints.
5. Sales order credit check from customer limit minus open AR and active released orders.
6. AP/AR/cost candidate creation posts subledger journal vouchers automatically.

## Files

- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Inventory/InventoryIntegrationEvents.cs`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Wms/WmsIntegrationEvents.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseReceiptAggregate/PurchaseReceipt.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesOrderAggregate/SalesOrder.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountPayableAggregate/AccountPayable.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SupplierInvoiceAggregate/SupplierInvoice.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpProcurementDomainEvents.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpSalesFinanceDomainEvents.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpProcurementEntityTypeConfigurations.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpSalesFinanceEntityTypeConfigurations.cs`
- Create: EF migration under `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/ErpProcurementCommands.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Sales/ErpSalesCommands.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/ErpFinanceCommands.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/SalesFinance/ErpSalesFinanceQueries.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpProcurementIntegrationEventConverters.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesFinanceIntegrationEventConverters.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpProcurementEndpoints.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpSalesFinanceEndpoints.cs`
- Modify tests under `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/` and `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/`
- Modify: `docs/architecture/business-platform-domain-architecture.md`
- Modify: `docs/architecture/implementation-readiness.md`

## Tasks

- [ ] Write failing ERP domain tests for supplier invoice matching, receipt/delivery line dimensions, due dates, aging, clearing and credit checks.
- [ ] Write failing ERP web tests for Inventory/WMS contract events, finance clearing endpoints, aging query and automatic vouchers.
- [ ] Implement domain changes and new SupplierInvoice aggregate.
- [ ] Implement ERP commands, queries, endpoints and event converters using only public contracts.
- [ ] Add EF mapping and migration for supplier invoices plus AP/AR due-date fields.
- [ ] Update focused architecture/readiness docs for #411 closure and remaining P2 non-goals.
- [ ] Run ERP domain/web tests, contract tests touched by Inventory/WMS contract changes, ERP verify script and AppHost build where feasible.
- [ ] Commit, push `codex/issue-411-erp-business-gap`, and create a draft PR with `Closes #411`.

## Acceptance Checks

1. AP can be created from a matched supplier invoice only when PO, receipt and invoice quantities/prices are within tolerance.
2. A purchase receipt emits both ERP receipt fact and Inventory movement request fact with line-level stock dimensions.
3. A delivery order emits both ERP delivery fact and WMS outbound request fact with line-level fulfillment dimensions.
4. AP/AR clearing is reachable through endpoints, prevents over-clearing, updates open amount and posts balanced clearing vouchers.
5. AP/AR list responses expose due dates and aging buckets.
6. Sales order creation rejects customers whose credit limit is exceeded by open AR plus active released order exposure.
7. AP/AR/cost candidate creation posts balanced subledger vouchers without direct cross-service writes.

## Review Follow-up Scope

The post-review correction keeps two #411 P1 items inside this PR instead of documenting them as risks:

1. Purchase orders must start as approval-gated documents, not directly `Released`. ERP creates a pending PO, requests BusinessApproval through a public service contract, rejects receipts before release, and consumes Approval completed events to release or cancel the PO.
2. Supplier invoices in `PaymentHeld` must have a minimal reachable path. ERP supports releasing a held invoice to create the AP/voucher after review, and voiding a held invoice so its quantities no longer consume cumulative invoiced quantity.
