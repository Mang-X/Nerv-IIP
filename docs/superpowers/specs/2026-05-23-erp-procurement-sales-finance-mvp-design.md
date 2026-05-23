# ERP Procurement Sales Finance MVP Design

## Context

ERP is the remaining business execution service after Wave 1, Wave 2 and Equipment Reliability completion. `backend/services/Business/Erp` does not exist yet. GitHub #137, #138 and #139 are still open and are tracked under ERP epic #76.

This spec replaces the stale single 2026-05-20 ERP plan as the domain contract for the next implementation wave.

## Goals

1. Create a CleanDDD ERP service with Domain, Infrastructure and Web projects.
2. Implement Procurement/SRM-lite from planning suggestion to purchase receipt.
3. Implement Sales/CRM-lite/OMS-lite from opportunity to delivery request.
4. Implement Finance MVP for AP, AR, balanced vouchers and cost candidates.
5. Publish ERP facts through ADR 0011 integration event envelopes.
6. Keep ERP independent from WMS execution, Inventory balances and MES production task ownership.

## Non-Goals

1. No standalone SRM, CRM, CPQ or OMS service.
2. No complete general ledger, monthly closing, tax reporting or bank settlement.
3. No direct writes into Inventory, WMS, MES, DemandPlanning or Quality databases.
4. No cross-schema foreign keys.
5. No FileStorage object key or signed URL storage in domain aggregates.

## Service Boundary

| Area | ERP Owns | ERP Does Not Own |
| --- | --- | --- |
| Procurement | purchase requisition, RFQ, supplier quotation, purchase order, purchase receipt | supplier master data, warehouse receiving execution, stock balance |
| Sales | opportunity, quotation, sales order, delivery order request | customer master data, picking/packing execution, stock allocation balance |
| Finance | AP, AR, voucher, cost candidate, balanced posting invariant | full ledger close, bank reconciliation, tax engine |
| Integration | document lifecycle facts and accepted downstream references | another service's internal command or table state |

## Aggregates

| Aggregate | Issue | Key Invariants |
| --- | --- | --- |
| PurchaseRequisition | #137 | Can be created from a DemandPlanning suggestion or manually; suggestion reference is immutable; accepted suggestions must be idempotent by downstream document ID. |
| RequestForQuotation | #137 | Must reference one or more suppliers and at least one requested item; cannot receive quotations after closed/cancelled. |
| SupplierQuotation | #137 | Quantity and price must be positive; supplier and RFQ references are immutable. |
| PurchaseOrder | #137 | Lines cannot be empty; receipt quantity cannot exceed open ordered quantity; closed orders are immutable. |
| PurchaseReceipt | #137 | References PO and received lines; rejected or pending quality state cannot create AP candidate until accepted by business rule. |
| Opportunity | #138 | Customer reference and topic are required; closed opportunity cannot create new quotations. |
| Quotation | #138 | Lines cannot be empty; approved quotation can create sales order; expired/rejected quotation cannot. |
| SalesOrder | #138 | Created from quotation or manual order; delivery quantity cannot exceed ordered quantity. |
| DeliveryOrder | #138 | Requests WMS outbound execution; completion is confirmed from WMS/Inventory facts, not by direct warehouse mutation. |
| AccountPayable | #139 | Open amount is receipt amount minus paid amount; paid amount cannot exceed open amount. |
| AccountReceivable | #139 | Open amount is delivery/invoice amount minus collected amount; collected amount cannot exceed open amount. |
| JournalVoucher | #139 | Posting requires total debit equals total credit; posted voucher is immutable. |
| CostCandidate | #139 | References MES report, Inventory movement or WMS completion facts; remains a candidate, not final cost close. |

## Lifecycle Flows

### Procurement

```text
DemandPlanning.PlannedPurchaseSuggested
  -> ERP.PurchaseRequisition
  -> ERP.RequestForQuotation
  -> ERP.SupplierQuotation
  -> ERP.PurchaseOrder
  -> ERP.PurchaseReceipt
  -> Quality inspection / WMS inbound / Inventory movement
  -> ERP.AccountPayable
```

### Sales

```text
ERP.Opportunity
  -> ERP.Quotation
  -> ERP.SalesOrder
  -> ERP.DeliveryOrder
  -> WMS outbound / Inventory movement
  -> ERP.AccountReceivable
```

### Finance

```text
PurchaseReceipt / WMS inbound / Inventory movement
  -> AccountPayable candidate

DeliveryOrder / WMS outbound / Inventory movement
  -> AccountReceivable candidate

MES report / finished receipt / Inventory movement
  -> CostCandidate

AP / AR / CostCandidate
  -> JournalVoucher with balanced debit and credit lines
```

## API Contract

All ERP APIs use internal service authorization in the backend MVP. A future BusinessGateway or business console facade may expose user-facing routes.

