# Business Console MES PC Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **2026-05-27 Rebaseline:** This plan delivered a broad PC workbench surface, but it is no longer the canonical next-step plan for MES delivery. Future MES work must follow `docs/superpowers/plans/2026-05-27-mes-operational-foundation-reset.md`, which requires server-side numbering, complete source data, released engineering versions, MRP/procurement readiness, APS lite scheduling contracts, equipment IIoT runtime facts and durable MES execution facts before further page completion work is counted as delivered.

**Goal:** Complete a PC-first, standard MES workbench so production planners, supervisors, shift leaders, material handlers, quality inspectors, and maintenance coordinators can run the real shop-floor loop from production plan readiness through work order release, material readiness, dispatching, operation execution, reporting, quality handling, finished-goods receipt, shift handover, and traceability before starting PDA/mobile work.

**Architecture:** BusinessGateway remains the Business Console BFF and the only frontend-facing API for `/api/business-console/v1/**`; it performs user bearer validation, IAM permission checks, organization/environment context propagation, and internal service-token calls. MES owns shop-floor execution facts: work orders, operation tasks, dispatching, WIP state, production reports, material consumption evidence, downtime events, finished-goods receipt requests, shift handover, and genealogy snapshots. ProductEngineering, DemandPlanning, MasterData, Quality, WMS/Inventory, Maintenance/IndustrialTelemetry, and ERP are integrated through narrow read/action facades where the MES workbench must see or trigger their facts; MES must not take over their source-of-truth responsibilities. The first release uses direct Chinese UI copy in Vue pages and defers a full i18n catalog workflow.

**Tech Stack:** .NET 10, FastEndpoints, CleanDDD service boundaries, BusinessGateway facade, Hey API generated `@nerv-iip/api-client`, Vue 3, Vite Plus, Pinia Colada, `@nerv-iip/ui`, Playwright.

## Implementation Closure — 2026-05-26

This plan is implemented in PR #185 for the PC-first Business Console MES workbench:

- Backend MES now exposes the P0 workbench surface for production plans, readiness, work order release, material issue request, dispatch, operation task lifecycle, WIP, production reports, defects, downtime, receipt requests, shift handover, traceability, schedules, and capacity impacts.
- BusinessGateway exposes the matching `/api/business-console/v1/mes/**` facade routes with narrow IAM permission codes and generated OpenAPI/client coverage.
- Business Console now has Chinese PC routes for `生产驾驶舱`、`基础准备`、`生产计划`、`计划与工单`、`齐套与物料`、`派工看板`、`工序执行`、`报工与完工`、`质量与不良`、`完工入库`、`规则排程`、`设备与停机`、`班次交接`、`追溯查询` 和 `产能影响`。
- `scripts/verify-business-console-mes-pc-workbench.ps1` is the focused verification gate. It covers MES tests, BusinessGateway tests, api-client generation/typecheck/test, and Business Console typecheck/test/build; e2e is opt-in through `-E2E`.
- PDA/mobile remains deferred until these PC contracts stabilize.

---

## Baseline Decision

This plan replaces the mobile/PDA-first next-step assumption with a PC-first business implementation sequence:

1. Finish Business Console desktop pages first, with MES as the first deep workbench.
2. Design MES from the standard manufacturing execution flow, not from the current MVP endpoint list.
3. Start from API/BFF contracts, because MES pages depend on data from MES plus several neighboring business contexts.
4. Advance MES together with the minimum related business interfaces instead of trying to finish every surrounding system.
5. Treat production planning and material staging as MES-visible execution capabilities, while keeping long-horizon planning, warehouse execution, and inventory accounting in their owning services.
6. Defer PDA/mobile until the desktop workflow and generated Business Console contracts are stable.
7. Use Chinese text directly in the first page implementation. The repository has i18n concepts, but the first MES workbench should not pay the cost of a complete translation catalog, locale routing, and copy governance workflow.

## Standard MES Reference Model

The first MES workbench should follow the common MES/MOM shape used by ISA-95 and mature systems rather than only exposing CRUD pages:

