# Business Console MES PC Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete a PC-first, standard MES workbench so production planners, supervisors, shift leaders, material handlers, quality inspectors, and maintenance coordinators can run the real shop-floor loop from production plan readiness through work order release, material readiness, dispatching, operation execution, reporting, quality handling, finished-goods receipt, shift handover, and traceability before starting PDA/mobile work.

**Architecture:** BusinessGateway remains the Business Console BFF and the only frontend-facing API for `/api/business-console/v1/**`; it performs user bearer validation, IAM permission checks, organization/environment context propagation, and internal service-token calls. MES owns shop-floor execution facts: work orders, operation tasks, dispatching, WIP state, production reports, material consumption evidence, downtime events, finished-goods receipt requests, shift handover, and genealogy snapshots. ProductEngineering, DemandPlanning, MasterData, Quality, WMS/Inventory, Maintenance/IndustrialTelemetry, and ERP are integrated through narrow read/action facades where the MES workbench must see or trigger their facts; MES must not take over their source-of-truth responsibilities. The first release uses direct Chinese UI copy in Vue pages and defers a full i18n catalog workflow.

**Tech Stack:** .NET 10, FastEndpoints, CleanDDD service boundaries, BusinessGateway facade, Hey API generated `@nerv-iip/api-client`, Vue 3, Vite Plus, Pinia Colada, `@nerv-iip/ui`, Playwright.

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

## Verified Current Code Facts

These facts were checked against the repository on 2026-05-26:

| Area | Current fact | Implication |
| --- | --- | --- |
| Business Console app | `frontend/apps/business-console` exists as the independent Vite app on the BusinessGateway facade. | PC pages can continue without changing the main platform console. |
| Current MES pages | `frontend/apps/business-console/src/pages/mes/work-orders.vue` and `frontend/apps/business-console/src/pages/mes/schedules.vue` exist. | The next work should enhance MES pages rather than create a new app shell. |
| Current MES composable | `frontend/apps/business-console/src/composables/useBusinessMes.ts` consumes generated Business Console APIs. | New page data should be added here or split into MES-specific composables under the same app boundary. |
| BusinessGateway MES facade | `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs` exposes `listBusinessConsoleMesWorkOrders`, `createBusinessConsoleMesRushWorkOrder`, `runBusinessConsoleMesSchedule`, and `recordBusinessConsoleMesProductionReport`. | Business Console currently has only the MVP MES surface. |
| MES service surface | `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs` already contains service endpoints for work orders, production reports, finished-goods receipt requests, and capacity impacts. | The first API work can expand BusinessGateway facade coverage before introducing broader domain changes. |
| Existing page copy | Current MES Vue pages still contain English user-visible labels such as `Work orders`, `Create rush work order`, `Run schedule`, and `No work orders returned.` | PC completion must include a Chinese-copy pass. |
| Mobile/PDA | No mobile Business Console client or generated mobile API boundary is present. | Mobile/PDA is not a blocker for PC MES and should start after PC contract stabilization. |

## Business Scope

### In Scope

1. Production cockpit for today's plan attainment, work order progress, material blockers, downtime, quality exceptions, and handover items.
2. Production plan readiness, plan-to-work-order conversion, work order release, rush/insert handling, and release risk checks.
3. Material readiness, shortage visibility, issue/staging request creation, line-side receipt confirmation, return/supplement request visibility, and material consumption evidence.
4. Operation dispatching to shift, team, person, work center, and device; operation task start, pause, resume, complete, transfer, and hold.
5. Production reporting with good quantity, scrap, rework, labor/machine time, material batch/serial evidence, and attachments.
6. In-process quality and nonconformance entry points: first-piece/in-process/final inspection task visibility, defect registration, rework/scrap linkage, and Quality/NCR drill-down.
7. Finished-goods receipt request creation and status visibility from production completion through WMS/Inventory receipt evidence.
8. Downtime, equipment impact, maintenance request visibility, recovery confirmation, and dispatch blocking/warning on unavailable assets.
9. Shift handover with unresolved production, material, quality, equipment, and receipt issues carried to the next shift.
10. Work-order-level and batch/serial-level genealogy/traceability across plan, BOM/routing version, operation tasks, reports, material lots, quality, equipment, people, downtime, and receipt requests.
11. BusinessGateway MES facade expansion for existing MES read/write service capabilities plus missing standard MES P0 contracts.
12. Minimal cross-domain read/action facades where the MES page needs context:
   - ProductEngineering: production version, MBOM, routing release context.
   - DemandPlanning/ERP: production plan source, planned work order suggestion, sales/order priority context where already available.
   - MasterData: SKU, work center, production line, device asset labels.
   - Quality: inspection task, defect, NCR, rework/scrap disposition context related to work orders and operation reports.
   - WMS/Inventory: stock availability, issue/staging execution status, line-side receipt, finished-goods inbound and stock movement visibility.
   - Maintenance/IndustrialTelemetry: asset unavailable/restored, downtime, alarm, recovery, and capacity impact visibility.
   - ERP Finance: source-document drill-down for production cost evidence where an existing service surface already supports it.
