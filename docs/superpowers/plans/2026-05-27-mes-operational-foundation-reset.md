# MES Operational Foundation Reset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebase MES PC delivery on a real operational foundation so the shock absorber manufacturing flow can run from demand, engineering and supply readiness through work order release, dispatch, reporting, receipt and traceability.

**Architecture:** Stop treating MES pages as the first deliverable. Build source facts and server-side business behavior first, expose them through BusinessGateway, then implement Chinese PC pages that guide real users through linked workflows. MES owns execution facts only; MasterData, ProductEngineering, DemandPlanning, Scheduling/APS lite, ERP, Inventory, WMS, Quality, BarcodeLabel, Maintenance and IndustrialTelemetry remain the fact owners for their own domains.

**Tech Stack:** .NET 10, FastEndpoints, CleanDDD, EF Core PostgreSQL, BusinessGateway facade, Hey API generated `@nerv-iip/api-client`, Vue 3, Vite Plus, Pinia Colada, `@nerv-iip/ui`, Playwright.

---

## Rebaseline Decision

The 2026-05-26 PC workbench plan gave MES a broad page and facade surface, but it is not enough for delivery. A usable MES cannot start from work order CRUD or static page data. It requires released engineering facts, valid master data, available materials, purchasable supply, MRP suggestions, server-side numbering, release snapshots and execution state transitions.

From this plan onward, MES PC work is gated by a simple rule:

> No page is delivery-ready until the source facts it needs can be maintained or imported, resolved through backend contracts, selected in the UI, and verified through an end-to-end shock absorber manufacturing scenario.

## Worker Review Findings

Two delegated reviews reached the same conclusion:

1. The repository already has more than pages. MasterData, ProductEngineering, DemandPlanning, ERP, Inventory, WMS, Quality, MES, Maintenance and IndustrialTelemetry services exist with real aggregates and endpoint surfaces.
2. The current MES PC workbench still behaves like a contract surface in several P0 areas. Some readiness paths return static `Ready`, production plans can be empty, and several actions return `Accepted` without durable downstream facts.
3. The 2026-05-26 plan mentioned BOM, routing, production versions, MRP, supply, quality, equipment and numbering, but it did not make them the release gate before page completion.

Important current gaps called out by the reviews:

| Gap | Code fact |
| --- | --- |
| Foundation readiness can return static `Ready` instead of checking source facts. | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs` |
| Production plan list/readiness and plan-to-work-order need a durable link to DemandPlanning/ERP suggestions. | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/MesWorkbenchQueries.cs`; `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Workbench/MesWorkbenchCommands.cs` |
| Material readiness and material issue/line-side receipt are not yet a real Inventory/WMS loop. | MES workbench queries and commands under `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/` |
| Quality context, shift handover and batch/material traceability contain empty or shallow responses. | MES workbench query handlers under `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Workbench/` |
| DemandPlanning exists, but current input preparation still needs real ProductEngineering/Inventory/ERP source adapters before it can be the production-plan source. | `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs` |
| Users can still be asked for durable business IDs in several creation flows. | MasterData, MES, ProductEngineering and ERP create requests currently include user-provided document numbers or codes. |

## Shock Absorber P0 Scenario

Use one realistic product family to prove the system can run:

| Layer | Required scenario facts |
| --- | --- |
| Finished goods | Front shock absorber assembly and rear shock absorber assembly. |
| Components | Piston rod, outer tube, piston valve, seal kit, spring seat, shock absorber oil, carton, label and pallet. |
| Suppliers | At least three approved suppliers: machining supplier, seal/oil supplier and packaging supplier. |
| Plant model | One plant, two production lines, four work centers: tube welding, rod assembly, oil filling/sealing, damping test/packing. |
| Engineering | Released MBOM, routing and ProductionVersion for each finished good; operation sequence includes standard duration, required work center, required skill and material demand. |
| Demand | One sales order, one forecast demand and one safety-stock replenishment demand. |
| Planning | MRP creates one planned work order suggestion and at least one planned purchase suggestion with pegging to demand. |
| Procurement | Purchase suggestion can become purchase requisition, RFQ or purchase order; receipt can feed quality/inventory readiness. |
| MES | Accepted production suggestion becomes a work order, checks readiness, releases a snapshot, creates material issue request, dispatches operations, records production, creates finished-goods receipt and supports traceability. |

## P0 Prerequisite Matrix

