# BarcodeLabel Business Scan GS1 Traceability Design

## Goal

Close GitHub issue #418 by turning BarcodeLabel from a passive scan log into a backend business scan intake that can parse GS1 labels, persist serialized traceability facts, and route supported scans into downstream business action contracts.

## Scope

BarcodeLabel remains the owner of barcode rules, label templates, print batches, label items, scan records and barcode traceability facts. It does not own inventory balances, WMS task state, MES work order state or Quality dispositions.

This slice implements the minimum closed loop:

1. GS1 AI parsing and validation for GTIN `(01)`, lot `(10)`, serial `(21)` and optional quantity `(30)`, including parenthesized values and raw GS1-128 values separated by FNC1 / ASCII 29.
2. GS1-aware label generation for serialized label items, with `gtin`, `lotNo` and `serialNumber` persisted on each item.
3. Scan records that store parsed GS1 data and reject unsupported workflow values instead of routing by free text.
4. EPCIS minimum facts in BarcodeLabel:
   - `commissioning` when a serialized label item is generated.
   - `objectEvent` when an accepted serialized scan is recorded.
5. Inventory scan workflow routing for `inventory.receipt`, `inventory.issue` and `inventory.adjustment` by publishing the existing `InventoryMovementRequestedIntegrationEvent`.

MES, WMS and Quality direct consumers are intentionally not added in this slice. They should subscribe to the shared barcode scan envelope or add their own downstream routes when their exact command contracts are ready. This keeps the current fix backend-real while avoiding fake service mutations.

## Contracts

BarcodeLabel publishes a shared barcode scan envelope from `Nerv.IIP.Contracts.BarcodeLabel`:

`BarcodeScanAcceptedIntegrationEvent`

The event includes organization, environment, device, source workflow, source document, idempotency, raw scanned value, parsed GS1 fields and scan timestamp. It carries only public business IDs and parsed label data.

For the initial business route, BarcodeLabel also publishes `InventoryMovementRequestedIntegrationEvent` for accepted inventory workflows. Payload mapping:

| Scan workflow | Inventory movement type |
| --- | --- |
| `inventory.receipt` | `inbound` |
| `inventory.issue` | `outbound` |
| `inventory.adjustment` | `adjustment` |

The scan request must provide `skuCode`, `uomCode`, `siteCode`, `locationCode`, `qualityStatus`, `ownerType` and `quantity` for inventory workflows. `lotNo` and `serialNo` are taken from parsed GS1 AI values when present. `sourceService` is `barcode-label`.

## Domain Rules

1. GS1 rules use barcode type `gs1-128` or `gs1-datamatrix` and checksum rule `gs1-mod10`.
2. GS1 label generation requires a numeric 13-digit GTIN root plus an explicit 6-12 digit GS1 company prefix length, then produces an AI string containing `(01)`, `(10)` and `(21)`.
3. Serialized label items must persist `gtin`, `lotNo`, `serialNumber` and optional `epcUri`.
4. Accepted scans must parse GS1 values before persistence when the scanned value starts with GS1 AI data. AI `(30)` is parsed as package content reference only; inventory movement quantity must be supplied by the scan context.
5. Inventory workflows require parsed or explicit SKU/lot/serial context and inventory movement fields. Missing business fields reject the command before persistence.
6. Rejected scans remain allowed and do not publish business action events.
7. Idempotent replay returns the existing scan and must not create duplicate EPCIS or downstream movement facts.

## Persistence

Add to schema `barcode`:

1. Column on `barcode_rules`: nullable `gs1_company_prefix_length`, required by GS1 rule validation and used to split SGTIN EPC URI values.
2. Columns on `label_print_items`: `gtin`, `lot_no`, `serial_number`, `epc_uri`.
3. Columns on `scan_records`: `gtin`, `lot_no`, `serial_number`, `quantity`, `business_action`, `downstream_event_id`.
4. Table `epcis_events`: event id, organization/environment, event type, action, business step, disposition, epc/label value, GTIN, lot, serial, source document, source workflow, scan record id, print item id and occurred time.

No cross-schema foreign keys are introduced.

## Testing

Acceptance requires:

1. Domain tests for GS1 mod-10 check digit generation and AI value generation/parsing.
2. Domain tests proving serialized print items create commissioning EPCIS events.
3. Command tests proving accepted inventory scans publish an `InventoryMovementRequestedIntegrationEvent` with parsed lot/serial.
4. Command tests proving unsupported workflow values and missing inventory context fail.
5. Idempotency tests proving duplicate scan replays do not duplicate EPCIS events.
6. Schema convention tests covering new columns/table comments and `barcode.__EFMigrationsHistory`.
7. Existing BarcodeLabel verification script and focused tests pass.

## Self-Review

No placeholders remain. The scope is intentionally limited to BarcodeLabel plus Inventory's existing event contract; MES/WMS/Quality consumers are documented follow-up routes rather than implied by UI behavior.
