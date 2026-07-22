# Order Urgency Model V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver one audited, explainable order-urgency result across sales, demand, scheduling, and MES work-order entry points.

**Architecture:** BusinessScheduling owns deterministic derived urgency snapshots and audited business-priority settings. It calculates from normalized scheduling problem/plan facts, exposes three governed endpoints through BusinessGateway, and the Business Console renders the same facade result through a shared Vue component.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, FastEndpoints, MediatR, BusinessGateway, OpenAPI/Hey API, Vue 3, TanStack Colada, NvUI, Vitest.

---

### Task 1: Freeze the explainable contract and pure calculator

**Files:**
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/OrderUrgencyAggregate/OrderUrgencyModels.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/Services/OrderUrgencyCalculator.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/OrderUrgencyCalculatorTests.cs`

- [ ] Write failing domain tests for P0/P1, CR/Slack thresholds, material/equipment/quality/tooling/capacity risks, stale/missing facts, stable reason ordering and time-driven upgrades.
- [ ] Run `dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj --filter OrderUrgencyCalculatorTests` and confirm failures identify missing types.
- [ ] Add V1 enums/records and a pure calculator that accepts `calculatedAtUtc` and normalized facts explicitly; it must not access time, HTTP or persistence.
- [ ] Rerun the focused tests and confirm they pass.

### Task 2: Persist priority audit and idempotent urgency snapshots

**Files:**
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/OrderUrgencyAggregate/OrderUrgencyBusinessPriority.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/OrderUrgencyAggregate/OrderUrgencySnapshot.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/OrderUrgencyBusinessPriorityEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/OrderUrgencyBusinessPriorityChangeEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/OrderUrgencySnapshotEntityTypeConfiguration.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/OrderUrgencyBusinessPriorityTests.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/OrderUrgencyPersistenceTests.cs`

- [ ] Write failing tests proving required reason/actor, monotonic revision, expiry, append-only changes and unique idempotency key behavior.
- [ ] Run the focused domain/web tests and confirm the expected failures.
- [ ] Implement aggregates, mappings and DbSets with scoped unique indexes and comments.
- [ ] Rerun focused tests and confirm pass.

### Task 3: Compose current scheduling facts and recalculate deterministically

**Files:**
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingProblemProducer.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyFactAssembler.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyRecalculationService.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyRefreshWorker.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/CreateSchedulePlanCommand.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/RecordSchedulePlanInvalidationsCommand.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Program.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/OrderUrgencyFactAssemblerTests.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/OrderUrgencyRecalculationTests.cs`

- [ ] Write failing tests for latest scoped problem/plan selection, aliases, CR/Slack inputs, all five execution-risk sources, stale snapshots, event-triggered refresh and duplicate refresh.
- [ ] Run the focused tests and confirm failures.
- [ ] Implement normalized fact assembly from persisted Scheduling JSON/plan entities and stable input fingerprinting.
- [ ] Implement immediate and periodic refresh with hourly/15-minute buckets and duplicate-key recovery.
- [ ] Rerun focused tests and confirm pass.

### Task 4: Add governed Scheduling endpoints

**Files:**
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/SetOrderUrgencyBusinessPriorityCommand.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Queries/OrderUrgencyQueries.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints/Scheduling/SchedulingEndpoints.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Auth/SchedulingPermissionCodes.cs`
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEndpointContractTests.cs`
- Create: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/OrderUrgencyCommandQueryTests.cs`

- [ ] Write failing endpoint/handler tests for scoped list/detail, reference aliases, missing/stale fail-closed output, priority validation and history.
- [ ] Run the focused Scheduling web tests and confirm failures.
- [ ] Add FastEndpoints contracts and MediatR handlers for list, detail and priority update.
- [ ] Rerun focused Scheduling tests and confirm pass.

### Task 5: Add BusinessGateway facade and actor injection

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Scheduling/BusinessConsoleSchedulingEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Program.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `docs/architecture/facade-coverage-matrix.json`
- Modify: `docs/architecture/facade-coverage-matrix.md`