| Capability | P0 requirement | Owner | Current gap to close |
| --- | --- | --- | --- |
| Server-side numbering | Generate SKU, engineering document, BOM/routing, production version, demand, MRP run, purchase, sales, work order, operation task, report, defect, downtime, handover and receipt request numbers on the server. | Each owning service with shared governance. | No complete rule/counter/concurrency/idempotency strategy. |
| Material master | Maintain product, semi-finished, raw material and packaging records with role flags, traceability, UOM and quality requirements. | MasterData | Business Console exposes only a narrow SKU page and still asks users for code. |
| Partner master | Maintain customer and supplier records, roles, qualification and active status. | MasterData / ERP | Supplier/customer pages and linked selectors are missing or not prominent. |
| Plant/resource master | Maintain plant, line, work center, device, shift, calendar, team, skill and resource capability. | MasterData | Backend exists, but UI, seed data and form linkages are incomplete. |
| Engineering release | Maintain and release EBOM, MBOM, routing, operation definitions and ProductionVersion. | ProductEngineering | Backend exists; Business Console lacks a usable engineering workbench and MES does not yet force released snapshots in every path. |
| Demand and MRP | Create sales/forecast/safety-stock demand, run MRP, show pegging and accept planned purchase/work-order suggestions. | DemandPlanning / ERP Sales | MRP exists but source adapters and UI workflow are not complete. |
| Procurement supply | Convert purchase suggestions to procurement documents, track supplier quotation, purchase order and receipt readiness. | ERP Procurement / WMS / Inventory / Quality | ERP backend exists; PC pages and MES readiness links are incomplete. |
| Inventory and line-side supply | Show available quantity, quality status, staging route, material issue request and line-side receipt. | Inventory / WMS / MES | MES material readiness currently needs real BOM and Inventory/WMS linkage. |
| Quality gate | Resolve inspection plans, first-piece/in-process/final inspection, quality holds and NCR context. | Quality / MES | MES currently needs stronger quality readiness and drill-down. |
| Equipment availability | Resolve static device capability plus runtime maintenance/alarm/downtime availability. | MasterData / Maintenance / IndustrialTelemetry / MES | Maintenance/telemetry facts exist but PC readiness linkage is incomplete. |
| MES lifecycle | Convert accepted plan to work order, release snapshot, dispatch, start/pause/resume/complete, report, receipt, trace. | MES | Must stop accepting free-text work order IDs and fill sparse query handlers with durable facts. |
| APS lite | Define scheduling input/output contracts, finite-capacity heuristic scheduling, resource load, conflict explanation, locked tasks and rush insertion. | Scheduling / MES / DemandPlanning / IndustrialTelemetry / Maintenance | P0 now includes #206 scheduling core and #207 equipment runtime facts. Full optimizer, simulation and auto-reschedule remain later. |

## Delivery Phases

### P0-A: Operational Foundation Gate

**Goal:** Make source facts visible and enforce the "no backend, no page completion" rule.

**Files:**
- Modify: `docs/superpowers/plans/2026-05-26-business-console-mes-pc-completion.md`
- Modify: `frontend/DESIGN/roadmaps/business-console-mes-pc-workbench.md`
- Create: `docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.md`
- Create: `docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.html`

- [ ] **Step 1: Freeze this rebaseline**

Add a notice to the previous 2026-05-26 plan saying it is superseded for future MES work by this operational-foundation plan.

- [ ] **Step 2: Add the UI delivery gate to DESIGN**

Add a rule that MES pages must be backed by source facts, linked selectors, server-side numbering and Chinese business copy before they can be counted as delivered.

- [ ] **Step 3: Verify docs**

Run:

```powershell
rg -n "待确认|待补充" docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.md docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.html frontend/DESIGN/roadmaps/business-console-mes-pc-workbench.md
```

Expected: no unresolved placeholder is introduced. Product-copy forbidden examples remain documented in DESIGN as negative examples, not product UI text.

### P0-B: Numbering and Idempotent Creation

**Goal:** Remove user-generated system IDs from all create flows that produce durable business documents.

