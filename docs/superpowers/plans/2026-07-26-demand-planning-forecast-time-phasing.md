# DemandPlanning Forecast Time-Phasing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Time-phase forecast demand across inclusive daily buckets for
MAN-426 / GitHub #774 without changing ordinary sales-order demand behavior.

**Architecture:** Convert each forecast's consumed remaining quantity to
six-decimal micro-units, derive deterministic daily quantities from rounded
cumulative targets across the full inclusive forecast period, and return only
the daily facts inside the requested inclusive horizon. Load forecast-consuming
facts across the complete configured consumption window so adjacent horizon
runs share the same remaining-quantity basis.

**Tech Stack:** .NET 10, EF Core, xUnit, PostgreSQL 18, NetCorePal CleanDDD
patterns, VitePress product docs.

## Global Constraints

- Starting commit and review base are
  `19265d5f45b912deec202fc297cffd6162725675` from
  `origin/codex/man-425-mrp-safety-stock`.
- Read root `AGENTS.md`, all of `docs/architecture/implementation-readiness.md`,
  the design spec beside this plan, DemandPlanning/MRP/database/product docs,
  and any nearest `AGENTS.md` before editing.
- Use strict RED-GREEN-REFACTOR and record the exact expected RED failures
  before production edits.
- Preserve planning-UOM conversion and the existing period-level
  backward/forward forecast consumption definition.
- Preserve ordinary sales-order/MPS output quantities and dates; auxiliary
  out-of-horizon reads may only support stable forecast consumption.
- Use inclusive `DateOnly` day buckets. Do not introduce timezone conversion,
  week buckets, raw SQL, cross-schema reads, or provider-specific production
  APIs.
- Quantity facts must be non-negative, deterministically ordered, stable across
  repeated reads, and exactly conserved at six decimal places across adjacent
  horizons.
- Keep changes focused on DemandPlanning adapter/tests and planner-visible docs.
- No endpoint/contract/schema/migration change is expected. If none occurs,
  state facade/OpenAPI/generated-client/migration impact as not applicable.
- Do not run `dotnet test backend/Nerv.IIP.sln`; MAN-253 owns that verification
  slot until the coordinator explicitly releases it.
- Do not push, open a PR, merge, or update Linear. The controller owns
  publication and tracker updates.

---

### Task 1: Replace Forecast Clamp with Conserving Daily Time-Phasing

**Files:**

- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs`
- Modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/PlanningInputAdapterTests.cs`
- Create or modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/*Forecast*PostgresTests.cs`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `frontend/apps/docs/docs/getting-started/planning-to-finished-goods.md`
- Modify: `frontend/apps/docs/docs/roles/planner.md`

**Interfaces:**

- Consumes: `ForecastInput`, `DemandSnapshot`, `PlanningParameterSnapshot`,
  `UomConversionSnapshot`, active sales/sales-order demand, released MPS facts.
- Produces: the existing `DemandSnapshot` shape only; each positive forecast
  snapshot has the original forecast reference, planning UOM, site, daily
  `DateOnly` due date, and a six-decimal conserved quantity.

- [ ] **Step 1: Write RED adapter tests**

  Add literal, hand-derived cases for:

  ```text
  full inside:       forecast 2026-07-01..07-03 quantity 3, horizon same
                     => three daily facts of 1, total 3
  left crossing:     forecast 2026-06-29..07-02 quantity 4, July horizon
                     => 07-01 and 07-02 only, total 2
  right crossing:    forecast 2026-07-01..09-30 quantity 90, July horizon
                     => 31 daily facts, total 30.326087 (not 90 @ 07-31)
  covers horizon:    forecast 2026-06-01..08-31 quantity 92, July horizon
                     => 31 daily facts, total 31
  fully outside:     no forecast fact
  single day:        one fact with the complete quantity
  leap/month edge:   2024-02-28..03-01 split across Feb and Mar horizons
  indivisible:       quantity 1 over three days => daily values conserve
                     1.000000 exactly at six decimals
  adjacent horizons: union of all slices equals the stable remaining forecast
                     after an active consuming demand in the full configured
                     window
  unchanged demand:  sales-order quantity and due date are byte-for-byte
                     equivalent to the existing output
  repeat read:       the ordered forecast fact sequence is identical
  ```

  Assert every returned forecast quantity is positive, no date falls outside
  the horizon, and no `(forecastReference, dueDate)` pair is duplicated.

- [ ] **Step 2: Run RED and record the old clamp failure**

  Run only the new/changed time-phasing tests:

  ```bash
  dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~Forecast_time_phasing|FullyQualifiedName~cross_horizon" --logger "console;verbosity=normal" --nologo
  ```

  Expected: the right-crossing assertion receives one `90` fact at
  `2026-07-31`; daily-count, overlap-ratio, leap/month, adjacent-horizon, and
  conservation assertions fail because `ClampForecastDueDate` has no
  time-phasing.

- [ ] **Step 3: Implement the minimum provider-neutral phasing**

  Replace `ClampForecastDueDate` with a private, deterministic helper that:

  1. clamps only the generated date range to the overlap of forecast period and
     horizon;
  2. normalizes positive remaining forecast quantity to six decimal places;
  3. calculates each overlap day's amount from rounded cumulative micro-unit
     targets over the complete inclusive forecast period;
  4. omits zero daily facts and never emits a negative quantity.

  Query forecast-consuming sales/sales-order and released MPS facts over the
  union of complete configured consumption windows. Keep a separately filtered
  in-horizon output collection so normal demand facts do not leak across the
  horizon. Apply the same daily helper in the initial seed pass without changing
  its discovery-only responsibility.

- [ ] **Step 4: Run GREEN and existing adapter regressions**

  Re-run the RED command, then:

  ```bash
  dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter "FullyQualifiedName~PlanningInputAdapterTests" --logger "console;verbosity=minimal" --nologo
  ```

  Preserve the existing forecast-consumption, planning-UOM, sales-order, MPS,
  optional-source, and deterministic ordering tests.

- [ ] **Step 5: Add PostgreSQL 18 evidence**

  Reuse `DemandPlanningRealPostgresFactAttribute` and
  `PostgreSqlTestDatabase`. Migrate a fresh database, persist a forecast plus
  consuming-demand facts, query through
  `DemandPlanningUpstreamInputSnapshotProvider`, clear tracking, and assert the
  same daily dates, six-decimal totals, adjacent-horizon conservation, and
  unchanged ordinary demand behavior. Do not add raw SQL or provider-specific
  production code.

- [ ] **Step 6: Update planner-visible and engineering docs**

  Record the inclusive daily formula, consumption order, exact quantity
  conservation, multi-horizon behavior, PostgreSQL evidence, and the explicit
  lack of endpoint/contract/schema/migration impact in implementation readiness.
  In the planner tutorial and role path, explain that forecast period totals are
  spread by calendar day and only the requested horizon's share enters MRP.

- [ ] **Step 7: Verify, self-review, and commit**

  Run:

  ```bash
  dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj --no-restore --nologo
  dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --nologo
  pwsh -NoProfile -File scripts/verify-business-demand-planning-mrp-mvp.ps1 -SkipRestore
  pnpm -C frontend --filter @nerv-iip/docs typecheck
  pnpm -C frontend --filter @nerv-iip/docs test
  pnpm -C frontend --filter @nerv-iip/docs build
  pnpm -C frontend exec vp fmt --check apps/docs/docs/getting-started/planning-to-finished-goods.md apps/docs/docs/roles/planner.md
  git diff --check
  ```

  Review inclusive/exclusive dates, daily grain, proportional crossings,
  rounding and quantity conservation, adjacent horizons, DateOnly/timezone
  neutrality, non-negative outputs, repeated-read determinism, ordinary demand
  preservation, EF query translation, and scope. Commit only intended files and
  write the requested SDD report.
