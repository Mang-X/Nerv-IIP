# DemandPlanning MRP Safety Stock Replenishment Design

## Goal

Close MAN-425 / GitHub #773 by making MRP actively plan the uncovered safety-stock
deficit while preserving the existing daily-bucket, scheduled-receipt, lot-sizing,
UOM-conversion, make/buy, BOM-explosion, exception-message, and pegging semantics.

## Existing Contract

- MasterData owns the default safety-stock quantity; DemandPlanning consumes it as
  an immutable `PlanningParameterSnapshot`.
- Safety stock is a stock-position floor, not repeatable gross demand in every
  date bucket.
- A planned work-order or planned-purchase suggestion remains a DemandPlanning
  fact until MES or ERP accepts it.
- The calculator is pure and must not read another schema, use raw SQL, or depend
  on provider-specific APIs.
- `ProtectSafetyStockWithRemainingReceipts` currently prevents required receipts
  from being cancelled, but `ConsumeSupply` does not add an uncovered safety
  deficit to the planned net requirement.

## Considered Approaches

### 1. Add a synthetic safety-stock demand to every date bucket

Rejected. It repeats the floor across buckets and can double-plan safety stock.

### 2. Add a safety-only post-processing suggestion

Rejected as the primary path. It leaves a normal demand bucket under-planned
(the existing 8 on hand / 12 safety / 10 demand case would still calculate 10)
and separates one stock-position decision into unrelated suggestions.

### 3. Extend stock-position netting and add a no-demand fallback

Selected. The first real requirement bucket for an item includes the currently
uncovered safety deficit. Scheduled receipts may cover demand and the deficit;
only the remaining combined shortage becomes new planned supply. Once that
bucket has planned or received enough supply to restore the floor, later buckets
must not add the same safety deficit again. Items that never enter a demand or
BOM bucket receive one safety-only calculation at the horizon start through the
same make/buy, lead-time, lot-sizing, UOM, BOM, explanation, and persistence path.

## Formula and State

For the first applicable bucket of an item:

```text
initialSafetyDeficit = max(0, safetyStock - projectedAvailable)
netRequirement =
    grossDemand
    - availableAboveSafetyUsed
    - scheduledReceiptsUsedForDemandOrSafety
    + initialSafetyDeficit
```

The result is clamped to zero. Scheduled receipts are consumed once; a receipt
quantity used to restore safety cannot also satisfy demand or become a cancel
exception. A late receipt used for either purpose retains the existing
`reschedule-in` semantics. After existing or planned supply covers the floor, the
item is marked safety-covered for the remaining buckets in the run.

The explanation keeps gross demand separate from the safety deficit and names
`safety-stock` as the primary source for a safety-only suggestion. No negative
quantity may reach a suggestion, pegging link, remaining receipt, or explanation.
All arithmetic remains `decimal`; existing UOM conversion and rounding rules run
before safety-stock netting.

## Required Regression Coverage

1. Existing 8 available / 12 safety / 10 demand produces one new-supply net
   requirement of 14.
2. Zero gross demand with available below safety produces the appropriate
   make/buy planned suggestion.
3. Scheduled receipts fully cover, partially cover, or do not cover the deficit
   without double-use or erroneous cancellation.
4. Multiple daily buckets restore safety once and then plan only later demand.
5. Decimal/UOM precision is preserved and all boundary quantities remain
   non-negative.
6. The existing safety-floor, cancel-protection, scheduled-receipt exception,
   deterministic 8/19 fixture, lot-sizing, BOM, and UOM tests stay green.
7. A real PostgreSQL profile persists and reads back the corrected planned
   quantity through the existing `RunMrpCommandHandler` path.

## Change Surface

Expected production change is confined to
`Application/Planning/MrpCalculator.cs`. Tests may add a focused PostgreSQL
acceptance file and reuse the repository's temporary-database pattern. Update
`docs/architecture/implementation-readiness.md` with the delivered formula and
evidence.

No HTTP endpoint, public contract, facade declaration, OpenAPI snapshot,
generated client, schema, or migration change is expected.
