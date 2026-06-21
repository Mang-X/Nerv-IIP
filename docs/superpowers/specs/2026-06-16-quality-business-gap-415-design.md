# Quality Business Gap #415 Design

## Goal

Close the P0/P1 Quality business gaps from GitHub issue #415 without moving Inventory, MES, ERP or MasterData facts into Quality.

## Architecture

Quality remains the owner of inspection specifications, inspection execution, NCR/MRB review facts and CAPA facts. Inventory remains the owner of stock ledger balances and quality status dimensions. Quality publishes enriched public integration events; Inventory consumes those events and posts service-local transfer movements to release or quarantine stock.

The change is additive and compatible with the existing MVP APIs:

1. Existing inspection plan creation still accepts `samplingRule`, but can now also carry structured characteristic specifications and AQL sampling parameters.
2. Existing inspection record creation still accepts manually decided line results, but when `inspectionPlanId` is supplied the handler loads the active plan, validates required characteristics, and calculates line/final results from specification limits or AQL accept/reject numbers.
3. Existing NCR close commands still record downstream execution references, but disposition now records MRB review participants and the event payload exposes those review facts.
4. CAPA is introduced as a Quality aggregate linked to NCRs; it supports containment, corrective/preventive actions, effectiveness verification and closure.
5. Inventory stock status changes are driven only by Quality integration events consumed inside Inventory. Quality never references Inventory Domain, Infrastructure or database objects.

## Scope

In scope for this issue:

1. Inspection plan characteristic type, nominal value, lower/upper specification limits and unit.
2. Structured AQL sampling fields: inspection level, AQL, sample size, acceptance number and rejection number.
3. Numeric measured value on inspection result lines and automatic pass/fail calculation for planned inspections.
4. Plan coverage validation for required characteristics and source category matching.
5. Public Quality inspection event payload enrichment with stock release dimensions needed by Inventory.
6. Inventory consumer for `quality.InspectionPassed` and `quality.InspectionRejected`, posting public status transfer commands from `quality` to `unrestricted` or `blocked`. When Quality supplies stock release dimensions, Inventory uses them to target the exact ledger; otherwise it keeps the #412 single matching `quality` ledger fallback.
7. NCR disposition MRB review records and event payload fields.
8. CAPA aggregate, persistence and internal service API.
9. Schema catalog/readiness/API contract docs updates and focused verification.

Explicitly out of scope:

1. SPC/Cp/Cpk and control chart calculation. This change stores numeric measured values so SPC can be added later.
2. SCAR, COA comparison and gauge calibration/MSA. COA attachment references continue to use FileStorage IDs; calibration remains a future Quality/Maintenance integration.
3. Creating MES rework orders, ERP return documents or Inventory scrap movements from Quality commands. This design exposes disposition/MRB facts through events; downstream document creation remains owned by those services.

## Business Rules

### Inspection Specification

1. `variable` characteristics require at least one specification limit or a nominal value. If lower and upper limits are present, lower must not exceed upper.
2. A measured value below lower limit or above upper limit fails. A value within limits passes.
3. `attribute` characteristics do not require a measured value. They use defect quantity and AQL sampling numbers when structured sampling is configured.
4. Required plan characteristics must be present in a planned inspection record.
5. Planned records must use an active plan whose category matches the record source type.

### AQL Sampling

1. Sampling parameters are stored per characteristic because inspection strictness can differ by characteristic.
2. If sample size is configured, `InspectedQuantity` must be at least the configured sample size.
3. Attribute defect quantity less than or equal to `AcceptanceNumber` passes.
4. Attribute defect quantity greater than or equal to `RejectionNumber` fails.
5. Ambiguous values between acceptance and rejection become `conditional-release` and require a disposition reason.

### Inventory Release

1. Quality event payloads include optional stock release dimensions: UOM, site, location, lot, serial, source quality status, owner type and owner id.
2. Inventory ignores Quality inspection events that lack stock release dimensions.
3. Passed inspections transfer inspected quantity from `quality` to `unrestricted`.
4. Rejected inspections transfer inspected quantity from `quality` to `blocked`.
5. Inventory uses deterministic idempotency keys derived from the Quality event id and target status.

### NCR/MRB/CAPA

1. NCR disposition records MRB review decisions as immutable review entries with reviewer id, decision, comment and reviewed time.
2. Rework, scrap, return-to-supplier and conditional-release dispositions require at least one MRB review entry.
3. CAPA can be opened from an NCR, records root cause, containment, corrective and preventive actions, and must pass effectiveness verification before closure.
4. Closing an NCR does not automatically close CAPA. CAPA lifecycle is tracked independently but linked to source NCR ids.

## Testing

1. Quality domain tests cover specification limit calculation, AQL pass/reject/conditional logic, required characteristic coverage, MRB review requirements and CAPA lifecycle.
2. Quality contract tests cover enriched event JSON shape and compatibility with existing event names.
3. Inventory consumer tests cover passed/rejected Quality events creating paired transfer movements and idempotent duplicate handling.
4. Schema convention tests cover new Quality tables/columns and migrations history schema.
5. Focused service tests run for Quality Domain/Web, Inventory Web, Contracts Quality and Contracts IntegrationEvents where touched.
