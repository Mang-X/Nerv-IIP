# Business Console MES PC Completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the PC-first Business Console MES workbench so business teams can operate work orders, operation tasks, production reports, schedule results, finished-goods receipt requests, and related capacity/quality signals from desktop pages before starting PDA/mobile work.

**Architecture:** BusinessGateway remains the Business Console BFF and the only frontend-facing API for `/api/business-console/v1/**`; it performs user bearer validation, IAM permission checks, organization/environment context propagation, and internal service-token calls. MES remains the execution fact owner, while ProductEngineering, MasterData, Quality, WMS/Inventory, Maintenance/IndustrialTelemetry, and ERP are integrated only through minimal read/action facades needed by the MES PC pages. The first release uses direct Chinese UI copy in Vue pages and defers a full i18n catalog workflow.

**Tech Stack:** .NET 10, FastEndpoints, CleanDDD service boundaries, BusinessGateway facade, Hey API generated `@nerv-iip/api-client`, Vue 3, Vite Plus, Pinia Colada, `@nerv-iip/ui`, Playwright.

---

## Baseline Decision

This plan replaces the mobile/PDA-first next-step assumption with a PC-first business implementation sequence:

1. Finish Business Console desktop pages first, with MES as the first deep workbench.
2. Start from API/BFF contracts, because MES pages depend on data from MES plus several neighboring business contexts.
3. Advance MES together with the minimum related business interfaces instead of trying to finish every surrounding system.
4. Defer PDA/mobile until the desktop workflow and generated Business Console contracts are stable.
5. Use Chinese text directly in the first page implementation. The repository has i18n concepts, but the first MES workbench should not pay the cost of a complete translation catalog, locale routing, and copy governance workflow.

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

1. MES desktop overview and workbench pages.
2. BusinessGateway MES facade expansion for existing MES read/write service capabilities.
3. Minimal cross-domain read facades where the MES page needs context:
   - ProductEngineering: production version, MBOM, routing release context.
   - MasterData: SKU, work center, production line, device asset labels.
   - Quality: inspection/NCR context related to work orders and operation reports.
   - WMS/Inventory: finished-goods receipt request and resulting stock movement visibility.
   - Maintenance/IndustrialTelemetry: asset unavailable/restored and capacity impact visibility.
   - ERP Finance: source-document drill-down for production cost evidence where an existing service surface already supports it.
4. Business Console generated client refresh and stable exports.
5. Desktop UI pages with Chinese visible copy.
6. Focused unit, API contract, frontend, and e2e verification.

### Out of Scope

1. PDA/mobile scanning flows.
2. Full APS/Gantt optimization UI. Schedule remains a list/timeline/table workbench unless #78 is explicitly revived.
3. Direct frontend calls to business services.
4. Moving domain rules into BusinessGateway.
5. Full i18n translation catalog, locale switcher, or route-localized copy.
6. Raw PLC/DCS/SCADA control or WCS implementation inside MES.

## Dependency Matrix

| PC MES page need | Fact owner | BusinessGateway approach |
| --- | --- | --- |
| Work order list/detail, operation tasks, production reports | MES | Add page-level MES facade endpoints and map downstream MES DTOs to Business Console DTOs. |
| SKU/work center/device display names | MasterData | Add read-only resource resolution endpoint or reuse existing MasterData resource list where sufficient. |
| Production version, MBOM, routing context | ProductEngineering | Add a narrow read facade only for released production-version summary used by work order detail. |
| Quality holds, inspection records, NCRs | Quality | Add related-quality read endpoint keyed by work order ID and operation task ID. |
| Finished-goods receipt request status | MES first, WMS/Inventory for downstream facts | Surface MES receipt requests first; add WMS/Inventory read links only for posted inbound/stock movement evidence. |
| Maintenance capacity impacts | MES plus Maintenance/IndustrialTelemetry events | Surface existing MES capacity impact query first; enrich labels through MasterData only. |
| Cost/source-document drill-down | ERP Finance | Link to existing ERP Finance candidate/source-document surface only after route and permission are verified. |

## File Structure

Planned file responsibilities:

