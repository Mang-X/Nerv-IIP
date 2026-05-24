# Business Console MVP Design

## Context

BusinessMasterData, Inventory, Quality and MES backend services are already present
in the repository and are recorded in `docs/architecture/implementation-readiness.md`.
The missing architectural pieces for #166 to #169 are the business frontend entry,
the business BFF, and the OpenAPI to api-client generation path.

ADR 0012 explicitly keeps industry business pages out of the main platform console.
The current `frontend/apps/console/src/pages/business/index.vue` is only a status
page and does not maintain business facts. The upcoming SKU maintenance, stock
availability, stock movement, stock count, inspection, NCR, work order and schedule
pages are real business CRUD and workflow surfaces. They must therefore use a
business application entry and a business gateway rather than expanding the
PlatformGateway or the main console.

## Decision

Implement the Business Console MVP with a new `frontend/apps/business-console`
application and a new `backend/gateway/BusinessGateway` BFF.

BusinessGateway owns page-level business facade endpoints under
`/api/business-console/v1/**`. It authenticates the console user, asks IAM for the
required permission, and calls business services with the internal service token.
It does not own business facts, persistence, scheduling rules, stock calculations,
inspection disposition rules, or MES execution rules.

The frontend consumes only generated `@nerv-iip/api-client` exports for
BusinessGateway. Business pages must not directly call `backend/services/Business`
service URLs, and must not consume generated files through deep imports.

## Goals

1. Keep ADR 0012 intact while delivering the first business CRUD/workflow console.
2. Add a complete BusinessGateway OpenAPI export and api-client generation path.
3. Provide a small but real vertical slice for #166, #167, #168 and #169.
4. Reuse the existing Calm Control Plane design system and app shell primitives.
5. Keep BusinessGateway thin: authorization, request shaping, response shaping and
   downstream proxying only.

## Non-Goals

1. Do not move business pages into `frontend/apps/console`.
2. Do not add MasterData, Inventory, Quality or MES facade endpoints to
   PlatformGateway.
3. Do not introduce shared database tables, cross-schema foreign keys or service
   implementation references between BusinessGateway and business services.
4. Do not build a Gantt chart for #169.
5. Do not extract `frontend/packages/auth` in this MVP unless a concrete blocker
   appears. Authentication can start as app-local code aligned with the existing
   console implementation.

## Architecture

```text
frontend/apps/business-console
  -> @nerv-iip/api-client business-console stable exports
  -> BusinessGateway /api/business-console/v1/**
  -> IAM permission check
  -> BusinessMasterData / Inventory / Quality / MES internal APIs
```

BusinessGateway uses HTTP clients for the four MVP services:

1. MasterData, default local base URL `http://localhost:5107`.
2. Inventory, default local base URL `http://localhost:5109`.
3. Quality, default local base URL `http://localhost:5110`.
4. MES, default local base URL `http://localhost:5111`.

BusinessGateway also uses IAM auth and authorization clients in the same style as
PlatformGateway. User-facing requests carry the user's bearer token to BusinessGateway.
Downstream business service calls use `IInternalServiceTokenProvider` because those
service APIs are internal-service protected. IAM remains the final permission source.

## Backend BFF Contract

BusinessGateway endpoints use stable lower camel case operation IDs with the
`BusinessConsole` prefix, for example:

| operationId | Route | Downstream service |
| --- | --- | --- |
| `listBusinessConsoleSkus` | `GET /api/business-console/v1/master-data/skus` | MasterData list resources filtered to SKU. |
| `createBusinessConsoleSku` | `POST /api/business-console/v1/master-data/skus` | MasterData create SKU. |
| `listBusinessConsoleMasterDataResources` | `GET /api/business-console/v1/master-data/resources` | MasterData list resources. |
| `getBusinessConsoleInventoryAvailability` | `GET /api/business-console/v1/inventory/availability` | Inventory availability query. |
| `postBusinessConsoleInventoryMovement` | `POST /api/business-console/v1/inventory/movements` | Inventory movement posting. |
| `createBusinessConsoleInventoryCountTask` | `POST /api/business-console/v1/inventory/count-tasks` | Inventory count task creation. |
| `confirmBusinessConsoleInventoryCountAdjustment` | `POST /api/business-console/v1/inventory/count-tasks/{countTaskId}/adjustments` | Inventory count adjustment. |
| `listBusinessConsoleQualityInspectionPlans` | `GET /api/business-console/v1/quality/inspection-plans` | Quality inspection plans. |
| `createBusinessConsoleQualityInspectionRecord` | `POST /api/business-console/v1/quality/inspection-records` | Quality inspection records. |
| `listBusinessConsoleQualityNcrs` | `GET /api/business-console/v1/quality/ncrs` | Quality NCR list. |
| `submitBusinessConsoleQualityNcrDisposition` | `POST /api/business-console/v1/quality/ncrs/{ncrId}/disposition` | Quality NCR disposition. |
| `closeBusinessConsoleQualityNcr` | `POST /api/business-console/v1/quality/ncrs/{ncrId}/close` | Quality NCR close. |
| `listBusinessConsoleMesWorkOrders` | `GET /api/business-console/v1/mes/work-orders` | MES work order list. |
| `createBusinessConsoleMesRushWorkOrder` | `POST /api/business-console/v1/mes/work-orders/rush` | MES rush work order creation. |
| `runBusinessConsoleMesSchedule` | `POST /api/business-console/v1/mes/schedules/run` | MES rule schedule run. |
| `recordBusinessConsoleMesProductionReport` | `POST /api/business-console/v1/mes/production-reports` | MES production report. |