- [ ] Write failing Gateway tests that prove all three facade operations, permission enforcement, route forwarding and authenticated actor injection.
- [ ] Run the focused Gateway tests and confirm failures.
- [ ] Implement models, client methods and endpoints; clients never accept actor from the browser request.
- [ ] Register all three Scheduling endpoints as `exposed` with matching gateway operation IDs and update matrix counts.
- [ ] Rerun focused Gateway and facade-coverage tests.

### Task 6: Generate migration, schema docs and public client

**Files:**
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/*AddOrderUrgencyModelV1*.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify generated OpenAPI snapshots and `frontend/packages/api-client` only through governed export/codegen commands.

- [ ] Run PostgreSQL-profile `dotnet-ef migrations add AddOrderUrgencyModelV1` with the documented project/startup paths.
- [ ] Inspect generated tables, indexes and comments; update the schema catalog and readiness narrative.
- [ ] Run the governed OpenAPI export command discovered in `api-contract-and-codegen.md`.
- [ ] Run `pnpm -C frontend generate:api`; verify the three operations and types are present in the stable business-console barrel.
- [ ] Run schema convention and API-client contract tests.

### Task 7: Build shared Vue urgency presentation

**Files:**
- Create: `frontend/apps/business-console/src/composables/useOrderUrgency.ts`
- Create: `frontend/apps/business-console/src/components/urgency/OrderUrgencyCell.vue`
- Create: `frontend/apps/business-console/src/components/urgency/OrderUrgencyDisplayMode.vue`
- Create: `frontend/apps/business-console/src/components/urgency/OrderUrgencyDetailSheet.vue`
- Create: `frontend/apps/business-console/src/components/urgency/OrderUrgencyCell.test.ts`
- Create: `frontend/apps/business-console/src/composables/useOrderUrgency.test.ts`

- [ ] Write failing Vitest cases for shared alias lookup, five display modes, hover summary, Sheet details, reason codes, audit history, direct Scheduling link and priority update payload.
- [ ] Run the two focused Vitest files and confirm failures.
- [ ] Implement the composable with generated query/mutation options and empty-scope suppression.
- [ ] Implement NvUI-only presentation with semantic status tones and no business calculation.
- [ ] Rerun focused tests and confirm pass.

### Task 8: Integrate the four acceptance entry points

**Files:**
- Modify: `frontend/apps/business-console/src/pages/erp/sales/orders.vue`
- Modify: `frontend/apps/business-console/src/components/planning/PlanningWorkbench.vue`
- Modify: `frontend/apps/business-console/src/pages/mes/work-orders/index.vue`
- Modify: `frontend/apps/business-console/src/pages/scheduling.vue`
- Modify: `frontend/apps/business-console/src/pages/erp/sales.test.ts`
- Modify: `frontend/apps/business-console/src/components/planning/PlanningWorkbench.test.ts`
- Create: `frontend/apps/business-console/src/pages/mes/work-orders/index.test.ts`
- Modify: `frontend/apps/business-console/src/pages/scheduling.test.ts`

- [ ] Add failing page tests proving the same snapshot is indexed by sales order number, demand source reference, MES work-order ID/source reference and Scheduling assignment order ID.
- [ ] Run focused page tests and confirm failures.
- [ ] Add urgency columns/mode control to sales, demand and MES lists and urgency cells to Scheduling assignment details; do not edit Gantt/Canvas components.
- [ ] Add/update the user-facing docs page for the changed workflow, or record the exact existing product-doc location if already covered.
- [ ] Run focused page tests and confirm pass.

### Task 9: Verify the whole change and prepare the PR

**Files:**
- All files changed by Tasks 1-8.

- [ ] Run touched-file formatting checks: `pnpm -C frontend exec vp fmt --check <each touched frontend file>`.
- [ ] Run Scheduling domain/web tests, BusinessGateway tests, facade coverage, schema convention and contract serialization tests.
- [ ] Run `dotnet test backend/Nerv.IIP.sln`.
- [ ] Run `pnpm -C frontend typecheck`, `pnpm -C frontend test`, and `pnpm -C frontend build`.
- [ ] Review `git diff --check`, the complete diff and generated artifacts; verify no #178-owned SchedulingCanvas/Gantt files changed.
- [ ] Commit, push `codex/man-584-urgency-v1`, create a ready PR with `Closes #1053`, test evidence, migration/OpenAPI/facade declarations, risks and product-doc impact, then stop without merging.