| Reference | Relevant design signal |
| --- | --- |
| [ISA-95 / IEC 62264](https://www.isa.org/standards-and-publications/isa-standards/isa-95-standard) | Level 3 Manufacturing Operations Management covers production operations and the interfaces between Level 3 manufacturing systems and Level 4 business systems. Use it to keep ERP planning/finance separate from shop-floor execution. |
| [Siemens Opcenter Execution](https://www.siemens.com/en-gb/products/opcenter/execution/discrete/) | Mature MES emphasizes work orders, materials/components/process changes, production tracking, JIT/JIS material visibility, quality, and traceability. |
| [Siemens Opcenter APS / Planning and Scheduling](https://www.siemens.com/en-us/products/opcenter/production-planning-scheduling-capabilities/) | Advanced planning and finite-capacity scheduling are separate planning/scheduling capabilities. MES should consume or perform short-horizon dispatching, not become full APS in the first release. |
| [SAP Digital Manufacturing](https://www.sap.com/products/scm/digital-manufacturing.html) | Resource orchestration includes live operations planning using warehouse/inventory, quality, labor, and maintenance variables; execution tracks labor, work instructions, scrap, rework, and process controls. |
| [SAP Digital Manufacturing + EWM staging](https://help.sap.com/docs/sap-digital-manufacturing/execution/614d9a19fb28417fbd200cd0c200b75c.html) | MES can trigger material staging requests based on order, dispatching, resource, work center, and production-supply-area context, while EWM/WMS executes warehouse tasks. |
| [Rockwell Plex MES/MOM](https://plex.rockwellautomation.com/en-us/products/manufacturing-execution-system.html) | Mature MES is production management with real-time visibility, quality, inventory/material traceability, barcode scanning, and compliance evidence. |

### P0 MES Core For This Plan

| Capability | MES owns the fact? | First-release expectation |
| --- | --- | --- |
| Production plan readiness and order release | Partly | MES workbench evaluates whether DemandPlanning/ERP suggestions can become executable work orders. Long-horizon plan facts stay in DemandPlanning/ERP. |
| Work order execution | Yes | Work order status, release snapshot, execution state, priority, rush/insert handling, and close/reopen controls. |
| Operation dispatching | Yes | Assign operation tasks to line/work center/device/person/shift; prevent or warn on missing material, quality hold, or unavailable equipment. |
| WIP tracking | Yes | Track current operation, waiting/running/paused/complete/held state, quantity movement between operations, and blocking reasons. |
| Production reporting | Yes | Good, scrap, rework, labor time, machine time, start/end, operator, device, and operation status effects. |
| Material consumption evidence | Yes for execution evidence | Record actual material batch/serial consumption against order/operation. Inventory balances and warehouse tasks stay outside MES. |
| Material readiness and issue request | MES-visible trigger | MES calculates readiness from BOM/routing/work center context and inventory/WMS availability, then creates staging/issue requests for WMS/Inventory execution. |
| Process quality and nonconformance | Partly | MES captures in-process defects and blocks execution; Quality owns inspection standards, NCR lifecycle, and formal disposition. |
| Downtime and equipment impact | Yes for execution event | MES records production-impacting downtime and recovery confirmation; Maintenance owns maintenance work orders and asset lifecycle. |
| Finished-goods receipt request | Yes for production request | MES creates the request after production/quality readiness; WMS/Inventory owns inbound receipt and stock posting. |
| Shift handover | Yes | MES carries unresolved production, material, quality, equipment, and receipt issues across shifts. |
| Genealogy/traceability | Yes as execution evidence | Trace work order, batch/serial, material, operation, person, device, quality, downtime, and receipt links. |

### P1/P2 Not First-Release Core

P1 follow-ups: richer finite-capacity dispatching, line-side inventory details, tooling/mold lifecycle, SPC/Cpk, electronic work instructions version enforcement, Andon escalation, OEE loss-tree analysis, and batch/recipe weighing for process industries.

P2 integrations: full APS optimization, full WMS/AGV/WCS automation, full QMS/LIMS, full CMMS/EAM, SCADA/PLC control, BI/data lake analytics, mobile/PDA scanning, and detailed cost accounting.

## Foundation Readiness Baseline

The MES workbench must not start from work order CRUD. It first needs a production foundation readiness layer that checks whether the core facts required to release and execute a work order exist, are active, and are usable for the selected organization/environment/site/line/date.

### Foundation Ownership

| Foundation area | Source of truth | MES first-release responsibility |
| --- | --- | --- |
| Organization, environment, user, permissions | IAM | Use IDs and permission checks; do not copy IAM roles or memberships into MES. |
| Site, plant, area, line, work center, work station | BusinessMasterData | Resolve and validate the production hierarchy before plan release, dispatch, reporting, and handover. |
| Work calendar, shift, team | BusinessMasterData | Validate that planned start/end, dispatch, report, and handover are inside an active calendar/shift/team context. |
| Personnel business attributes and skills | IAM user ID + BusinessMasterData `PersonnelSkill` | Validate operator/team assignment and skill qualification; MES stores assignment snapshot only. |
| Device asset and resource capability | BusinessMasterData static facts, Maintenance/Telemetry runtime facts | Validate static compatibility and current availability before dispatch; MES records actual device usage and downtime impact. |
| SKU, UOM, UOM conversion, traceability policy | BusinessMasterData | Validate manufacturing-enabled SKU, UOM conversions, batch/serial policy, and default barcode rule before release/reporting. |
| Production version, MBOM, routing, operation definitions | ProductEngineering | Resolve released production version and lock a release snapshot; MES does not edit engineering design facts. |
| Warehouse, production supply area, line-side location, inventory status | WMS/Inventory plus MasterData labels | Validate material availability and staging route; MES creates request intent and records line-side receipt evidence. |
| Inspection standards, inspection plans, quality holds | Quality plus MasterData characteristic definitions | Validate inspection requirements and blocking quality state; MES records execution defect context and links to Quality facts. |
| Maintenance plans, downtime, asset restoration | Maintenance plus IndustrialTelemetry | Validate asset availability and production-impacting maintenance state; MES records shop-floor downtime and recovery confirmation. |
| Barcode rules, labels, scan records | BarcodeLabel | Resolve barcode/label rule references for work order, material lot, product serial, flow card, container, pallet, and inspection labels. |
| Business document numbering | Service-local numbering policy with shared governance; future Numbering service remains optional | Generate stable IDs for MES-owned documents, using a consistent rule contract and collision tests; do not hardcode UI-entered IDs as the long-term source. |

### Minimum Readiness Checks

Every plan-to-work-order or work-order-release path must compute a readiness result with `Ready`, `Warning`, or `Blocked` status and a machine-readable reason code. The first release must cover:

| Readiness check | Blocking examples | Warning examples |
| --- | --- | --- |
| MasterData hierarchy | Plant, line, work center, shift, team, SKU, UOM, or device is missing/disabled. | Work center is active but missing capacity metadata. |
| Calendar and shift | Planned time has no active work calendar or shift. | Planned time crosses shift boundary and needs handover. |
| Personnel and skill | Assigned user lacks required skill/qualification or inactive IAM user reference. | Skill expires soon or manual supervisor confirmation is required. |
| Product engineering | No released production version, MBOM, routing, or operation sequence. | Production version is valid but close to expiry/effective-date change. |
| Material and supply | Required material has no UOM conversion, traceability policy mismatch, no available inventory, or no staging route. | Material partially available, substitute available, or expected receipt date is known. |
| Quality | SKU or operation requires inspection but no inspection plan exists, or source batch is quality-held. | Inspection plan exists but needs first-piece confirmation. |
| Equipment and maintenance | Required device/work center is unavailable, under maintenance, or has active blocking alarm. | Device is available but has scheduled maintenance conflict. |
| Barcode and label | Required barcode/label rule missing for traceable material, serial product, flow card, or receipt label. | Barcode rule exists but template has no printer mapping. |
| Numbering | Required document number rule missing for work order, operation task, material request, report, defect, downtime, receipt request, handover, or traceability event. | Rule exists but prefix sequence is near configured threshold. |

### Foundation Record Contract

Every foundation resolver used by MES must return enough data for both execution decisions and user guidance. Do not return only `true`/`false`.

| Field | Requirement |
| --- | --- |
| `sourceSystem` | One of `IAM`, `MasterData`, `ProductEngineering`, `WMS`, `Inventory`, `Quality`, `Maintenance`, `IndustrialTelemetry`, `BarcodeLabel`, or `MES`. |
| `referenceType` | Stable type name such as `Plant`, `ProductionLine`, `WorkCenter`, `WorkCalendar`, `Shift`, `Team`, `PersonnelSkill`, `DeviceAsset`, `Sku`, `Uom`, `ProductionVersion`, `Mbom`, `Routing`, `InventoryLocation`, `InspectionPlan`, `BarcodeRule`, or `NumberingRule`. |
| `referenceId` | Durable source-system ID; never use display text as the ID. |
| `displayName` | Human-readable Chinese name when available; use source code as the fallback for records without a name. |
| `status` | `Ready`, `Warning`, or `Blocked`; source-specific states must be normalized at the BusinessGateway/MES workbench boundary. |
| `effectiveFromUtc` / `effectiveToUtc` | Required for production version, BOM/routing, calendar, shift, skill qualification, inspection plan, barcode rule, and numbering rule when the source has effectivity. |
| `version` | Required for production version, MBOM, routing, barcode template, and inspection plan when the source has versions. |
| `fixHint` | Short Chinese operator/planner guidance, for example `请先维护该产线的工作日历` or `请发布该物料的生产版本`。 |

### Reason Code Baseline

Use stable reason codes so pages, tests, and later mobile/PDA flows can reuse the same semantics:

| Code | Severity | Meaning |
| --- | --- | --- |
| `MASTERDATA_HIERARCHY_MISSING` | Blocked | Site, plant, area, line, work center, or work station cannot be resolved. |
| `MASTERDATA_REFERENCE_INACTIVE` | Blocked | A required MasterData record exists but is disabled or outside effectivity. |
| `CALENDAR_SHIFT_MISSING` | Blocked | Planned execution time has no active work calendar or shift. |
| `SHIFT_HANDOVER_REQUIRED` | Warning | Planned execution crosses a shift boundary and must create/consume handover context. |
| `PERSONNEL_SKILL_MISSING` | Blocked | Assigned user/team lacks the required skill or qualification. |
| `PERSONNEL_SKILL_EXPIRING` | Warning | Required skill is valid but expires within the configured warning window. |
| `PRODUCTION_VERSION_MISSING` | Blocked | No released production version can be resolved for SKU, site/line, and planned date. |
| `BOM_ROUTING_MISSING` | Blocked | Released production version has no usable MBOM, routing, or operation sequence. |
| `MATERIAL_TRACEABILITY_MISMATCH` | Blocked | Required material traceability policy conflicts with SKU or barcode rules. |
| `MATERIAL_NOT_AVAILABLE` | Blocked | Required material has no available inventory or staged supply route. |
| `MATERIAL_PARTIAL_AVAILABLE` | Warning | Some required material can be issued but shortage remains. |
| `QUALITY_PLAN_MISSING` | Blocked | Required inspection plan or quality standard cannot be resolved. |
| `QUALITY_HOLD_ACTIVE` | Blocked | Related source batch, material, or product is under quality hold. |
| `EQUIPMENT_UNAVAILABLE` | Blocked | Required work center/device is down, under maintenance, or blocked by active alarm. |
| `EQUIPMENT_MAINTENANCE_CONFLICT` | Warning | Device is usable now but conflicts with scheduled maintenance. |
| `BARCODE_RULE_MISSING` | Blocked | Required barcode or label rule cannot be resolved. |
| `LABEL_TEMPLATE_PRINTER_MISSING` | Warning | Label rule exists but no printer/template mapping is configured. |
| `NUMBERING_RULE_MISSING` | Blocked | MES cannot generate the required document number server-side. |
| `NUMBERING_SEQUENCE_NEAR_LIMIT` | Warning | Number sequence is close to its configured limit. |
| `SOURCE_SERVICE_UNAVAILABLE` | Blocked | BusinessGateway cannot reach a required source service, or the source service returns timeout, 5xx, or malformed readiness response. |

### Source Boundary Rule

The `/mes/foundation` page is a readiness and guidance surface, not a foundation-data maintenance module. It may show blocker cards and links to source pages when routes exist, but it must not create MasterData, ProductEngineering, WMS/Inventory, Quality, Maintenance, Telemetry, BarcodeLabel, or IAM records from inside MES. This keeps MES focused on execution and avoids duplicating master-data workflows.

### Source Failure Rule

Foundation readiness is a decision surface, so source-service failures must be visible as production blockers instead of blank pages. For `GET /api/business-console/v1/mes/foundation-readiness`, BusinessGateway must return HTTP 200 with the affected area marked `Blocked` and a `SOURCE_SERVICE_UNAVAILABLE` issue when MasterData, ProductEngineering, WMS/Inventory, Quality, Maintenance/Telemetry, BarcodeLabel, or the MES numbering policy resolver times out, returns 5xx, returns invalid JSON, or omits required readiness fields. IAM authentication/authorization failures remain normal 401/403 responses and must not be converted into readiness issues.

### Snapshot Rule

MES must store immutable execution snapshots when a work order is released or an operation task is dispatched:

1. MasterData snapshot: site/plant/line/work center/work station, shift/team, SKU/UOM, device static identity, resource capability, and personnel skill reference.
2. ProductEngineering snapshot: `productionVersionId`, MBOM ID/version, routing ID/version, operation sequence, required resource capability, standard duration, and material demand summary.
3. Material snapshot: required material, UOM, traceability policy, planned quantity, substitute policy, requested/staged/received quantities, and WMS/Inventory references.
4. Quality snapshot: inspection requirement, first-piece/in-process/final inspection trigger, quality hold state, and related inspection/NCR references.
5. Numbering/barcode snapshot: generated document IDs and barcode/label rule references used for the released execution object.

Snapshots keep historical execution readable. They do not move source-of-truth ownership away from MasterData, ProductEngineering, WMS/Inventory, Quality, Maintenance, or BarcodeLabel.

## Verified Current Code Facts

These facts were checked against the repository on 2026-05-26:

| Area | Current fact | Implication |
| --- | --- | --- |
| Business Console app | `frontend/apps/business-console` exists as the independent Vite app on the BusinessGateway facade. | PC pages can continue without changing the main platform console. |
| Current MES pages | `frontend/apps/business-console/src/pages/mes/work-orders.vue` and `frontend/apps/business-console/src/pages/mes/schedules.vue` exist. | The next work should enhance MES pages rather than create a new app shell. |
| Current MES composable | `frontend/apps/business-console/src/composables/useBusinessMes.ts` consumes generated Business Console APIs. | New page data should be added here or split into MES-specific composables under the same app boundary. |
| BusinessGateway MES facade | `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs` exposes `listBusinessConsoleMesWorkOrders`, `createBusinessConsoleMesRushWorkOrder`, `runBusinessConsoleMesSchedule`, and `recordBusinessConsoleMesProductionReport`. | Business Console currently has only the MVP MES surface. |
| MES service surface | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs` already contains service endpoints for work orders, production reports, finished-goods receipt requests, and capacity impacts. | The first API work can expand BusinessGateway facade coverage before introducing broader domain changes. |
| DemandPlanning and ERP plan context | DemandPlanning exposes demand sources, MRP runs, MRP pegging, and planning suggestions under `/api/business/v1/planning/**`; ERP exposes sales orders and finance source-document drill-down, but there is no verified endpoint literally named `production-plans`. | BusinessGateway should build the MES production-plan facade from verified planning suggestions or ERP sales priority context. When a stable read endpoint is missing, show the MES/source raw ID and record the gap in the implementation PR instead of fabricating data. |
| Existing page copy | Current MES Vue pages still contain English user-visible labels such as `Work orders`, `Create rush work order`, `Run schedule`, and `No work orders returned.` | PC completion must include a Chinese-copy pass. |
| Mobile/PDA | No mobile Business Console client or generated mobile API boundary is present. | Mobile/PDA is not a blocker for PC MES and should start after PC contract stabilization. |

## Business Scope

### In Scope

1. Production foundation readiness checks across MasterData, ProductEngineering, WMS/Inventory, Quality, Maintenance/Telemetry, BarcodeLabel, and numbering policy.
2. Production cockpit for today's plan attainment, work order progress, material blockers, downtime, quality exceptions, and handover items.
3. Production plan readiness, plan-to-work-order conversion, work order release, rush/insert handling, and release risk checks.
4. Material readiness, shortage visibility, issue/staging request creation, line-side receipt confirmation, return/supplement request visibility, and material consumption evidence.
5. Operation dispatching to shift, team, person, work center, and device; operation task start, pause, resume, complete, transfer, and hold.
6. Production reporting with good quantity, scrap, rework, labor/machine time, material batch/serial evidence, and attachments.
7. In-process quality and nonconformance entry points: first-piece/in-process/final inspection task visibility, defect registration, rework/scrap linkage, and Quality/NCR drill-down.
8. Finished-goods receipt request creation and status visibility from production completion through WMS/Inventory receipt evidence.
9. Downtime, equipment impact, maintenance request visibility, recovery confirmation, and dispatch blocking/warning on unavailable assets.
10. Shift handover with unresolved production, material, quality, equipment, and receipt issues carried to the next shift.
11. Work-order-level and batch/serial-level genealogy/traceability across plan, BOM/routing version, operation tasks, reports, material lots, quality, equipment, people, downtime, and receipt requests.
12. BusinessGateway MES facade expansion for existing MES read/write service capabilities plus missing standard MES P0 contracts.
13. Minimal cross-domain read/action facades where the MES page needs context:
   - ProductEngineering: production version, MBOM, routing release context.
   - DemandPlanning/ERP: production plan source, planned work order suggestion, sales/order priority context where already available.
   - MasterData: SKU, work center, production line, device asset labels.
   - Quality: inspection task, defect, NCR, rework/scrap disposition context related to work orders and operation reports.
   - WMS/Inventory: stock availability, issue/staging execution status, line-side receipt, finished-goods inbound and stock movement visibility.
   - Maintenance/IndustrialTelemetry: asset unavailable/restored, downtime, alarm, recovery, and capacity impact visibility.
   - BarcodeLabel: barcode rule, label template, print batch, and scan record references needed for traceability.
   - ERP Finance: source-document drill-down for production cost evidence where an existing service surface already supports it.
14. Business Console generated client refresh and stable exports.
15. Desktop UI pages with Chinese visible copy.
16. Focused unit, API contract, frontend, and e2e verification.

### Out of Scope

1. PDA/mobile scanning flows.
2. Full APS/Gantt optimization UI. Schedule remains dispatch-oriented list/timeline/table workbench unless #78 is explicitly revived.
3. Full warehouse execution: bin strategy, wave picking, AGV/WCS routing, put-away, inventory counting, and warehouse-task optimization stay in WMS.
4. Inventory accounting: stock ledger, global availability promise, valuation, and financial inventory remain in Inventory/ERP.
5. Full QMS/LIMS: formal inspection-standard governance, lab sample lifecycle, CAPA, supplier quality, and audit programs stay in Quality/QMS.
6. Full CMMS/EAM: asset lifecycle, maintenance plan ownership, spare-parts planning, and maintenance cost accounting stay in Maintenance/EAM.
7. Direct frontend calls to business services.
8. Moving domain rules into BusinessGateway.
9. Full i18n translation catalog, locale switcher, or route-localized copy.
10. Raw PLC/DCS/SCADA control or WCS implementation inside MES.

## Dependency Matrix

| PC MES need | Execution owner | External fact owner | BusinessGateway approach |
| --- | --- | --- | --- |
| Foundation readiness | MES workbench decision surface | MasterData, ProductEngineering, WMS/Inventory, Quality, Maintenance/Telemetry, BarcodeLabel, IAM | Add a readiness endpoint that validates all required references and returns `Ready`/`Warning`/`Blocked` with reason codes. |
| Production plan readiness | MES workbench decision surface | DemandPlanning/ERP source plans, ProductEngineering BOM/routing, Inventory availability, Maintenance capacity | Add aggregated readiness endpoint that returns risk reasons and allowed release actions without moving source facts into MES. |
| Work order release and execution | MES | ProductEngineering, MasterData | Add work order detail/release endpoints with release snapshot of BOM/routing/version, work centers, and plan source. |
| Material readiness and shortages | MES visible readiness result | ProductEngineering BOM, Inventory availability/reservation, WMS line-side status | Add material readiness endpoint keyed by plan/work order/operation; keep inventory quantities authoritative in Inventory/WMS. |
| Material issue/staging request | MES triggers and tracks request intent | WMS/Inventory executes picking, staging, receipt, and stock movement | Add request creation/status endpoints; do not model warehouse tasks inside MES. |
| Operation dispatch | MES | MasterData resources, Maintenance availability, Quality holds | Add dispatch task list and assignment endpoints with blocking/warning reasons. |
| WIP and production reports | MES | Quality/Inventory downstream effects | Add operation state, report list, report create, and WIP summary endpoints. |
| Nonconformance and rework/scrap | MES creates execution defect context | Quality owns NCR/disposition; Inventory owns scrap movement | Add defect entry and related-quality drill-down; formal disposition stays in Quality. |
| Finished-goods receipt | MES request | WMS/Inventory inbound receipt and stock posting | Surface MES receipt requests plus downstream WMS/Inventory evidence. |
| Downtime and equipment impact | MES execution impact | IndustrialTelemetry events and Maintenance work orders | Surface downtime list/create/recovery endpoints and downstream maintenance status. |
| Shift handover | MES | Related contexts provide open issue status | Add handover summary/create/accept endpoints. |
| Traceability | MES execution genealogy | ProductEngineering, Quality, WMS/Inventory, Maintenance provide linked facts | Add traceability query endpoints by work order, batch/serial, material lot, and defect ID. |
| Barcode and labels | MES references rules and records scans | BarcodeLabel owns rule, template, print batch, scan record | Add rule resolution and label/scan references to material, report, receipt, and traceability DTOs. |
| Numbering | MES owns MES document IDs | Shared numbering governance; service-local generator in first release | Add explicit number rule checks and generate IDs server-side for MES documents. |
| Cost/source-document drill-down | ERP Finance | ERP Finance | Link to existing ERP Finance candidate/source-document surface only after route and permission are verified. |

## File Structure

Planned file responsibilities:

| Path | Responsibility |
| --- | --- |
| `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/*` | Add or verify batch resolve/readiness endpoints for site, line, work center, calendar, shift, team, personnel skill, device asset, resource capability, SKU, UOM, and reference data. |
| `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/*` | Add or verify production-version resolve endpoints that return released MBOM, routing, operation sequence, material demand, and resource capability references. |
| `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Endpoints/*` | Add or verify barcode rule and label template resolution for work order, operation task, material lot, product serial, receipt, and traceability labels. |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs` | Add missing MES read endpoints only when the MES service lacks the page-level query. |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/...` | Query handlers for production readiness, work order detail, material readiness, dispatch task list, operation task list, WIP, production reports, downtime, handover, traceability, schedule result history, and any missing read model. |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/...` | Commands for work order release, dispatch assignment, operation start/pause/resume/complete, material issue request intent, defect entry, downtime entry/recovery, finished-goods receipt request, and shift handover. |
| `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/...` | MES service endpoint and query tests. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs` | Business Console DTOs for MES workbench responses. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs` | Internal HTTP clients for MES and minimal related business read endpoints. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs` | `BusinessGatewayPermissions` constants and Business Console authorization checks for the MES permission matrix. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs` | BusinessGateway MES facade endpoints and stable operation IDs. |
| `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs` | Stable route and operationId tests. |
| `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs` | Bearer, permission, context, and downstream proxy tests. |
| `frontend/packages/api-client/src/business-console.ts` | Stable business-console exports after generated client refresh. |
| `frontend/apps/business-console/src/composables/useBusinessMes.ts` | Query/mutation composition entry point for MES PC pages, delegating grouped hooks to `src/composables/mes/*.ts`. |
| `frontend/apps/business-console/src/pages/mes/foundation.vue` | Foundation readiness page for master data, product engineering, supply, quality, equipment, barcode, and numbering blockers. |
| `frontend/apps/business-console/src/pages/mes/index.vue` | Production cockpit: plan attainment, blockers, exceptions, handover, and traceability entry points. |
| `frontend/apps/business-console/src/pages/mes/plans.vue` | Production plan readiness, plan-to-work-order conversion, release risk checks, and rush/insert impact. |
| `frontend/apps/business-console/src/pages/mes/work-orders.vue` | Work order list, release state, readiness summary, and quick actions. |
| `frontend/apps/business-console/src/pages/mes/work-orders/[workOrderId].vue` | Work order detail page. |
| `frontend/apps/business-console/src/pages/mes/materials.vue` | Material readiness, shortages, issue/staging request status, line-side receipt, return/supplement request visibility. |
| `frontend/apps/business-console/src/pages/mes/dispatch.vue` | Dispatch board for assigning operation tasks to shift/team/person/work center/device. |
| `frontend/apps/business-console/src/pages/mes/operation-tasks.vue` | Operation task queue and start/pause/resume/complete actions. |
| `frontend/apps/business-console/src/pages/mes/reports.vue` | Production report list and creation entry points for good/scrap/rework/labor/machine time. |
| `frontend/apps/business-console/src/pages/mes/quality.vue` | In-process quality tasks, defect entry, related NCR/rework/scrap context. |
| `frontend/apps/business-console/src/pages/mes/receipts.vue` | Finished-goods receipt request visibility and WMS/Inventory evidence. |
| `frontend/apps/business-console/src/pages/mes/schedules.vue` | Rule schedule run and dispatch-oriented result table/timeline. |
| `frontend/apps/business-console/src/pages/mes/downtime.vue` | Downtime registration, equipment impact, maintenance status, and recovery confirmation. |
| `frontend/apps/business-console/src/pages/mes/handovers.vue` | Shift handover summary, unresolved item carryover, and receiver confirmation. |
| `frontend/apps/business-console/src/pages/mes/traceability.vue` | Work-order, batch/serial, material-lot, and defect traceability search. |
| `frontend/apps/business-console/src/pages/mes/capacity.vue` | Capacity impact visibility from MES-maintenance integration, retained if separate from downtime. |
| `frontend/apps/business-console/tests/e2e/business-console.spec.ts` | Desktop MES navigation and smoke coverage. |
| `scripts/verify-business-console-mes-pc-workbench.ps1` | Governed focused verification script for this plan. |
| `docs/architecture/frontend-structure.md` | Update only after routes are implemented, to keep the Business Console route table current. |
| `docs/architecture/implementation-readiness.md` | Update only after implementation lands and verification evidence exists. |

## Contract Targets

Target BusinessGateway operation IDs:

| Method | Route | Operation ID | Downstream owner |
| --- | --- | --- | --- |
| GET | `/api/business-console/v1/mes/foundation-readiness` | `getBusinessConsoleMesFoundationReadiness` | BusinessGateway aggregation over MasterData, ProductEngineering, WMS/Inventory, Quality, Maintenance/Telemetry, BarcodeLabel, IAM |
| GET | `/api/business-console/v1/mes/foundation-readiness/master-data` | `getBusinessConsoleMesMasterDataReadiness` | MasterData resolve/validate facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/product-engineering` | `getBusinessConsoleMesProductEngineeringReadiness` | ProductEngineering production-version resolve facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/supply` | `getBusinessConsoleMesSupplyReadiness` | WMS/Inventory availability and staging route facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/quality` | `getBusinessConsoleMesQualityReadiness` | Quality inspection/hold facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/equipment` | `getBusinessConsoleMesEquipmentReadiness` | MasterData, Maintenance, IndustrialTelemetry facade |
| GET | `/api/business-console/v1/mes/foundation-readiness/barcode-numbering` | `getBusinessConsoleMesBarcodeNumberingReadiness` | BarcodeLabel plus MES numbering policy facade |
| GET | `/api/business-console/v1/mes/overview` | `getBusinessConsoleMesOverview` | BusinessGateway aggregation over MES queries |
| GET | `/api/business-console/v1/mes/production-plans` | `listBusinessConsoleMesProductionPlans` | DemandPlanning/ERP source plan via MES workbench facade |
| GET | `/api/business-console/v1/mes/production-plans/{productionPlanId}/readiness` | `getBusinessConsoleMesProductionPlanReadiness` | MES aggregation over ProductEngineering, Inventory/WMS, Quality, Maintenance |
| POST | `/api/business-console/v1/mes/production-plans/{productionPlanId}/work-orders` | `convertBusinessConsoleMesPlanToWorkOrder` | MES command with DemandPlanning/ERP source reference |
| GET | `/api/business-console/v1/mes/work-orders` | `listBusinessConsoleMesWorkOrders` | Existing MES service list |
| GET | `/api/business-console/v1/mes/work-orders/{workOrderId}` | `getBusinessConsoleMesWorkOrderDetail` | MES service detail query |
| POST | `/api/business-console/v1/mes/work-orders/{workOrderId}/release` | `releaseBusinessConsoleMesWorkOrder` | MES command |
| POST | `/api/business-console/v1/mes/work-orders/rush` | `createBusinessConsoleMesRushWorkOrder` | Existing MES service command |
| GET | `/api/business-console/v1/mes/work-orders/{workOrderId}/material-readiness` | `getBusinessConsoleMesMaterialReadiness` | MES aggregation over ProductEngineering, Inventory/WMS |
| POST | `/api/business-console/v1/mes/work-orders/{workOrderId}/material-issue-requests` | `createBusinessConsoleMesMaterialIssueRequest` | MES request intent, WMS/Inventory execution |
| GET | `/api/business-console/v1/mes/material-issue-requests` | `listBusinessConsoleMesMaterialIssueRequests` | MES/WMS status aggregation |
| POST | `/api/business-console/v1/mes/material-issue-requests/{requestId}/line-side-receipts` | `confirmBusinessConsoleMesLineSideMaterialReceipt` | MES receipt confirmation plus WMS/Inventory evidence |
| GET | `/api/business-console/v1/mes/dispatch-tasks` | `listBusinessConsoleMesDispatchTasks` | MES dispatch query |
| POST | `/api/business-console/v1/mes/dispatch-tasks/{operationTaskId}/assign` | `assignBusinessConsoleMesDispatchTask` | MES dispatch command |
| GET | `/api/business-console/v1/mes/operation-tasks` | `listBusinessConsoleMesOperationTasks` | MES service query |
| POST | `/api/business-console/v1/mes/operation-tasks/{operationTaskId}/start` | `startBusinessConsoleMesOperationTask` | MES command |
| POST | `/api/business-console/v1/mes/operation-tasks/{operationTaskId}/pause` | `pauseBusinessConsoleMesOperationTask` | MES command |
| POST | `/api/business-console/v1/mes/operation-tasks/{operationTaskId}/resume` | `resumeBusinessConsoleMesOperationTask` | MES command |
| POST | `/api/business-console/v1/mes/operation-tasks/{operationTaskId}/complete` | `completeBusinessConsoleMesOperationTask` | MES command |
| GET | `/api/business-console/v1/mes/wip` | `getBusinessConsoleMesWipSummary` | MES query |
| GET | `/api/business-console/v1/mes/production-reports` | `listBusinessConsoleMesProductionReports` | Existing MES service list |
| POST | `/api/business-console/v1/mes/production-reports` | `recordBusinessConsoleMesProductionReport` | Existing MES service command |
| POST | `/api/business-console/v1/mes/defects` | `recordBusinessConsoleMesDefect` | MES defect context, Quality downstream |
| GET | `/api/business-console/v1/mes/related-quality-items` | `listBusinessConsoleMesRelatedQualityItems` | Quality read facade |
| GET | `/api/business-console/v1/mes/finished-goods-receipt-requests` | `listBusinessConsoleMesFinishedGoodsReceiptRequests` | Existing MES service list |
| POST | `/api/business-console/v1/mes/finished-goods-receipt-requests` | `createBusinessConsoleMesFinishedGoodsReceiptRequest` | Existing MES service command |
| GET | `/api/business-console/v1/mes/downtime-events` | `listBusinessConsoleMesDowntimeEvents` | MES/Maintenance/Telemetry aggregation |
| POST | `/api/business-console/v1/mes/downtime-events` | `recordBusinessConsoleMesDowntimeEvent` | MES command |
| POST | `/api/business-console/v1/mes/downtime-events/{downtimeEventId}/recover` | `confirmBusinessConsoleMesDowntimeRecovery` | MES recovery command plus Maintenance context |
| GET | `/api/business-console/v1/mes/shift-handovers` | `listBusinessConsoleMesShiftHandovers` | MES query |
| POST | `/api/business-console/v1/mes/shift-handovers` | `createBusinessConsoleMesShiftHandover` | MES command |
| POST | `/api/business-console/v1/mes/shift-handovers/{handoverId}/accept` | `acceptBusinessConsoleMesShiftHandover` | MES command |
| GET | `/api/business-console/v1/mes/traceability/work-orders/{workOrderId}` | `getBusinessConsoleMesWorkOrderTraceability` | MES genealogy query |
| GET | `/api/business-console/v1/mes/traceability/batches/{batchOrSerial}` | `getBusinessConsoleMesBatchTraceability` | MES genealogy query |
| GET | `/api/business-console/v1/mes/traceability/material-lots/{materialLotId}` | `getBusinessConsoleMesMaterialLotTraceability` | MES genealogy query |
| GET | `/api/business-console/v1/mes/capacity-impacts` | `listBusinessConsoleMesCapacityImpacts` | Existing MES service list |

## Permission Targets

Define Business Console permissions explicitly in `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`, where `BusinessGatewayPermissions` currently lives. Add matching IAM seed/catalog and `docs/architecture/authorization-matrix.md` entries in the implementation PR. Do not let unrelated read pages fall through `business.mes.work-orders.manage`. Routes in this table omit the shared `/api/business-console/v1` prefix.

| Permission constant | Permission code | Routes |
| --- | --- | --- |
| `MesFoundationRead` | `business.mes.foundation.read` | All `/mes/foundation-readiness*` routes. |
| `MesOverviewRead` | `business.mes.overview.read` | `/mes/overview`. |
| `MesPlansRead` | `business.mes.plans.read` | `GET /mes/production-plans`, `GET /mes/production-plans/{productionPlanId}/readiness`. |
| `MesWorkOrdersRead` | `business.mes.work-orders.read` | `GET /mes/work-orders`, `GET /mes/work-orders/{workOrderId}`. |
| `MesWorkOrdersManage` | `business.mes.work-orders.manage` | Work order release, rush work order creation, and plan-to-work-order conversion. |
| `MesMaterialsRead` | `business.mes.materials.read` | Material readiness and material issue request list. |
| `MesMaterialsManage` | `business.mes.materials.manage` | Material issue request creation and line-side receipt confirmation. |
| `MesDispatchRead` | `business.mes.dispatch.read` | Dispatch task list. |
| `MesDispatchManage` | `business.mes.dispatch.manage` | Dispatch assignment. |
| `MesOperationsRead` | `business.mes.operations.read` | Operation task list and WIP summary. |
| `MesOperationsManage` | `business.mes.operations.manage` | Operation start, pause, resume, complete, transfer, and hold commands. |
| `MesReportingRead` | `business.mes.reporting.read` | Production report list. |
| `MesReportingWrite` | `business.mes.reporting.write` | Production report creation. |
| `MesQualityRead` | `business.mes.quality.read` | Related quality items and defect context drill-down. |
| `MesQualityWrite` | `business.mes.quality.write` | MES execution defect creation. |
| `MesReceiptsRead` | `business.mes.receipts.read` | Finished-goods receipt request list. |
| `MesReceiptsManage` | `business.mes.receipts.manage` | Finished-goods receipt request creation. |
| `MesDowntimeRead` | `business.mes.downtime.read` | Downtime event list. |
| `MesDowntimeManage` | `business.mes.downtime.manage` | Downtime event creation and recovery confirmation. |
| `MesHandoversRead` | `business.mes.handovers.read` | Shift handover list. |
| `MesHandoversManage` | `business.mes.handovers.manage` | Shift handover creation and acceptance. |
| `MesTraceabilityRead` | `business.mes.traceability.read` | Work order, batch/serial, and material-lot traceability queries. |
| `MesSchedulesRead` | `business.mes.schedules.read` | Schedule result/status history. |
| `MesSchedulesManage` | `business.mes.schedules.manage` | Rule schedule run. |
| `MesCapacityRead` | `business.mes.capacity.read` | Capacity impact list. |

The MES service `MesPermissionCodes` should mirror MES-owned endpoint intent at the same granularity for contract metadata. Source services keep their own permission catalogs; BusinessGateway still performs the end-user authorization check before forwarding internal bearer calls.

All target routes must keep the existing BusinessGateway pattern:

1. Gateway endpoint uses `AuthorizedBusinessProxyEndpoint`.
2. Gateway endpoint permission comes from `BusinessGatewayPermissions`.
3. Gateway forwards `tokenProvider.BearerToken` to business services.
4. Business services stay protected by `InternalServiceAuthorizationPolicy`.
5. Frontend consumes only `@nerv-iip/api-client` stable business-console exports.

## Task 0: Production Foundation Readiness

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- Review and modify when the endpoint is missing: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Endpoints/*` for production foundation readiness.
- Review and modify when the endpoint is missing: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Endpoints/*` for production-version readiness.
- Review and modify when the endpoint is missing: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Endpoints/*` for barcode and label rule readiness.
- Test: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Test: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`

- [ ] **Step 1: Define readiness DTOs**

Add Business Console DTOs with stable names and reason codes:

```csharp
public sealed record BusinessConsoleMesFoundationReadinessRequest(
    string OrganizationId,
    string EnvironmentId,
    string? SiteCode,
    string? LineCode,
    string? WorkCenterCode,
    string? SkuId,
    string? ProductionVersionId,
    DateTimeOffset? PlannedStartUtc,
    DateTimeOffset? PlannedEndUtc);

public sealed record BusinessConsoleMesFoundationReadinessResponse(
    string Status,
    IReadOnlyCollection<BusinessConsoleMesReadinessArea> Areas,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> BlockingIssues,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> WarningIssues);

public sealed record BusinessConsoleMesReadinessArea(
    string AreaCode,
    string Status,
    IReadOnlyCollection<BusinessConsoleMesReadinessIssue> Issues);

public sealed record BusinessConsoleMesReadinessIssue(
    string Code,
    string Severity,
    string Message,
    string? SourceSystem,
    string? ReferenceType,
    string? ReferenceId,
    string? ReferenceDisplayName,
    DateTimeOffset? EffectiveFromUtc,
    DateTimeOffset? EffectiveToUtc,
    string? Version,
    string? FixHint);
```

Use these area codes in the first release: `master-data`, `product-engineering`, `supply`, `quality`, `equipment`, `barcode-numbering`, and `iam-context`.
Use these status values only: `Ready`, `Warning`, and `Blocked`.
Use the reason codes from the Reason Code Baseline table; add a new code only with a gateway contract test and a page rendering assertion.

- [ ] **Step 2: Write gateway tests for foundation readiness**

Add tests proving `GET /api/business-console/v1/mes/foundation-readiness`:

1. Requires authenticated Business Console user bearer.
2. Calls IAM authorization with `BusinessGatewayPermissions.MesFoundationRead`.
3. Calls downstream read clients with internal service bearer token.
4. Returns `Blocked` when any P0 area returns a blocking issue.
5. Returns `Warning` when no blocker exists but at least one warning exists.
6. Returns `Ready` when all areas are ready.
7. Preserves source-system and reference IDs so users know which foundation record to fix.
8. Converts source-service timeout, 5xx, invalid JSON, and missing required readiness fields into a `Blocked` area with `SOURCE_SERVICE_UNAVAILABLE`, while preserving normal 401/403 responses for IAM authentication and authorization failures.

- [ ] **Step 3: Verify source-service resolver coverage**

Check these source services before adding new endpoints:

```powershell
rg -n "Resolve|Validate|ProductionVersion|Barcode|Rule|WorkCalendar|Shift|PersonnelSkill|DeviceAsset|WorkCenter" backend/services/Business
```

Use existing resolver endpoints when they already return the Foundation Record Contract fields. When coverage is missing, add only these narrow read endpoints:

| Service | Endpoint shape | Must answer |
| --- | --- | --- |
| MasterData | `POST /api/business/master-data/v1/readiness/production-foundation` | Hierarchy, work calendar, shift, team, personnel skill, SKU/UOM, work center, device asset, and resource capability readiness. |
| ProductEngineering | `POST /api/business/product-engineering/v1/readiness/production-version` | Released production version, MBOM, routing, operation sequence, material demand, standard duration, and required resource capability readiness. |
| BarcodeLabel | `POST /api/business/barcode-label/v1/readiness/rules` | Barcode rule, label template, printer mapping, and scan rule readiness for MES document/material/product/receipt/traceability use cases. |

Do not add broad foundation-data maintenance screens or CRUD endpoints inside MES.

- [ ] **Step 4: Add numbering readiness contract**

For MES-owned documents, add server-side number rule checks before commands create records:

| Document | Required prefix example | Rule owner |
| --- | --- | --- |
| Work order | `MO` | MES service-local policy |
| Operation task | `OP` | MES service-local policy |
| Material issue request | `MI` | MES service-local policy |
| Production report | `PR` | MES service-local policy |
| Defect record | `DF` | MES service-local policy |
| Downtime event | `DT` | MES service-local policy |
| Finished-goods receipt request | `FG` | MES service-local policy |
| Shift handover | `SH` | MES service-local policy |

The first implementation may keep the generator inside MES, but the rule shape must be explicit enough to move to a shared Numbering service later without changing Business Console contracts.

- [ ] **Step 5: Run gateway focused tests**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

Expected after implementation: PASS.

- [ ] **Step 6: Commit foundation readiness**

```powershell
git add backend/gateway/BusinessGateway backend/services/Business/MasterData backend/services/Business/ProductEngineering backend/services/Business/BarcodeLabel docs/architecture/authorization-matrix.md
git commit -m "feat: add mes foundation readiness contracts"
```

## Task 1: Contract Gap Map And First Failing Tests

**Files:**
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `docs/architecture/api-contract-and-codegen.md` if the current document lacks BusinessGateway export expectations for the added routes.

- [ ] **Step 1: Write OpenAPI operationId assertions**

Add assertions for every route in the Contract Targets table. Keep the existing assertion style in `BusinessGatewayOpenApiTests.cs`, for example:

```csharp
AssertOperationId(paths, "/api/business-console/v1/mes/production-reports", "get", "listBusinessConsoleMesProductionReports");
AssertOperationId(paths, "/api/business-console/v1/mes/finished-goods-receipt-requests", "get", "listBusinessConsoleMesFinishedGoodsReceiptRequests");
AssertOperationId(paths, "/api/business-console/v1/mes/capacity-impacts", "get", "listBusinessConsoleMesCapacityImpacts");
```

- [ ] **Step 2: Write proxy tests before implementation**

Add tests proving each new facade:

1. Rejects unauthenticated requests.
2. Calls IAM authorization with the expected permission code.
3. Forwards `organizationId`, `environmentId`, IDs, filters, and `take`.
4. Sends the internal service bearer token downstream.
5. Does not call the downstream service when IAM denies access.

- [ ] **Step 3: Run failing gateway tests**

Run:

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

Expected at this point: FAIL because the new routes, clients, models, and permissions do not exist yet.

- [ ] **Step 4: Commit tests only**

```powershell
git add backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs docs/architecture/api-contract-and-codegen.md
git commit -m "test: define mes pc workbench business gateway contracts"
```

## Task 2: MES Service Read Surface

**Files:**
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- Create or modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/WorkOrders/*`
- Create or modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/*`
- Test: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/*`

- [ ] **Step 1: Add missing MES service tests**

Write service-level tests for:

1. `GET /api/business/v1/mes/work-orders/{workOrderId}` returns one work order with operation tasks, release snapshot, material readiness summary, quality status, equipment status, and receipt status.
2. `GET /api/business/v1/mes/operation-tasks` filters by organization, environment, status, work center, device, shift, team, and work order.
3. `POST /api/business/v1/mes/work-orders/{workOrderId}/release` refuses release when production version, route, key material, quality hold, or equipment availability blocks execution, and allows release with warnings when policy permits manual confirmation.
4. `GET /api/business/v1/mes/work-orders/{workOrderId}/material-readiness` returns demand quantity, available quantity, requested quantity, staged quantity, received quantity, shortage quantity, substitute availability, and blocking reason.
5. `POST /api/business/v1/mes/work-orders/{workOrderId}/material-issue-requests` creates a MES material request intent without creating warehouse tasks directly.
6. `POST /api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign` records person/device/shift assignment and blocks unavailable device or quality hold according to rule.
7. `POST /api/business/v1/mes/operation-tasks/{operationTaskId}/start|pause|resume|complete` changes operation task state and preserves audit-friendly timestamps and actor IDs.
8. Existing `GET /api/business/v1/mes/production-reports` remains available and report creation can include good, scrap, rework, labor time, machine time, material batch/serial evidence, and attachments.
9. `POST /api/business/v1/mes/defects` records an execution defect context and links to Quality/NCR downstream identifiers when available.
10. Existing `GET /api/business/v1/mes/finished-goods-receipt-requests` remains available.
11. Existing `GET /api/business/v1/mes/capacity-impacts` remains available.
12. `POST /api/business/v1/mes/downtime-events` and recovery confirmation record production-impacting downtime.
13. `POST /api/business/v1/mes/shift-handovers` carries unresolved production/material/quality/equipment/receipt issues to the next shift.
14. `GET /api/business/v1/mes/wip` returns WIP counts by work order, operation, work center, status, blocker reason, shift, team, and planned/actual quantity.
15. Traceability queries return at least work order, production version, operation tasks, reports, material lots, defects, downtime, receipt request, person, and device links.

- [ ] **Step 2: Implement missing read queries**

Only add MES service queries and commands that do not already exist. Use async EF Core calls with `CancellationToken`, and keep query/endpoint DTOs in the Web/Application layer rather than Domain.

Expected endpoint contract additions:

```csharp
new(typeof(GetMesWorkOrderDetailEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}", MesPermissionCodes.WorkOrdersRead, "getBusinessMesWorkOrderDetail"),
new(typeof(ListOperationTasksEndpoint), "GET", "/api/business/v1/mes/operation-tasks", MesPermissionCodes.OperationsRead, "listBusinessMesOperationTasks"),
new(typeof(GetMaterialReadinessEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}/material-readiness", MesPermissionCodes.MaterialsRead, "getBusinessMesMaterialReadiness"),
new(typeof(AssignDispatchTaskEndpoint), "POST", "/api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign", MesPermissionCodes.DispatchManage, "assignBusinessMesDispatchTask"),
new(typeof(GetWipSummaryEndpoint), "GET", "/api/business/v1/mes/wip", MesPermissionCodes.OperationsRead, "getBusinessMesWipSummary"),
new(typeof(RecordDowntimeEventEndpoint), "POST", "/api/business/v1/mes/downtime-events", MesPermissionCodes.DowntimeManage, "recordBusinessMesDowntimeEvent"),
new(typeof(CreateShiftHandoverEndpoint), "POST", "/api/business/v1/mes/shift-handovers", MesPermissionCodes.HandoversManage, "createBusinessMesShiftHandover"),
new(typeof(GetWorkOrderTraceabilityEndpoint), "GET", "/api/business/v1/mes/traceability/work-orders/{workOrderId}", MesPermissionCodes.TraceabilityRead, "getBusinessMesWorkOrderTraceability"),
```

Add every new MES service permission constant to `MesPermissionCodes.All` and its endpoint contract test. Keep the service endpoint protected by `InternalServiceAuthorizationPolicy`; the permission code remains contract/catalog metadata and is not a terminal-user bearer authorization decision.

- [ ] **Step 3: Run MES focused tests**

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

Expected after implementation: PASS.

- [ ] **Step 4: Commit MES service surface**

```powershell
git add backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests
git commit -m "feat: expose mes pc workbench read surface"
```

## Task 3: BusinessGateway MES Facade Expansion

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayAuthorization.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- Test: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Test: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`

- [ ] **Step 1: Add Business Console MES DTOs**

Add compact DTOs for:

1. MES cockpit counts, blocker summaries, and role-specific pending work.
2. Production plan readiness rows and release-risk details.
3. Work order detail and release snapshot.
4. Material readiness, shortage, issue/staging request, line-side receipt, and material consumption evidence rows.
5. Dispatch task rows and assignment request/response.
6. Operation task rows and start/pause/resume/complete responses.
7. WIP summary rows.
8. Production report rows.
9. Defect/nonconformance execution context rows.
10. Finished-goods receipt request rows.
11. Downtime and equipment impact rows.
12. Shift handover rows.
13. Traceability graph/list rows.
14. Capacity impact rows.
15. Related quality item rows.

Keep DTO property names stable and frontend-oriented, for example `productionPlanId`, `workOrderId`, `operationTaskId`, `materialId`, `materialLotId`, `batchOrSerial`, `status`, `readinessStatus`, `blockingReasons`, `workCenterId`, `deviceAssetId`, `shiftId`, `assignedUserId`, `plannedStartUtc`, `startedAtUtc`, `reportedAtUtc`, `qualityStatus`, `receiptStatus`, and `handoverStatus`.

- [ ] **Step 2: Add internal client methods**

Extend `IBusinessMesClient` and `HttpBusinessMesClient` for the MES routes in the Contract Targets table. Add separate client interfaces only when a non-MES fact owner is needed by a page:

```csharp
Task<BusinessConsoleMesProductionReportListResponse> ListProductionReportsAsync(
    string internalBearerToken,
    BusinessConsoleMesProductionReportListRequest request,
    CancellationToken cancellationToken);

Task<BusinessConsoleMesFinishedGoodsReceiptRequestListResponse> ListFinishedGoodsReceiptRequestsAsync(
    string internalBearerToken,
    BusinessConsoleMesReceiptRequestListRequest request,
    CancellationToken cancellationToken);

Task<BusinessConsoleMesCapacityImpactListResponse> ListCapacityImpactsAsync(
    string internalBearerToken,
    BusinessConsoleMesCapacityImpactListRequest request,
    CancellationToken cancellationToken);

Task<BusinessConsoleMesMaterialReadinessResponse> GetMaterialReadinessAsync(
    string internalBearerToken,
    string workOrderId,
    BusinessConsoleMesContextRequest request,
    CancellationToken cancellationToken);

Task<BusinessConsoleMesTraceabilityResponse> GetWorkOrderTraceabilityAsync(
    string internalBearerToken,
    string workOrderId,
    BusinessConsoleMesContextRequest request,
    CancellationToken cancellationToken);
```

- [ ] **Step 3: Add facade endpoints**

Add one FastEndpoints class per route to `BusinessConsoleMesEndpoints.cs`, following the existing endpoint style. Do not place route mappings in startup files.

- [ ] **Step 4: Use narrow permissions**

Implement the Permission Targets table exactly:

1. Add missing constants to `BusinessGatewayPermissions` in `BusinessGatewayAuthorization.cs`.
2. Map every BusinessGateway endpoint to the listed permission; do not reuse `MesWorkOrdersManage` for foundation, material, dispatch, operation, quality, receipt, downtime, handover, traceability, schedule-read, or capacity-read pages.
3. Add IAM seed/catalog and `docs/architecture/authorization-matrix.md` rows for every new permission code.
4. Add gateway tests proving at least one read route and one write route use different permission codes in each MES area.

- [ ] **Step 5: Run gateway tests**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
```

Expected after implementation: PASS.

- [ ] **Step 6: Commit gateway facade**

```powershell
git add backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests docs/architecture/authorization-matrix.md
git commit -m "feat: expand mes business console facade"
```

## Task 4: OpenAPI Snapshot And Generated Client

**Files:**
- Modify generated snapshot: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Modify generated client files under: `frontend/packages/api-client/src/generated/business-console/`
- Modify stable exports: `frontend/packages/api-client/src/business-console.ts`
- Test: `frontend/packages/api-client/src/generated-contract.test.ts`

- [ ] **Step 1: Export BusinessGateway OpenAPI**

Use the repository's existing governed OpenAPI export path. Do not hand-edit OpenAPI JSON.

- [ ] **Step 2: Regenerate frontend API client**

```powershell
pnpm -C frontend generate:api
```

Expected: generated business-console client contains the new operation functions and Pinia Colada query/mutation options.

- [ ] **Step 3: Add stable exports**

Export only the required MES workbench functions and type aliases from `frontend/packages/api-client/src/business-console.ts`. Do not deep-import generated files in app code.

- [ ] **Step 4: Update generated contract test**

Add `expect(...).toBeTypeOf('function')` assertions for the new query/mutation options and stable exports.

- [ ] **Step 5: Run api-client tests**

```powershell
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/api-client typecheck
```

Expected after implementation: PASS.

- [ ] **Step 6: Commit contract artifacts**

```powershell
git add frontend/packages/api-client
git commit -m "feat: generate mes pc business console client"
```

## Task 5: PC MES Composables

**Files:**
- Modify: `frontend/apps/business-console/src/composables/useBusinessMes.ts`
- Create: `frontend/apps/business-console/src/composables/mes/useMesWorkbench.ts`
- Create: `frontend/apps/business-console/src/composables/mes/useMesReferenceLabels.ts`
- Test: existing or new Vitest files under `frontend/apps/business-console/src/**/__tests__` or `frontend/apps/business-console/tests`

- [ ] **Step 1: Add query wrappers**

Expose composable functions for:

1. `useMesOverview()`
2. `useMesFoundationReadiness()`
3. `useMesProductionPlans()`
4. `useMesProductionPlanReadiness(productionPlanId)`
5. `useMesWorkOrders()`
6. `useMesWorkOrderDetail(workOrderId)`
7. `useMesMaterialReadiness(workOrderId)`
8. `useMesMaterialIssueRequests()`
9. `useMesDispatchTasks()`
10. `useMesOperationTasks()`
11. `useMesWipSummary()`
12. `useMesProductionReports()`
13. `useMesQualityContext()`
14. `useMesFinishedGoodsReceiptRequests()`
15. `useMesDowntimeEvents()`
16. `useMesShiftHandovers()`
17. `useMesTraceability()`
18. `useMesCapacityImpacts()`
19. `useMesSchedules()`

- [ ] **Step 2: Replace hardcoded context source**

Keep the existing `org-001` and `env-dev` development defaults only behind one explicit app-local helper, so pages can later move to a real context selector without editing every form.

- [ ] **Step 3: Add invalidation rules**

After plan conversion, work order release, material issue request creation, line-side material receipt, dispatch assignment, operation state change, production report creation, defect entry, finished-goods receipt request creation, downtime recovery, shift handover acceptance, or schedule run, invalidate affected MES queries by operation ID. Reuse the existing `isBusinessQuery` pattern.

- [ ] **Step 4: Run Business Console typecheck**

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
```

Expected after implementation: PASS.

- [ ] **Step 5: Commit composables**

```powershell
git add frontend/apps/business-console/src/composables
git commit -m "feat: add mes pc workbench composables"
```

## Task 6: PC MES Pages With Chinese Copy

**Files:**
- Create: `frontend/apps/business-console/src/pages/mes/index.vue`
- Create: `frontend/apps/business-console/src/pages/mes/foundation.vue`
- Create: `frontend/apps/business-console/src/pages/mes/plans.vue`
- Modify: `frontend/apps/business-console/src/pages/mes/work-orders.vue`
- Create: `frontend/apps/business-console/src/pages/mes/work-orders/[workOrderId].vue`
- Create: `frontend/apps/business-console/src/pages/mes/materials.vue`
- Create: `frontend/apps/business-console/src/pages/mes/dispatch.vue`
- Create: `frontend/apps/business-console/src/pages/mes/operation-tasks.vue`
- Create: `frontend/apps/business-console/src/pages/mes/reports.vue`
- Create: `frontend/apps/business-console/src/pages/mes/quality.vue`
- Create: `frontend/apps/business-console/src/pages/mes/receipts.vue`
- Modify: `frontend/apps/business-console/src/pages/mes/schedules.vue`
- Create: `frontend/apps/business-console/src/pages/mes/downtime.vue`
- Create: `frontend/apps/business-console/src/pages/mes/handovers.vue`
- Create: `frontend/apps/business-console/src/pages/mes/traceability.vue`
- Create: `frontend/apps/business-console/src/pages/mes/capacity.vue`
- Modify: `frontend/apps/business-console/src/layouts/BusinessLayout.vue`
- Test: `frontend/apps/business-console/tests/e2e/business-console.spec.ts`

- [ ] **Step 1: Build the desktop MES navigation**

Add the MES pages to the Business Console navigation with Chinese labels:

| Route | Label |
| --- | --- |
| `/mes` | `生产驾驶舱` |
| `/mes/foundation` | `基础准备` |
| `/mes/plans` | `生产计划` |
| `/mes/work-orders` | `计划与工单` |
| `/mes/materials` | `齐套与物料` |
| `/mes/dispatch` | `派工看板` |
| `/mes/operation-tasks` | `工序执行` |
| `/mes/reports` | `报工与完工` |
| `/mes/quality` | `质量与不良` |
| `/mes/receipts` | `完工入库` |
| `/mes/schedules` | `规则排程` |
| `/mes/downtime` | `设备与停机` |
| `/mes/handovers` | `班次交接` |
| `/mes/traceability` | `追溯查询` |
| `/mes/capacity` | `产能影响` |

- [ ] **Step 2: Replace visible English MES copy**

All visible MES page text must be Chinese literals for this phase. Examples:

| Current English | Required Chinese |
| --- | --- |
| `Work orders` | `生产工单` |
| `Create rush work order` | `创建急单` |
| `Record production report` | `提交生产报工` |
| `Run schedule` | `运行排程` |
| `No work orders returned.` | `暂无生产工单。` |
| `Material readiness` | `齐套检查` |
| `Issue request` | `领料申请` |
| `Dispatch` | `派工` |
| `Downtime` | `停机` |
| `Traceability` | `追溯` |
| `Organization` | `组织` |
| `Environment` | `环境` |
| `Status` | `状态` |
| `Take` | `数量上限` |

Do not introduce a new translation catalog or locale switcher in this task.

For the existing `frontend/apps/business-console/src/pages/mes/schedules.vue`, replace all visible English copy, consume `useMesSchedules()`, keep the page as a rule-schedule result/status workbench, and do not add full APS/Gantt behavior in this task.

- [ ] **Step 3: Implement page states**

Each page must cover loading, empty, error, success, and disabled-submit states. Use `Spinner`, `TableEmpty`, `Badge`, `Button`, `Field`, and related `@nerv-iip/ui` exports only.

- [ ] **Step 4: Keep pages operational rather than marketing-style**

Use dense tables, filters, concise metrics, and direct action panels. Do not add a landing-page hero or decorative card layout.

- [ ] **Step 5: Add route-level smoke coverage**

Extend Playwright smoke tests to open each MES route and assert at least one Chinese heading or table label appears.

- [ ] **Step 6: Run frontend focused checks**

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts
```

Expected after implementation: PASS. If local Playwright managed browser is missing, set `PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH` to the installed Chromium/Chrome path and rerun once.

- [ ] **Step 7: Commit PC MES pages**

```powershell
git add frontend/apps/business-console/src frontend/apps/business-console/tests
git commit -m "feat: complete mes pc business console pages"
```

## Task 7: Minimal Cross-Domain MES Context

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify or create endpoint files under: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/`
- Test: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify frontend pages only where the added context is displayed.

- [ ] **Step 1: Add only context needed by MES pages**

Implement the cross-domain reads in this order:

1. DemandPlanning/ERP source plan and priority context for production plan readiness.
2. MasterData labels for SKU, work center, production line, shift, team, device asset, and production-supply area.
3. ProductEngineering production-version, MBOM, routing, work instruction, and effective-version summary on work order detail.
4. Inventory/WMS availability, reservation, issue/staging status, line-side receipt, return/supplement, and downstream finished-goods receipt evidence.
5. Quality inspection task, hold, defect, NCR, rework, scrap, and disposition rows related to work orders and operation tasks.
6. Maintenance/IndustrialTelemetry asset state, alarm, downtime, recovery, and capacity-impact labels if the MES query returns only IDs.
7. ERP Finance source-document links only when the existing ERP surface is verified.

- [ ] **Step 2: Keep fallbacks ID-based**

If a related service does not have a stable read endpoint yet, show the raw ID from MES and do not block the MES page. Record the missing read endpoint in the implementation PR description rather than fabricating data.

- [ ] **Step 3: Run gateway and frontend checks**

```powershell
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
pnpm -C frontend --filter @nerv-iip/business-console typecheck
```

Expected after implementation: PASS.

- [ ] **Step 4: Commit cross-domain context**

```powershell
git add backend/gateway/BusinessGateway frontend/apps/business-console/src
git commit -m "feat: add mes related business context"
```

## Task 8: Focused Verification Script

**Files:**
- Create: `scripts/verify-business-console-mes-pc-workbench.ps1`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/frontend-structure.md`
- Modify if route/contract docs changed: `docs/architecture/api-contract-and-codegen.md`

- [ ] **Step 1: Create governed script**

The script must dot-source `scripts/lib/ScriptAutomation.ps1` and use helper functions such as `Invoke-DotNet` and `Invoke-Pnpm`. It must not call `dotnet`, `pnpm`, or `pwsh` directly.

The script should run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj --no-restore
pnpm -C frontend generate:api
pnpm -C frontend --filter @nerv-iip/api-client typecheck
pnpm -C frontend --filter @nerv-iip/api-client test
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
```

- [ ] **Step 2: Add optional e2e mode**

Support an opt-in switch for:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console e2e -- business-console.spec.ts
```

Document that a local Chrome/Chromium executable may be required.

- [ ] **Step 3: Run script governance**

```powershell
scripts/check-script-governance.ps1
```

Expected after implementation: PASS.

- [ ] **Step 4: Run the focused verification script**

```powershell
scripts/verify-business-console-mes-pc-workbench.ps1
```

Expected after implementation: PASS.

- [ ] **Step 5: Update architecture docs with verified facts**

Only after the script passes, update:

1. `docs/architecture/frontend-structure.md` route table for the new MES pages.
2. `docs/architecture/implementation-readiness.md` current-code-fact entry for Business Console PC MES completion.
3. `docs/architecture/api-contract-and-codegen.md` if BusinessGateway export/codegen commands or snapshots changed.

- [ ] **Step 6: Commit verification and docs**

```powershell
git add scripts/verify-business-console-mes-pc-workbench.ps1 docs/architecture/implementation-readiness.md docs/architecture/frontend-structure.md docs/architecture/api-contract-and-codegen.md
git commit -m "docs: record mes pc workbench verification"
```

## Final Verification

Run the focused gate:

```powershell
scripts/verify-business-console-mes-pc-workbench.ps1
```

Then run the broader frontend and backend checks that match the changed surface:

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

If Docker-dependent gates are not run, state the Docker blocker explicitly in the PR.

## Rollout Order

1. Merge Task 0 foundation readiness contracts first: MasterData, ProductEngineering, WMS/Inventory, Quality, Maintenance/Telemetry, BarcodeLabel, IAM, source-service failure handling, permissions, and numbering checks.
2. Merge Task 1 API/BFF contract work before page expansion.
3. Implement Task 2 standard P0 MES service surface in this order: plan readiness and work order release, material readiness/request, dispatch and operation state, WIP, report/quality/downtime, receipt/handover/traceability.
4. Merge generated client and composables immediately after contracts.
5. Merge PC MES pages with Chinese copy and role-oriented navigation.
6. Add minimal cross-domain context as a follow-up if it increases review size too much, but do not drop foundation readiness, material readiness, dispatch, downtime, handover, or traceability from the target model.
7. Start WMS workbench, DemandPlanning/MRP, ERP drill-down, Quality deeper workflow, and Maintenance/Telemetry PC pages after MES desktop flow is usable.
8. Start PDA/mobile only after MES PC contracts and primary flows stop changing.

## Acceptance Checklist

- [ ] BusinessGateway exposes the MES PC workbench routes in the Contract Targets table.
- [ ] BusinessGateway and IAM expose the Permission Targets matrix, and read/write routes do not collapse into one broad manage permission.
- [ ] BusinessGateway tests cover auth, permission, context propagation, internal bearer forwarding, and downstream denial behavior.
- [ ] Foundation readiness returns `Ready`, `Warning`, or `Blocked` across MasterData, ProductEngineering, WMS/Inventory, Quality, Maintenance/Telemetry, BarcodeLabel, IAM, and numbering areas.
- [ ] Foundation readiness converts source-service timeout, 5xx, invalid JSON, or malformed readiness payload into a `Blocked` area with `SOURCE_SERVICE_UNAVAILABLE`.
- [ ] Work order release stores snapshots for master data, production version, material readiness, quality requirement, equipment/person assignment, barcode rule, and generated document IDs.
- [ ] MES service endpoints exist for P0 execution facts: plan readiness, work order release, material readiness/request intent, dispatch, operation state, WIP, report, defect context, downtime, receipt request, shift handover, and traceability.
- [ ] Generated business-console client exports stable MES workbench functions and types.
- [ ] PC MES routes exist under `frontend/apps/business-console/src/pages/mes` with foundation readiness, production cockpit, production plan, material readiness, dispatch, operation execution, report, quality, receipt, downtime, handover, and traceability pages.
- [ ] User-visible MES page copy is Chinese in the first implementation.
- [ ] No page directly calls business service URLs or generated deep imports.
- [ ] MES can see and trigger material issue/staging flow, but WMS/Inventory remain the source of warehouse execution and inventory balances.
- [ ] MES can see quality, downtime, and maintenance context, but Quality and Maintenance remain their own source-of-truth services.
- [ ] MES can consume barcode and label rules, but BarcodeLabel remains the source of templates, print batches, and scan records.
- [ ] MES document IDs are generated server-side using explicit numbering rules; users are not required to manually invent durable IDs.
- [ ] Traceability can start from work order, batch/serial, material lot, or defect and return the linked execution evidence.
- [ ] `scripts/verify-business-console-mes-pc-workbench.ps1` passes.
- [ ] `docs/architecture/frontend-structure.md` and `docs/architecture/implementation-readiness.md` are updated after implementation evidence exists.
- [ ] PDA/mobile remains explicitly deferred until PC MES contracts stabilize.