| Path | Responsibility |
| --- | --- |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs` | Add missing MES read endpoints only when the MES service lacks the page-level query. |
| `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/...` | Query handlers for work order detail, operation task list, schedule result history, and any missing read model. |
| `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/...` | MES service endpoint and query tests. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs` | Business Console DTOs for MES workbench responses. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs` | Internal HTTP clients for MES and minimal related business read endpoints. |
| `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs` | BusinessGateway MES facade endpoints and stable operation IDs. |
| `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs` | Stable route and operationId tests. |
| `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs` | Bearer, permission, context, and downstream proxy tests. |
| `frontend/packages/api-client/src/business-console.ts` | Stable business-console exports after generated client refresh. |
| `frontend/apps/business-console/src/composables/useBusinessMes.ts` | Query/mutation composition for MES PC pages. Split into `src/composables/mes/*.ts` only if the file grows beyond one focused workbench. |
| `frontend/apps/business-console/src/pages/mes/index.vue` | MES overview page. |
| `frontend/apps/business-console/src/pages/mes/work-orders.vue` | Enhanced work order list and quick actions. |
| `frontend/apps/business-console/src/pages/mes/work-orders/[workOrderId].vue` | Work order detail page. |
| `frontend/apps/business-console/src/pages/mes/operation-tasks.vue` | Operation task queue. |
| `frontend/apps/business-console/src/pages/mes/reports.vue` | Production report list and creation entry points. |
| `frontend/apps/business-console/src/pages/mes/receipts.vue` | Finished-goods receipt request visibility. |
| `frontend/apps/business-console/src/pages/mes/schedules.vue` | Rule schedule run and schedule-result table/timeline. |
| `frontend/apps/business-console/src/pages/mes/capacity.vue` | Capacity impact visibility from MES-maintenance integration. |
| `frontend/apps/business-console/tests/e2e/business-console.spec.ts` | Desktop MES navigation and smoke coverage. |
| `scripts/verify-business-console-mes-pc-workbench.ps1` | Governed focused verification script for this plan. |
| `docs/architecture/frontend-structure.md` | Update only after routes are implemented, to keep the Business Console route table current. |
| `docs/architecture/implementation-readiness.md` | Update only after implementation lands and verification evidence exists. |

## Contract Targets

Target BusinessGateway operation IDs:

| Method | Route | Operation ID | Downstream owner |
| --- | --- | --- | --- |
| GET | `/api/business-console/v1/mes/overview` | `getBusinessConsoleMesOverview` | BusinessGateway aggregation over MES queries |
| GET | `/api/business-console/v1/mes/work-orders` | `listBusinessConsoleMesWorkOrders` | Existing MES service list |
| GET | `/api/business-console/v1/mes/work-orders/{workOrderId}` | `getBusinessConsoleMesWorkOrderDetail` | MES service detail query |
| POST | `/api/business-console/v1/mes/work-orders/rush` | `createBusinessConsoleMesRushWorkOrder` | Existing MES service command |
| GET | `/api/business-console/v1/mes/operation-tasks` | `listBusinessConsoleMesOperationTasks` | MES service query |
| GET | `/api/business-console/v1/mes/production-reports` | `listBusinessConsoleMesProductionReports` | Existing MES service list |
| POST | `/api/business-console/v1/mes/production-reports` | `recordBusinessConsoleMesProductionReport` | Existing MES service command |
| GET | `/api/business-console/v1/mes/finished-goods-receipt-requests` | `listBusinessConsoleMesFinishedGoodsReceiptRequests` | Existing MES service list |
| POST | `/api/business-console/v1/mes/finished-goods-receipt-requests` | `createBusinessConsoleMesFinishedGoodsReceiptRequest` | Existing MES service command |
| GET | `/api/business-console/v1/mes/capacity-impacts` | `listBusinessConsoleMesCapacityImpacts` | Existing MES service list |
| GET | `/api/business-console/v1/mes/related-quality-items` | `listBusinessConsoleMesRelatedQualityItems` | Quality read facade |

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

1. `GET /api/business/v1/mes/work-orders/{workOrderId}` returns one work order with operation tasks.
2. `GET /api/business/v1/mes/operation-tasks` filters by organization, environment, status, work center, and work order.
3. Existing `GET /api/business/v1/mes/production-reports` remains available.
4. Existing `GET /api/business/v1/mes/finished-goods-receipt-requests` remains available.
5. Existing `GET /api/business/v1/mes/capacity-impacts` remains available.

- [ ] **Step 2: Implement missing read queries**

Only add MES service queries that do not already exist. Use async EF Core calls with `CancellationToken`, and keep query DTOs in the Web/Application layer rather than Domain.

Expected endpoint contract additions:

```csharp
new(typeof(GetMesWorkOrderDetailEndpoint), "GET", "/api/business/v1/mes/work-orders/{workOrderId}", MesPermissionCodes.WorkOrdersManage, "getBusinessMesWorkOrderDetail"),
new(typeof(ListOperationTasksEndpoint), "GET", "/api/business/v1/mes/operation-tasks", MesPermissionCodes.WorkOrdersManage, "listBusinessMesOperationTasks"),
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

1. MES overview counts and status summaries.
2. Work order detail.
3. Operation task rows.
4. Production report rows.
5. Finished-goods receipt request rows.
6. Capacity impact rows.
7. Related quality item rows.

Keep DTO property names stable and frontend-oriented, for example `workOrderId`, `operationTaskId`, `status`, `workCenterId`, `plannedStartUtc`, `reportedAtUtc`, `qualityStatus`, and `receiptStatus`.

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
2. `useMesWorkOrders()`
3. `useMesWorkOrderDetail(workOrderId)`
4. `useMesOperationTasks()`
5. `useMesProductionReports()`
6. `useMesFinishedGoodsReceiptRequests()`
7. `useMesCapacityImpacts()`
8. `useMesSchedules()`

- [ ] **Step 2: Replace hardcoded context source**

Keep the existing `org-001` and `env-dev` development defaults only behind one explicit app-local helper, so pages can later move to a real context selector without editing every form.

- [ ] **Step 3: Add invalidation rules**

After rush order creation, production report creation, finished-goods receipt request creation, or schedule run, invalidate affected MES queries by operation ID. Reuse the existing `isBusinessQuery` pattern.

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
- Modify: `frontend/apps/business-console/src/pages/mes/work-orders.vue`
- Create: `frontend/apps/business-console/src/pages/mes/work-orders/[workOrderId].vue`
- Create: `frontend/apps/business-console/src/pages/mes/operation-tasks.vue`
- Create: `frontend/apps/business-console/src/pages/mes/reports.vue`
- Create: `frontend/apps/business-console/src/pages/mes/receipts.vue`
- Modify: `frontend/apps/business-console/src/pages/mes/schedules.vue`
- Create: `frontend/apps/business-console/src/pages/mes/capacity.vue`
- Modify if needed: `frontend/apps/business-console/src/layouts/BusinessLayout.vue`
- Test: `frontend/apps/business-console/tests/e2e/business-console.spec.ts`

- [ ] **Step 1: Build the desktop MES navigation**

Add the MES pages to the Business Console navigation with Chinese labels:

| Route | Label |
| --- | --- |
| `/mes` | `MES 总览` |
| `/mes/work-orders` | `生产工单` |
| `/mes/operation-tasks` | `工序任务` |
| `/mes/reports` | `生产报工` |
| `/mes/receipts` | `完工入库` |
| `/mes/schedules` | `规则排程` |
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

1. MasterData labels for SKU, work center, production line, and device asset.
2. ProductEngineering production-version summary on work order detail.
3. Quality related inspection/NCR rows.
4. WMS/Inventory downstream receipt and stock-movement evidence.
5. Maintenance/IndustrialTelemetry capacity-impact labels if the MES query returns only IDs.
6. ERP Finance source-document links only when the existing ERP surface is verified.

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
2. Merge generated client and composables immediately after contracts.
3. Merge PC MES pages with Chinese copy.
4. Add minimal cross-domain context as a follow-up if it increases review size too much.
5. Start WMS workbench, DemandPlanning/MRP, ERP drill-down, Quality deeper workflow, and Maintenance/Telemetry PC pages after MES desktop flow is usable.
6. Start PDA/mobile only after MES PC contracts and primary flows stop changing.

## Acceptance Checklist

- [ ] BusinessGateway exposes the MES PC workbench routes in the Contract Targets table.
- [ ] BusinessGateway tests cover auth, permission, context propagation, internal bearer forwarding, and downstream denial behavior.
- [ ] MES service read endpoints exist for the page data that MES owns.
- [ ] Generated business-console client exports stable MES workbench functions and types.
- [ ] PC MES routes exist under `frontend/apps/business-console/src/pages/mes`.
- [ ] User-visible MES page copy is Chinese in the first implementation.
- [ ] No page directly calls business service URLs or generated deep imports.
- [ ] `scripts/verify-business-console-mes-pc-workbench.ps1` passes.
- [ ] `docs/architecture/frontend-structure.md` and `docs/architecture/implementation-readiness.md` are updated after implementation evidence exists.
- [ ] PDA/mobile remains explicitly deferred until PC MES contracts stabilize.