**Files:**
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/MasterData/MasterDataEndpoints.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Commands/MasterData/CreateMasterDataCommands.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/WorkOrders/CreateRushWorkOrderCommand.cs`
- Modify: equivalent create command files in ProductEngineering, DemandPlanning and ERP where user-provided numbers are currently required.
- Test: service-level endpoint and concurrency tests under each affected service.

- [ ] **Step 1: Add service-local numbering rule and counter aggregates**

Create per-service numbering rule and counter tables. Scope counters by organization, environment, document type, optional site/plant prefix and date segment. Use optimistic concurrency or row-level locking in Infrastructure; keep unique indexes on final document numbers.

- [ ] **Step 2: Generate IDs inside the same transaction as document creation**

Creation commands must allocate a number and persist the business document in one unit of work. UI requests may include an idempotency key; they must not include system IDs except privileged import/override paths.

- [ ] **Step 3: Add duplicate and concurrency tests**

Test 20 parallel create requests for SKU and MES work orders. Expected: all persisted system numbers are unique, ordered within rule scope, and retries do not create duplicate documents for the same idempotency key.

### P0-C: Master Data Workbench and Seed Scenario

**Goal:** Let a business user maintain the plant, resources, materials and partners needed by MES.

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/MasterData/BusinessConsoleMasterDataEndpoints.cs`
- Modify: `frontend/apps/business-console/src/pages/master-data/skus.vue`
- Create or complete: Business Console pages for partners, resources, calendars, teams, skills and devices.
- Create: deterministic seed/import fixture for the shock absorber scenario.

- [ ] **Step 1: Replace manual code fields with server-generated IDs**

Remove "generate" buttons and ordinary user entry for system codes. Use business labels and selectors for material type, UOM, traceability, plant, line, work center, shift and supplier/customer roles.

- [ ] **Step 2: Complete linked selectors**

Forms must select from actual MasterData resources. Work center filters must depend on plant/line; device filters must depend on work center; team/shift filters must depend on effective date.

- [ ] **Step 3: Seed the shock absorber foundation**

Seed at least finished goods, raw materials, suppliers, plant, lines, work centers, devices, shifts, calendar, teams and skills. The seed must be in backend/dev setup or documented import fixtures, not announced as sample data in the product UI.

### P0-D: Product Engineering Release Workbench

**Goal:** Make BOM, BOM version, routing, operation and ProductionVersion usable before MES work order release.

**Files:**
- Modify: ProductEngineering endpoint/facade coverage as needed.
- Create: Business Console engineering pages for engineering items, EBOM, MBOM, routing and production versions.
- Test: ProductEngineering contract tests and BusinessGateway proxy tests.

- [ ] **Step 1: Expose list/detail/resolve facades for released engineering facts**

BusinessGateway must expose released MBOM, routing, operation sequence and ProductionVersion resolution needed by planning and MES pages.

- [ ] **Step 2: Add release-state UI**

Engineering pages must show draft/released/archived state and prevent MES from choosing draft engineering data.

- [ ] **Step 3: Lock release snapshots on MES work order release**

MES release must store productionVersionId, MBOM version, routing version, operations, material demand and resource capability snapshots.

### P0-E: Demand, MRP and Procurement Readiness

**Goal:** Make production plans come from real demand and MRP suggestions, not ad hoc pages.

**Files:**
- Modify: DemandPlanning input adapters and BusinessGateway facade.
- Modify: ERP procurement/sales facade and pages.
- Modify: Business Console pages under `erp`, `planning` or a clearer domain route.

- [ ] **Step 1: Feed MRP from sales order, forecast and safety stock**

DemandPlanning must accept or import demand sources for the P0 scenario and list pegging links to the original demand.

- [ ] **Step 2: Connect MRP to ProductEngineering and Inventory snapshots**

MRP must resolve ProductionVersion and BOM components and subtract inventory availability before creating planned work-order or purchase suggestions.

- [ ] **Step 3: Create procurement readiness flow**

Planned purchase suggestion can become purchase requisition/order/receipt, with supplier selected from partner master and receipt status visible to MES material readiness.

### P0-F: MES Execution Backbone

**Goal:** Make MES work orders executable only after foundation, engineering, material, quality and equipment readiness pass.

**Files:**
- Modify: MES command/query handlers for production plans, work order release, material readiness, dispatch, operation lifecycle, reporting, receipt, downtime, handover and traceability.
- Modify: BusinessGateway MES facade.
- Modify: Business Console MES pages after backend behavior is complete.

- [ ] **Step 1: Restrict work order sources**

Normal work orders come from accepted planned work-order suggestions or released production plans. Rush orders remain allowed but still require production version, material, quality, equipment and numbering checks.

- [ ] **Step 2: Fill sparse query handlers with durable facts**

Material issue requests, shift handovers, related quality items and traceability queries must read persisted facts or linked service facts. Empty stub responses are not acceptable for delivery.

- [ ] **Step 3: Enforce lifecycle actions**

Release, dispatch, start, pause, resume, complete, report and receipt actions must validate readiness and current state. The UI can expose the action only when the backend returns it as allowed.

### P0-G: PC UI Rebuild Around Workflows

**Goal:** Rework Business Console pages after the backend/data foundation exists.

