# ERP Return System Design

## Scope

Implement MAN-397 / #713 only: supplier purchase returns connected to immutable receipt lines, customer RMAs through warehouse receipt and Quality disposition, debit/credit-note settlement, and auditable vouchers.

## Architecture

ERP owns return authorization and financial compensation; WMS owns physical supplier-return outbound and customer-return inbound; Quality owns inspection outcomes; Inventory remains the stock ledger owner. New public Events describe completed WMS facts and ERP RMA authorization. Consumers validate tenant/source/version, write their local inbox before side effects, and use document-number uniqueness as a second idempotency boundary.

## Chosen Flow

1. WMS turns a rejected purchase receipt inspection into a supplier-return outbound with source `purchase-receipt-return` and the original receipt number. Its completion event causes ERP to record the purchase return and the correct GR/IR reversal and/or debit note.
2. An ERP RMA command validates ERP sales/AR facts and publishes an authorized-return event. WMS creates a quality-gated inbound order. Its completion projects the real WMS number to the RMA; its Quality result determines credit eligibility.
3. On a credit-eligible result, ERP writes a credit note, applies it to the open AR, and posts the balanced credit voucher. Rejected quality creates an auditable denial only.

## Rejected Alternatives

- Direct ERP reads/writes of WMS, Quality, or Inventory schemas violate service ownership and cannot be verified through a public boundary.
- A Gateway or shared workflow table would become a process manager contrary to ADR 0017.
- Cancelling receipts, AP, AR, or vouchers would destroy audit history; compensating documents preserve it.
