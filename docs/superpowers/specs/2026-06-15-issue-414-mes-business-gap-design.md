# Issue 414 MES Business Gap Design

## Context

GitHub issue #414 identifies MES business-loop gaps around work order lifecycle, material consumption inventory posting, finished-goods receipt, material issue and line-side receipt, defect-to-NCR handoff, and genealogy. Current repository facts show MES has durable execution aggregates and CAP registration, while Inventory, Quality and WMS own their downstream facts through public contracts. MES must therefore publish or consume public integration events; it must not mutate downstream service schemas or invent stock, NCR, WMS or engineering facts.

## Scope

This slice closes the hard backend breaks without pretending every upstream domain is complete:

1. Add MES lifecycle facts for started/completed/closed, completed and scrap quantities, held/scrapped states, and over-production guardrails.
2. Publish Inventory movement requests from MES production reports and finished-goods receipt requests using `Nerv.IIP.Contracts.Inventory`.
3. Publish a material-issue outbound movement request when MES creates a material issue request, so downstream WMS/Inventory can see a warehouse pick/issue intent through the existing movement-requested contract.
4. Publish MES defect-raised facts toward Quality and consume Quality NCR disposition decisions to mark MES defects with supported/unsupported dispositions.
5. Add produced lot/serial references to production reports and finished-goods receipt requests so existing traceability queries can answer forward genealogy by public references.
6. Keep unsupported downstream behavior explicit. MES records request and disposition intent; final stock posting, warehouse completion and NCR ownership stay with Inventory, WMS and Quality.

## Non-Goals

1. Do not implement a new WMS outbound order API from MES in this PR; WMS currently exposes warehouse-owned execution endpoints and movement-request status, not a MES-specific kitting command contract.
2. Do not implement full Quality automatic NCR creation if Quality has no consumer for a new MES defect event in this slice. The MES event provides the public handoff fact.
3. Do not add full route/MBOM expansion from ProductEngineering here. Missing route facts remain an explicit release blocker.
4. Do not implement advanced operator skill validation, changeover states or shift handover item detail in this issue-414 slice.

## Event Design

MES uses domain events converted by `IIntegrationEventConverter`:

1. `ProductionReportRecordedDomainEvent` converts consumed material lines to one `inventory.InventoryMovementRequested` per line with `MovementType=outbound`, `SourceService=business-mes`, `SourceDocumentId=ReportNo`, and a stable idempotency key.
2. `FinishedGoodsReceiptRequestedDomainEvent` converts the receipt request to one `inventory.InventoryMovementRequested` with `MovementType=inbound`, `SourceService=business-mes`, `SourceDocumentId=RequestNo`.
3. `MaterialIssueRequestedDomainEvent` converts the material issue request to one `inventory.InventoryMovementRequested` with `MovementType=outbound`, `SourceService=business-mes`, `SourceDocumentId=RequestNo`.
4. `DefectRaisedDomainEvent` converts to a Quality-compatible `quality.NcrOpened` envelope only as an upstream defect handoff payload. Quality remains the NCR owner; MES marks the local defect as `NcrRequested`.
5. MES consumes `quality.DispositionDecided`; supported dispositions update the local defect to `ReworkPending`, `ScrapAccepted`, `ReturnAccepted` or `DispositionAccepted`. Missing local defect is ignored idempotently because the NCR may come from another source.

## Data Model

MES adds fields to existing tables:

1. `work_orders`: `completed_quantity`, `scrap_quantity`, `over_receipt_tolerance_percent`, `closed_at_utc`, `hold_reason`, `cancel_reason`.
2. `production_reports`: `rework_quantity`, `scrap_reason_code`, `defect_record_no`, `produced_lot_no`, `serial_no`.
3. `finished_goods_receipt_requests`: `status`, `produced_lot_no`, `serial_no`, `posted_inventory_movement_id`, `posted_at_utc`.
4. `material_issue_requests`: `status` remains request/receipt state; downstream posting is represented by published movement request and existing local receipt facts.
5. `defect_records`: `ncr_id`, `ncr_code`, `disposition_type`, `disposition_reference_id`, `closed_at_utc`.

## Error And Empty-State Rules

1. If a report has no consumed material lines, MES records production and publishes no consumption movements.
2. If a finished-goods receipt has no produced lot/serial, MES still requests inbound movement with null lot/serial and traceability reports it as unknown, not fabricated.
3. If Quality disposition references an NCR/defect MES cannot match, MES treats it as unsupported for this service and does not create fake defects.
4. If WMS outbound order orchestration is unavailable, MES material issue creation still publishes the public movement request and returns the local request number; the UI can show "warehouse orchestration unsupported" from facade/read models later.

## Tests

1. Domain tests prove work order progress guards, hold/resume/cancel, completion/close transitions, report produced-lot facts, and defect disposition transitions.
2. Web tests prove MES integration converters emit Inventory/Quality events with stable event types, source service, idempotency and payload dimensions.
3. Handler tests prove Quality NCR disposition events update local defects idempotently and ignore unrelated NCRs.
4. Query tests prove traceability includes produced lot/serial and does not fake missing genealogy.