13. Business Console generated client refresh and stable exports.
14. Desktop UI pages with Chinese visible copy.
15. Focused unit, API contract, frontend, and e2e verification.

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
| Cost/source-document drill-down | ERP Finance | ERP Finance | Link to existing ERP Finance candidate/source-document surface only after route and permission are verified. |

## File Structure

Planned file responsibilities:

| Path | Responsibility |
| --- | --- |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs` | Add missing MES read endpoints only when the MES service lacks the page-level query. |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/...` | Query handlers for production readiness, work order detail, material readiness, dispatch task list, operation task list, WIP, production reports, downtime, handover, traceability, schedule result history, and any missing read model. |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/...` | Commands for work order release, dispatch assignment, operation start/pause/resume/complete, material issue request intent, defect entry, downtime entry/recovery, finished-goods receipt request, and shift handover. |
| `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/...` | MES service endpoint and query tests. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs` | Business Console DTOs for MES workbench responses. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs` | Internal HTTP clients for MES and minimal related business read endpoints. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs` | BusinessGateway MES facade endpoints and stable operation IDs. |
| `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs` | Stable route and operationId tests. |
| `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs` | Bearer, permission, context, and downstream proxy tests. |
| `frontend/packages/api-client/src/business-console.ts` | Stable business-console exports after generated client refresh. |
| `frontend/apps/business-console/src/composables/useBusinessMes.ts` | Query/mutation composition for MES PC pages. Split into `src/composables/mes/*.ts` only if the file grows beyond one focused workbench. |
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

All target routes must keep the existing BusinessGateway pattern:

1. Gateway endpoint uses `AuthorizedBusinessProxyEndpoint`.
2. Gateway endpoint permission comes from `BusinessGatewayPermissions`.
3. Gateway forwards `tokenProvider.BearerToken` to business services.
4. Business services stay protected by `InternalServiceAuthorizationPolicy`.
5. Frontend consumes only `@nerv-iip/api-client` stable business-console exports.

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
14. Traceability queries return at least work order, production version, operation tasks, reports, material lots, defects, downtime, receipt request, person, and device links.

- [ ] **Step 2: Implement missing read queries**

Only add MES service queries and commands that do not already exist. Use async EF Core calls with `CancellationToken`, and keep query/endpoint DTOs in the Web/Application layer rather than Domain.

Expected endpoint contract additions:

