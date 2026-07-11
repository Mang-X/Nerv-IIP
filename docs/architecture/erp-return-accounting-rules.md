# ERP Return Accounting Rules

## Purpose

MAN-397 defines return documents as compensating facts. A recorded purchase receipt, posted voucher, completed WMS movement, matched supplier invoice, and issued AR remain immutable. A return therefore creates a separately numbered return/debit/credit document and a balancing voucher; it never deletes or mutates the original fact.

## Ownership and Trigger Facts

| Fact | Owner | ERP action |
| --- | --- | --- |
| Supplier-return WMS outbound completed | WMS | Create one ERP purchase return from the referenced receipt lines. |
| Customer RMA WMS inbound completed | WMS | Mark the RMA warehouse-received; no financial posting yet. |
| Customer-return receiving inspection passed or conditionally released | Quality | Record disposition, issue credit note, apply it to the referenced open AR, and post the credit voucher. |
| Customer-return receiving inspection rejected | Quality | Record the rejected disposition and make no credit posting. |

All cross-service facts use public versioned integration events, consumer-local inbox records, stable business idempotency keys, and DLQ on non-recoverable divergence. ERP never reads WMS, Quality, Inventory, or Finance schemas.

## Purchase Return Rules

1. A WMS supplier-return outbound must name the original ERP purchase receipt and its purchase-order line references. ERP rejects a line whose SKU/UOM, quantity, tenant, or already-returned balance does not match the immutable receipt fact.
2. The supplier-return outbound remains the stock-removal authority. ERP only records the financial/documentary compensation after WMS reports it completed.
3. For the uninvoiced portion of a returned receipt line, ERP posts **Dr `GR-IR`, Cr `1401` inventory**. This reverses the original goods-receipt accrual (Dr `1401`, Cr `GR-IR`).
4. For the invoice-matched portion, ERP issues a supplier debit note, applies it to the matching open AP, and posts **Dr `2202` accounts payable, Cr `1401` inventory**. The debit note cannot exceed the invoice/AP amount still open.
5. A mixed return is allowed: one immutable purchase-return document stores the returned quantity and separately records its GR/IR-reversal and debit-note amounts. Each compensating voucher is balanced, carries the source return number, and uses the return completion date subject to the existing accounting-period guard.

## Sales RMA Rules

1. An RMA is authorized only against an ERP sales-order line and its source AR. Customer, SKU, UOM, quantity, and credit amount must agree with ERP-owned sales/AR facts. The RMA cannot exceed the unreturned delivery quantity or the AR open balance.
2. ERP publishes the authorization; WMS owns creation and completion of the return inbound order. Its actual inbound number is only projected back when WMS completes the receipt.
3. The customer-return inbound is quality-gated. Quality disposition is an auditable prerequisite to credit: `InspectionPassed` and `InspectionConditionalReleased` are credit-eligible; `InspectionRejected` records a denied credit and does not alter AR.
4. A credit-eligible RMA creates one credit note, applies its amount to the original AR, and posts **Dr `6001` sales returns/allowances, Cr `1122` accounts receivable**. Applying a credit note is distinct from a cash receipt: it reduces AR without fabricating cash collection.
5. Replayed WMS or Quality events locate the existing RMA, return document, note, AP/AR application, and voucher by their stable business keys and produce no second financial effect.

## Boundaries and Reporting

- The BusinessERP service owns purchase returns, debit notes, RMAs, credit notes, AP/AR applications, and accounting vouchers in the `erp` schema.
- WMS owns warehouse task/outbound/inbound completion and Inventory owns physical stock posting. The ERP voucher is not a substitute for a WMS or Inventory result.
- The first delivery exposes BusinessERP endpoints as facade-coverage `deferred`; no BusinessGateway/OpenAPI/api-client change occurs until the Business Console returns workflow is delivered. Product documentation is unaffected until that user-facing flow exists.