The BFF may rename page-level request fields, add defaults and normalize response
shapes for the business console, but it must not calculate domain outcomes that
belong to downstream services. Examples that must stay downstream are stock
available quantity, NCR state transitions and schedule result generation.

## API Client Generation

Add a BusinessGateway console OpenAPI snapshot:

```text
frontend/packages/api-client/openapi/business-gateway-console.v1.json
```

Generate it into a separate directory:

```text
frontend/packages/api-client/src/generated/business-console/
```

Add a stable handwritten export:

```text
frontend/packages/api-client/src/business-console.ts
```

The generated BusinessGateway console code must stay isolated from the existing
PlatformGateway generated code and the planned mobile generated code. `src/index.ts`
can re-export business-console public types and helpers, but app pages should import
from stable package entries rather than `src/generated/**`.

## Frontend Application

Create `frontend/apps/business-console` as the first real business frontend. It
uses Vue 3, Vite, Vue Router file routes, Pinia, Pinia Colada, lucide icons,
`@nerv-iip/ui`, `@nerv-iip/app-shell` and `@nerv-iip/api-client`.

The app structure mirrors the existing console app:

```text
frontend/apps/business-console/
  package.json
  vite.config.ts
  tsconfig.json
  src/
    main.ts
    App.vue
    router/
    layouts/
    pages/
    components/
    composables/
    stores/
    api/
```

Authentication starts app-local, aligned with the current console auth pattern. The
Business Console app can call PlatformGateway Console Auth endpoints for login,
refresh, logout and `/me`, while all business data pages call BusinessGateway. If
a second app-local auth copy creates real maintenance pain during implementation,
the extraction target is `frontend/packages/auth`, but that extraction is not a
prerequisite for the MVP.

## Page Scope

### #166 MasterData Layer 0

Deliver SKU-centered master data pages:

1. SKU list with search/filter and status indicators.
2. Create SKU form using UOM, category, material type, tracking policy, shelf-life,
   storage condition, default barcode rule, quality required and compliance tags.
3. Read-only resource lists for UOM, sites, production lines, work centers and
   device assets when needed by downstream forms.

Editing existing SKU records is intentionally limited to page structure and API
placement until the backend exposes update endpoints. The UI must not fake updates.

### #167 Inventory

Deliver inventory operations that reflect existing service facts:

1. Stock availability query by organization, environment, SKU, UOM, site, optional
   location, lot and serial.
2. Stock movement posting with source metadata, idempotency key, quality status,
   owner and quantity.
3. Stock count task creation and count adjustment confirmation.

### #168 Quality

Deliver Quality pages around inspection and NCR:

1. Inspection plan list.
2. Inspection record creation.
3. NCR list and detail-oriented sheet.
4. NCR disposition submission and close action.

### #169 MES

Deliver MES pages without Gantt:

1. Work order list.
2. Rush work order creation.
3. Rule schedule run and schedule result list/state where the existing MES API
   exposes it.
4. Production report creation and finished-goods receipt request visibility where
   available through existing endpoints.

## Error Handling

BusinessGateway maps downstream `ResponseData` failures and HTTP failures to the
same response envelope style used by PlatformGateway. It must preserve 401 and 403
semantics for unauthenticated and unauthorized users. Downstream 4xx responses are
shown as business form or page errors. Downstream 5xx and invalid responses are
shown as BFF errors without leaking service URLs, internal tokens or stack traces.

Frontend pages use existing alert, empty, skeleton, dialog, sheet and table
patterns from `@nerv-iip/ui`. Destructive or irreversible actions require explicit
confirmation when the backend action changes workflow state.

## Testing

Backend tests:

1. BusinessGateway OpenAPI operationId tests for every MVP route.
2. Authorization tests proving each route checks the expected IAM permission.
3. Downstream proxy tests proving user bearer tokens are not sent to internal
   business services and internal service tokens are used.
4. Error mapping tests for downstream failure envelopes and forbidden permission
   checks.

Frontend tests:

1. App bootstrap, auth guard and route smoke tests.
2. Composable tests for MasterData, Inventory, Quality and MES query/mutation
   wrappers.
3. Page tests for list, empty, error, submit success and permission-denied states.
4. Playwright checks for desktop and mobile layouts after the first pages are wired.

Verification commands for implementation:

```powershell
dotnet test backend/Nerv.IIP.sln
pnpm -C frontend generate:api
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

If scripts are added or changed, also run:

```powershell
scripts/check-script-governance.ps1
```

## Documentation Updates

The implementation must keep these documents aligned:

1. `docs/adr/0012-business-platform-domain-layering.md`
2. `docs/architecture/api-contract-and-codegen.md`
3. `docs/architecture/business-platform-domain-architecture.md`
4. `docs/architecture/frontend-structure.md`
5. `docs/architecture/repo-layout.md`
6. `docs/architecture/implementation-readiness.md`

Readiness should only claim code has landed after BusinessGateway, api-client
generation and business-console pages exist and verification has passed or the
remaining environment blockers are explicitly recorded.

## Risks And Mitigations

1. Auth duplication between console apps can drift. Keep the first version small,
   then extract `frontend/packages/auth` only after shared behavior is concrete.
2. BusinessGateway can become a business rule host. Tests and code review must keep
   calculations and state transitions in downstream business services.
3. Operation IDs can collide across generated clients. Use the `BusinessConsole`
   prefix and generate into `src/generated/business-console/`.
4. Backend services may lack update/detail endpoints for some desired UI flows. The
   MVP should show honest create/list/workflow surfaces and report missing backend
   endpoints rather than fake unsupported behavior.