**Files:**
- Modify: `frontend/apps/business-console/src/pages/**`
- Modify: `frontend/apps/business-console/src/composables/**`
- Modify: shared business components where needed.

- [ ] **Step 1: Rebuild navigation by work role**

Use `主数据`, `工程资料`, `计划与采购`, `生产执行`, `质量与库存`, `设备异常` or equivalent business domains. Do not expose diagnostic pages as primary operator workflows.

- [ ] **Step 2: Replace isolated forms with guided actions**

Main pages show queue, filter, KPI and table/detail. Create/report/confirm actions open from row context and prefill known facts.

- [ ] **Step 3: Verify the P0 scenario in browser**

Using the seeded shock absorber scenario, prove sales/forecast demand -> MRP -> purchase readiness -> production version -> work order -> material issue -> dispatch -> report -> receipt -> traceability. Capture screenshots for review.

### P0-H: Scheduling / APS Lite Core

**Goal:** Make dispatch decisions reproducible before the Gantt view becomes a delivery surface.

**Files:**
- Create or modify: Scheduling/APS contracts for `SchedulingProblem`, `SchedulePlan`, resource load and conflict reasons.
- Modify: DemandPlanning/MES/BusinessGateway integration points once the contract exists.
- Test: deterministic scheduling cases for the shock absorber scenario.

- [ ] **Step 1: Freeze scheduling contracts**

Define schedule input from work orders, operations, released production versions, resources, calendars, material readiness, quality blocks and equipment availability. Output must include assignments, start/end windows, resource loads, conflict reasons and impossible-to-schedule reasons.

- [ ] **Step 2: Implement deterministic finite-capacity scheduling**

The first algorithm is a heuristic, not a solver. It must handle operation precedence, device capacity, shift calendars, maintenance windows, active alarms, locked tasks, due-date priority and rush insertion.

- [ ] **Step 3: Keep Gantt as a consumer**

The Gantt/scheduling UI consumes `SchedulePlan` and sends adjustment intent. It does not calculate the official schedule in the browser.

### P0-I: Equipment IIoT Runtime Facts

**Goal:** Make device state, alarms, downtime and maintenance windows affect APS and MES readiness.

**Files:**
- Modify: IndustrialTelemetry and Maintenance query/event surfaces as needed.
- Modify: MES readiness and Scheduling availability integration once contracts exist.
- Create or modify: Business Console equipment/IIoT pages after backend facts exist.

- [ ] **Step 1: Map device runtime facts**

Device assets, work centers, telemetry tags, state mapping, alarm severity, sampling policy and source sequence must be explicit and idempotent.

- [ ] **Step 2: Expose availability for APS**

Scheduling must be able to query device availability for a time window, including active alarm, downtime, maintenance, inspection and substitute-device context.

- [ ] **Step 3: Expose readiness for MES**

MES release, dispatch and start actions must use the same equipment reason codes as Scheduling, instead of showing device problems only on a separate diagnostics page.

## P1 Scope

1. Richer schedule comparison, visual timeline interaction and dispatch simulation on top of APS lite.
2. Saved views, column visibility, export, batch action and approval handoffs.
3. Advanced quality workflow: first-piece inspection, SPC, NCR detail and CAPA handoff.
4. Tooling/mold lifecycle, preventive maintenance windows and OEE loss tree.
5. Supplier scorecard, lead-time variance and purchasing exception cockpit.

## P2 Scope

1. Solver-grade APS optimization, scenario simulation and automatic rescheduling.
2. PDA/mobile scanning and offline sync.
3. WCS/AGV/AMR automation depth.
4. Full QMS/LIMS and full CMMS/EAM.
5. Finance costing close and full general ledger month-end.

## Acceptance Gate

P0 is complete only when all are true:

1. Users do not manually enter system numbers for normal create flows.
2. All P0 forms use linked selectors or row context instead of free-text IDs.
3. The shock absorber scenario can be created or seeded and then run through MRP, procurement readiness, work order release, dispatch, report, receipt and traceability.
4. MES refuses release/start actions when production version, BOM/routing, material, quality, equipment, calendar, shift, barcode or numbering readiness is blocked.
5. APS lite can produce a deterministic schedule plan or conflict explanation from the P0 work orders, resources, calendars, material readiness and equipment runtime facts.
6. Business Console visible copy is Chinese business copy and contains no developer metadata such as gateway contracts, implementation context or sample-data explanations.
7. Verification includes backend tests, BusinessGateway proxy/authorization tests, generated client refresh, frontend typecheck/test/build and browser screenshots of the P0 scenario.