| Method | Route | Permission | Operation ID |
| --- | --- | --- | --- |
| POST | `/api/business/v1/erp/purchase-requisitions/from-suggestion` | `business.erp.procurement.manage` | `createErpPurchaseRequisitionFromSuggestion` |
| POST | `/api/business/v1/erp/rfqs` | `business.erp.procurement.manage` | `createErpRequestForQuotation` |
| POST | `/api/business/v1/erp/supplier-quotations` | `business.erp.procurement.manage` | `receiveErpSupplierQuotation` |
| POST | `/api/business/v1/erp/purchase-orders` | `business.erp.procurement.manage` | `createErpPurchaseOrder` |
| POST | `/api/business/v1/erp/purchase-receipts` | `business.erp.procurement.manage` | `recordErpPurchaseReceipt` |
| GET | `/api/business/v1/erp/purchase-orders` | `business.erp.procurement.read` | `listErpPurchaseOrders` |
| POST | `/api/business/v1/erp/opportunities` | `business.erp.sales.manage` | `openErpOpportunity` |
| POST | `/api/business/v1/erp/quotations` | `business.erp.sales.manage` | `createErpQuotation` |
| POST | `/api/business/v1/erp/quotations/{quotationId}/approve` | `business.erp.sales.manage` | `approveErpQuotation` |
| POST | `/api/business/v1/erp/sales-orders` | `business.erp.sales.manage` | `createErpSalesOrder` |
| POST | `/api/business/v1/erp/delivery-orders` | `business.erp.sales.manage` | `releaseErpDeliveryOrder` |
| GET | `/api/business/v1/erp/sales-orders` | `business.erp.sales.read` | `listErpSalesOrders` |
| POST | `/api/business/v1/erp/finance/payables` | `business.erp.finance.manage` | `createErpAccountPayable` |
| POST | `/api/business/v1/erp/finance/receivables` | `business.erp.finance.manage` | `createErpAccountReceivable` |
| POST | `/api/business/v1/erp/finance/cost-candidates` | `business.erp.finance.manage` | `createErpCostCandidate` |
| POST | `/api/business/v1/erp/finance/vouchers` | `business.erp.finance.manage` | `postErpJournalVoucher` |
| GET | `/api/business/v1/erp/finance/summary` | `business.erp.finance.read` | `getErpFinanceSummary` |

## Integration Events

| Event | Publisher | Consumer Intent |
| --- | --- | --- |
| `erp.PurchaseRequisitionCreated` | ERP | Trace DemandPlanning suggestion acceptance and procurement start. |
| `erp.PurchaseOrderReleased` | ERP | Notify WMS/Quality/Notification that inbound work can be prepared. |
| `erp.PurchaseReceiptRecorded` | ERP | Trigger Quality receiving inspection, WMS inbound and AP candidate logic. |
| `erp.DeliveryOrderReleased` | ERP | Trigger WMS outbound fulfillment. |
| `erp.AccountPayableCreated` | ERP | Notify finance summaries and workflow. |
| `erp.AccountReceivableCreated` | ERP | Notify finance summaries and workflow. |
| `erp.CostCandidateCreated` | ERP | Surface production or inventory cost candidate facts. |
| `erp.JournalVoucherPosted` | ERP | Surface balanced finance posting fact. |

Events must not carry credentials, object storage keys, full attachment bytes or external-system secrets.

## Permissions

| Permission | Purpose |
| --- | --- |
| `business.erp.procurement.read` | Read procurement documents. |
| `business.erp.procurement.manage` | Create and progress requisitions, RFQs, quotations, purchase orders and receipts. |
| `business.erp.sales.read` | Read sales documents. |
| `business.erp.sales.manage` | Create and progress opportunities, quotations, sales orders and delivery orders. |
| `business.erp.finance.read` | Read AP, AR, voucher and finance summary facts. |
| `business.erp.finance.manage` | Create finance candidates and post balanced vouchers. |

## Persistence Rules

1. Default schema is `erp`; EF migrations history table is `erp.__EFMigrationsHistory`.
2. IDs use the repository's established strongly typed ID and Guid v7 conventions.
3. Monetary values use decimal columns with explicit precision.
4. JSON snapshot columns must use schema convention comments and versioned payloads when snapshots are required.
5. ERP tables must not include `stock_balance`, `on_hand_quantity`, `warehouse_task_state` or equivalent ownership leakage.
6. Cross-service references are public IDs or document references only.

## Acceptance

1. ERP service has Domain, Infrastructure and Web projects, migrations, tests and verify scripts.
2. Procurement can create purchase requisition from a planning suggestion, issue RFQ, receive supplier quotation, create purchase order and record receipt.
3. Sales can create opportunity, quote, approve quote, create sales order and release delivery order.
4. Finance rejects unbalanced vouchers and prevents AP/AR overpayment or over-collection.
5. ERP publishes the integration events listed in this spec.
6. IAM seed, authorization matrix, schema catalog, implementation readiness and AppHost are updated by the integration plan.
7. Full-chain acceptance can use ERP public APIs/events without reading ERP tables directly.

## Issue Mapping

| Issue | Plan |
| --- | --- |
| #137 | `docs/superpowers/plans/2026-05-23-erp-procurement-mvp.md` |
| #138 | `docs/superpowers/plans/2026-05-23-erp-sales-mvp.md` |
| #139 | `docs/superpowers/plans/2026-05-23-erp-finance-mvp.md` |
| #76/#77 integration | `docs/superpowers/plans/2026-05-23-business-wave-3-erp-registration-verify-readiness.md` |
