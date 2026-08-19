# DemandPlanning MRP Safety Stock Replenishment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make MRP actively replenish an uncovered safety-stock deficit for MAN-425 / GitHub #773.

**Architecture:** Extend the pure calculator's stock-position netting so the
first applicable bucket includes the uncovered safety deficit, consumes
scheduled receipts once across demand and safety, and records the floor as
covered for later buckets. Queue a safety-only calculation only for planning
parameters never reached by demand or BOM explosion.

**Tech Stack:** .NET 10, xUnit, EF Core PostgreSQL, NetCorePal CleanDDD patterns.

## Global Constraints

- Read root `AGENTS.md`, all of `docs/architecture/implementation-readiness.md`,
  the design spec beside this plan, DemandPlanning architecture/spec documents,
  and any nearest `AGENTS.md` before editing.
- DemandPlanning is the only service in scope; do not cross schemas, use raw SQL,
  or reference provider APIs from Domain/Application/Endpoint code.
- Preserve daily buckets, make/buy, lead time, lot sizing, UOM conversion,
  scheduled-receipt exceptions, BOM explosion, and existing pegging contracts.
- Use strict RED-GREEN-REFACTOR. Record the exact expected RED failures before
  production edits.
- Keep one focused implementation commit. Do not push, open a PR, or merge.
- No endpoint/contract/schema/migration changes unless the evidence proves they
  are necessary; if none occur, state that explicitly in the report.

---

### Task 1: Correct Safety-Stock Netting and Prove It End to End

**Files:**

- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs`
- Modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/MrpCalculatorTests.cs`
- Create or modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/*Postgres*Tests.cs`
- Modify: `docs/architecture/implementation-readiness.md`

**Interfaces:**

- Consumes: `MrpCalculationInput`, `PlanningParameterSnapshot`,
  `InventoryAvailabilitySnapshot`, `ScheduledReceiptSnapshot`, existing UOM
  conversion and lot-sizing rules.
- Produces: existing `CalculatedPlanningSuggestion` and
  `CalculatedNetRequirementExplanation` shapes only; no public signature change.

- [ ] **Step 1: Write the failing calculator tests**

  Change the existing 8/12/10 test to expect `netRequirementQuantity == 14`,
  `plannedQuantity == 14`, and a formula that shows the four-unit safety deficit.
  Add literal, hand-derived cases for:

  ```text
  zero demand: available 2, safety 5, receipts 0 => planned 3
  partial receipt: demand 10, available 8, safety 12, receipt 3 => planned 11
  full receipt: demand 10, available 8, safety 12, receipt 14 => planned 0
  multiple dates: available 0, safety 2, demands 3 then 4 => planned 5 then 4
  precision: use existing UOM conversion/rounding inputs and assert exact decimal
             totals plus quantity >= 0 for every suggestion/pegging link
  ```

  Preserve or strengthen the existing cancel test so receipt quantities reserved
  for safety are not emitted as cancel suggestions.

- [ ] **Step 2: Run RED and record the expected failures**

  Run:

  ```bash
  dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MrpCalculatorTests --logger "console;verbosity=normal"
  ```

  Expected: the new 14-unit, zero-demand, partial-receipt, and multi-period
  assertions fail because current code protects but does not replenish safety.

- [ ] **Step 3: Implement the minimum stock-position change**

  Keep all arithmetic in `decimal`. Track the initial uncovered safety quantity
  separately from gross demand. Consume each scheduled-receipt remainder at most
  once across demand and safety. Include the uncovered residual in `Shortage`,
  expose the initial deficit to the formula, and mark the item floor covered so
  later buckets do not repeat it. Reuse the main planning path for items with
  safety parameters but no demand/BOM requirement.

- [ ] **Step 4: Run GREEN and refactor without behavior expansion**

  Re-run the focused calculator command. Keep the deterministic 8/19 fixture,
  safety-floor, scheduled-receipt exception, cancel-protection, lot-sizing, BOM,
  and UOM tests green.

- [ ] **Step 5: Add real PostgreSQL persistence evidence**

  Reuse the existing temporary PostgreSQL database convention to migrate a fresh
  DemandPlanning database, run `RunMrpCommandHandler` with 8 available / 12
  safety / 10 demand, save the Unit of Work, clear tracking, and read back one
  new-supply suggestion with quantity 14 and its safety-aware explanation.
  Gate it with the repository's `NERV_IIP_TEST_POSTGRES` convention.

- [ ] **Step 6: Update readiness and verify the complete scope**

  Document the corrected formula, once-per-run floor behavior, partial/full
  receipt behavior, PostgreSQL evidence, and the explicit lack of
  endpoint/contract/schema/migration impact.

  Run:

  ```bash
  dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests.csproj --no-restore
  dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore
  pwsh -NoProfile -File scripts/verify-business-demand-planning-mrp-mvp.ps1 -SkipRestore
  dotnet test backend/Nerv.IIP.sln --no-restore
  git diff --check
  ```

- [ ] **Step 7: Self-review and commit**

  Review the diff for formula correctness, bucket ordering, scheduled-receipt
  double-use, UOM/rounding, non-negative invariants, endpoint/schema scope, and
  unrelated changes. Commit only the intended files with a terse message and
  write the implementation/test report to the assigned SDD report file.
