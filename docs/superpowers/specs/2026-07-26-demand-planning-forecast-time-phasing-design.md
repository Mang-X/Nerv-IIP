# DemandPlanning Forecast Time-Phasing Design

## Goal

Close MAN-426 / GitHub #774 by replacing the forecast horizon-end clamp with
deterministic daily time-phasing. A forecast contributes only the quantity
belonging to the requested horizon, while all horizon slices together conserve
the consumed forecast quantity exactly.

## Existing Contract

- A `ForecastInput` quantity belongs to the whole interval from
  `PeriodStartDate` through `PeriodEndDate`. The domain accepts a one-day
  interval and rejects only `end < start`, so both endpoints are inclusive.
- MRP horizons are inclusive (`>= HorizonStart` and `<= HorizonEnd`) and the
  existing MPS input is a daily `DateOnly` bucket. There is no week-bucket or
  timezone contract to infer.
- Forecast quantity is normalized into the planning UOM before consumption.
  Active sales-order/sales demand and released MPS facts inside the configured
  forecast consumption window consume the forecast; ordinary demand facts keep
  their own quantity and due date.
- Forecast and downstream quantity columns use `decimal(18,6)`. Time-phasing
  must therefore conserve six-decimal quantity without depending on a database
  provider's rounding behavior.
- The current `ClampForecastDueDate` keeps an overlapping forecast visible but
  assigns the entire remaining period quantity to `horizonEnd`. For a
  2026-07-01 through 2026-09-30 forecast of 90, the July horizon currently
  produces `90 @ 2026-07-31`.

## Considered Approaches

### 1. Scale one aggregate demand to the horizon-overlap ratio

This fixes overstatement, but it preserves a single horizon-end due date and
does not use the existing daily planning grain.

### 2. Divide with unrestricted decimal precision and put the residue on one day

This looks simple in memory, but each persisted daily fact is rounded to six
decimal places. Independent rounding can lose quantity, while assigning all
residue to the last day recreates a smaller horizon-end lump.

### 3. Daily cumulative balanced allocation at six-decimal precision

Selected. Normalize the remaining forecast to integer micro-units. For day
offset `d`, calculate the rounded cumulative target
`round(totalUnits * d / inclusiveDayCount)` and subtract the previous
cumulative target. This creates non-negative daily facts, spreads indivisible
micro-units through the period, makes every horizon slice deterministic, and
guarantees that the complete period sums to the normalized remaining quantity.

## Formula and Data Flow

For each forecast:

```text
forecastQuantity = planning-UOM conversion(forecast.quantity)
consumedQuantity = sum(active sales/sales-order and released MPS quantities
                       in [periodStart - backwardDays,
                           periodEnd + forwardDays])
remainingQuantity = round6(max(0, forecastQuantity - consumedQuantity))

inclusiveDayCount = periodEnd - periodStart + 1
totalUnits = remainingQuantity * 1_000_000
cumulative(d) = round(totalUnits * d / inclusiveDayCount)
dailyUnits(d) = cumulative(d + 1) - cumulative(d)
dailyQuantity(d) = dailyUnits(d) / 1_000_000
```

Only positive daily facts whose due date is inside the inclusive MRP horizon are
returned. Consumption facts are loaded for the complete configured consumption
window, not merely the current horizon. That keeps `remainingQuantity` stable
when the same forecast is viewed through adjacent horizons; ordinary
sales-order/MPS demands are still returned only when their own due dates are in
the requested horizon.

The initial seed pass uses the same daily phasing for overlapping forecast
coverage but remains only a SKU/UOM/site discovery pass. The final pass remains
authoritative after planning-UOM conversion and forecast consumption.

## Required Regression Coverage

1. Forecast completely inside the horizon.
2. Forecast crossing the horizon's left edge.
3. Forecast crossing the horizon's right edge and proving the old full-quantity
   clamp failure.
4. Forecast covering the entire horizon.
5. Forecast completely outside the horizon.
6. One-day forecast (`start == end`).
7. Leap day and month-boundary allocation.
8. Quantity not divisible by the inclusive day count, with exact six-decimal
   conservation and no negative/zero returned facts.
9. Adjacent horizons whose combined facts equal the same remaining forecast
   quantity, including a consuming demand outside one slice.
10. Ordinary sales-order quantity and due date remain unchanged.
11. Repeated snapshot reads produce the same ordered facts without duplication.
12. PostgreSQL 18 migration/query evidence matches the provider-neutral tests.

## Change Surface

Expected production changes are confined to
`Application/Planning/PlanningInputAdapters.cs`. Adapter tests may gain a
focused PostgreSQL case using the existing `NERV_IIP_TEST_POSTGRES` convention.
Update implementation readiness and the planner-facing product guide/role page
to explain the visible daily forecast behavior.

No HTTP endpoint, facade declaration, public contract, OpenAPI snapshot,
generated client, database schema, or migration change is expected.