```csharp
new(typeof(GetMesWorkOrderDetailEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}", MesPermissionCodes.WorkOrdersManage, "getBusinessMesWorkOrderDetail"),
new(typeof(ListOperationTasksEndpoint), "GET", "/api/business/v1/mes/operation-tasks", MesPermissionCodes.WorkOrdersManage, "listBusinessMesOperationTasks"),
new(typeof(GetMaterialReadinessEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}/material-readiness", MesPermissionCodes.WorkOrdersManage, "getBusinessMesMaterialReadiness"),
new(typeof(AssignDispatchTaskEndpoint), "POST", "/api/business/v1/mes/dispatch-tasks/{operationTaskId}/assign", MesPermissionCodes.WorkOrdersManage, "assignBusinessMesDispatchTask"),
new(typeof(RecordDowntimeEventEndpoint), "POST", "/api/business/v1/mes/downtime-events", MesPermissionCodes.SchedulesManage, "recordBusinessMesDowntimeEvent"),
new(typeof(CreateShiftHandoverEndpoint), "POST", "/api/business/v1/mes/shift-handovers", MesPermissionCodes.WorkOrdersManage, "createBusinessMesShiftHandover"),
new(typeof(GetWorkOrderTraceabilityEndpoint), "GET", "/api/business/v1/mes/traceability/work-orders/{workOrderId}", MesPermissionCodes.WorkOrdersManage, "getBusinessMesWorkOrderTraceability"),
```

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
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/Auth/BusinessGatewayPermissions.cs`
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

Map read endpoints to read permissions and write endpoints to manage/write permissions. Keep permission names in `business.mes.*` shape and update authorization matrix only when a new permission code is introduced.

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
- Create if needed: `frontend/apps/business-console/src/composables/mes/useMesWorkbench.ts`
- Create if needed: `frontend/apps/business-console/src/composables/mes/useMesReferenceLabels.ts`
- Test: existing or new Vitest files under `frontend/apps/business-console/src/**/__tests__` or `frontend/apps/business-console/tests`

- [ ] **Step 1: Add query wrappers**

Expose composable functions for:

1. `useMesOverview()`
2. `useMesProductionPlans()`
3. `useMesProductionPlanReadiness(productionPlanId)`
4. `useMesWorkOrders()`
5. `useMesWorkOrderDetail(workOrderId)`
6. `useMesMaterialReadiness(workOrderId)`
7. `useMesMaterialIssueRequests()`
8. `useMesDispatchTasks()`
9. `useMesOperationTasks()`
10. `useMesWipSummary()`
11. `useMesProductionReports()`
12. `useMesQualityContext()`
13. `useMesFinishedGoodsReceiptRequests()`
14. `useMesDowntimeEvents()`
15. `useMesShiftHandovers()`
16. `useMesTraceability()`
17. `useMesCapacityImpacts()`
18. `useMesSchedules()`

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
- Modify if needed: `frontend/apps/business-console/src/layouts/BusinessLayout.vue`
- Test: `frontend/apps/business-console/tests/e2e/business-console.spec.ts`

- [ ] **Step 1: Build the desktop MES navigation**

Add the MES pages to the Business Console navigation with Chinese labels:

| Route | Label |
| --- | --- |
| `/mes` | `生产驾驶舱` |
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

1. Merge API/BFF contract work before page expansion.
2. Implement the standard P0 MES service surface in this order: plan readiness and work order release, material readiness/request, dispatch and operation state, report/quality/downtime, receipt/handover/traceability.
3. Merge generated client and composables immediately after contracts.
4. Merge PC MES pages with Chinese copy and role-oriented navigation.
5. Add minimal cross-domain context as a follow-up if it increases review size too much, but do not drop material readiness, dispatch, downtime, handover, or traceability from the target model.
6. Start WMS workbench, DemandPlanning/MRP, ERP drill-down, Quality deeper workflow, and Maintenance/Telemetry PC pages after MES desktop flow is usable.
7. Start PDA/mobile only after MES PC contracts and primary flows stop changing.

## Acceptance Checklist

- [ ] BusinessGateway exposes the MES PC workbench routes in the Contract Targets table.
- [ ] BusinessGateway tests cover auth, permission, context propagation, internal bearer forwarding, and downstream denial behavior.
- [ ] MES service endpoints exist for P0 execution facts: plan readiness, work order release, material readiness/request intent, dispatch, operation state, WIP, report, defect context, downtime, receipt request, shift handover, and traceability.
- [ ] Generated business-console client exports stable MES workbench functions and types.
- [ ] PC MES routes exist under `frontend/apps/business-console/src/pages/mes` with production cockpit, production plan, material readiness, dispatch, operation execution, report, quality, receipt, downtime, handover, and traceability pages.
- [ ] User-visible MES page copy is Chinese in the first implementation.
- [ ] No page directly calls business service URLs or generated deep imports.
- [ ] MES can see and trigger material issue/staging flow, but WMS/Inventory remain the source of warehouse execution and inventory balances.
- [ ] MES can see quality, downtime, and maintenance context, but Quality and Maintenance remain their own source-of-truth services.
- [ ] Traceability can start from work order, batch/serial, material lot, or defect and return the linked execution evidence.
- [ ] `scripts/verify-business-console-mes-pc-workbench.ps1` passes.
- [ ] `docs/architecture/frontend-structure.md` and `docs/architecture/implementation-readiness.md` are updated after implementation evidence exists.
- [ ] PDA/mobile remains explicitly deferred until PC MES contracts stabilize.
