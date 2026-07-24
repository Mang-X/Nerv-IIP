# MAN-595 Work-Center Cost-Rate Governance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a public, audited, effective-dated ERP work-center cost-rate configuration and prove the PostgreSQL/Redis report-to-Inventory cost chain.

**Architecture:** ERP persists append-only scoped rate revisions and selects the latest effective revision at the report occurrence time. BusinessGateway provides the second public hop with authenticated actor forwarding; generated OpenAPI artifacts expose the contract. The leader-demo full-stack scenario configures the rate through that facade and removes its explicit finished-goods cost bypass.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, FastEndpoints, MediatR, BusinessGateway HTTP facade, Hey API/pnpm, Playwright, Aspire, Redis Streams.

---

### Task 1: ERP versioned rate domain, persistence, command, query, and consumer

**Files:**
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/WorkOrderCostAggregate/WorkOrderCost.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpCostAccountingEntityTypeConfigurations.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/ConfigureWorkCenterCostRateCommand.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/Finance/WorkCenterCostRateQueries.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpWorkCenterCostRateEndpoints.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpProcurementEndpoints.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/WorkOrderCostIntegrationEventHandlers.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/WorkCenterCostRateApplicationTests.cs`
- Modify: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/WorkOrderCostEventClosureTests.cs`
- Modify: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpCostAccountingPostgresAcceptanceTests.cs`
- Create/modify through EF: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/*GovernWorkCenterCostRates*`

- [ ] **Step 1: Write failing domain/application tests.** Cover normalized currency, positive rate, UTC effective interval, audit metadata, monotonic revisions, organization/environment isolation, required work-center filter, latest-effective selection, expired/missing fail-closed, and configure-then-replay of the same MES event.
- [ ] **Step 2: Run the focused tests and confirm RED.** Run `dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --filter "FullyQualifiedName~WorkCenterCostRate|FullyQualifiedName~WorkOrderCostEventClosure"`; failures must be missing types/behavior rather than test setup errors.
- [ ] **Step 3: Implement the minimal append-only model and handlers.** Add `Define(... hourlyRate, currencyCode, effectiveFromUtc, effectiveToUtc, revision, changedBy, reason, changedAtUtc)`, `IsEffectiveAt(atUtc)`, command-side next revision calculation, history DTO/query, and consumer `Where(scope && workCenter && from <= reportedAt && (to == null || reportedAt < to)).OrderByDescending(revision).FirstOrDefaultAsync(...)`.
- [ ] **Step 4: Add FastEndpoints contracts.** Register POST with `ErpPermissionCodes.FinanceManage` and GET with `ErpPermissionCodes.FinanceRead`; write actor comes from `IErpIntegrationEventContextAccessor.GetContext().Actor`, not request JSON.
- [ ] **Step 5: Add and inspect the PostgreSQL migration.** Set the existing-row backfill values from the design, replace the old unique index, add unique revision and effective lookup indexes, update the model snapshot, and keep `erp` schema/history conventions.
- [ ] **Step 6: Verify GREEN.** Run ERP domain/web tests plus the real PostgreSQL acceptance when `NERV_IIP_TEST_POSTGRES` is available. Confirm missing rate does not write inbox state and a later replay succeeds after configuration.
- [ ] **Step 7: Commit.** Commit only Task 1 files with message `feat(erp): govern work-center cost rates`.

### Task 2: BusinessGateway facade, coverage registry, OpenAPI, and generated client

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Create: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Erp/BusinessConsoleErpWorkCenterCostRateEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiContractTests.cs`
- Modify: `docs/architecture/facade-coverage-matrix.json`
- Refresh: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Refresh: `frontend/packages/api-client/src/generated/business-console/*`
- Modify if required by the generator: `frontend/packages/api-client/src/business-console.ts`

- [ ] **Step 1: Write failing Gateway proxy/OpenAPI tests.** Require read/write finance permissions, exact org/environment forwarding, canonical `X-Authenticated-Actor` forwarding on writes, stable operation IDs `configureBusinessConsoleErpWorkCenterCostRate` and `listBusinessConsoleErpWorkCenterCostRates`, and downstream failure propagation.
- [ ] **Step 2: Run focused Gateway tests and confirm RED.** Run the named proxy/OpenAPI tests with a filter for work-center cost rate.
- [ ] **Step 3: Implement the two facade endpoints and client methods.** Reuse `ErpFinanceRead/Manage`; require canonical principal actor and forward it as the trusted ERP actor header while keeping scope in the request/query.
- [ ] **Step 4: Register both ERP service endpoints as `exposed`.** Add exact service routes and Gateway operation IDs to `facade-coverage-matrix.json` and update the narrative/readiness docs only with facts delivered by this change.
- [ ] **Step 5: Export OpenAPI and regenerate.** Use the repository OpenAPI export workflow, then `pnpm -C frontend generate:api`; do not hand-edit snapshots or generated code. Confirm stable exports include both operation helpers and DTOs.
- [ ] **Step 6: Verify GREEN.** Run focused Gateway tests, `dotnet test backend/tests/Nerv.IIP.FacadeCoverage.Tests/Nerv.IIP.FacadeCoverage.Tests.csproj`, `pnpm -C frontend typecheck`, and generated-artifact checks.
- [ ] **Step 7: Commit.** Commit Task 2 files with message `feat(gateway): expose work-center cost rates`.

### Task 3: PostgreSQL/Redis main-chain evidence and documentation

**Files:**
- Modify: `frontend/apps/business-console/e2e/leader-demo-main-chain.spec.ts`
- Modify: `frontend/apps/business-console/src/leaderDemoMainChain.contract.test.ts`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/integration-event-consumption-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Write a failing source-contract test.** Require a public work-center rate configuration step before production reporting, require `currencyCode`, effective timestamps, audit reason, and forbid `unitCost: finishedGoodsUnitCost` in the finished-goods receipt request.
- [ ] **Step 2: Run the contract test and confirm RED.** Run the single Vitest contract test and confirm it fails on the missing rate step/remaining explicit unit-cost bypass.
- [ ] **Step 3: Update the Playwright scenario.** Configure the run-scoped rate through BusinessGateway, record a redacted evidence node, omit explicit FGR cost, and assert the later public inventory-link unit cost/movement is derived from ERP capitalization. Keep all public-only and cleanup rules.
- [ ] **Step 4: Update architecture facts.** Document ownership, append-only revision selection, currency/effective scope, dead-letter replay semantics, migration/table/index changes, facade two-hop status, and the eventual real session ID/results.
- [ ] **Step 5: Run static checks.** Run the contract test and per-file formatter check for every touched frontend file.
- [ ] **Step 6: Run the managed real chain.** Execute `.\\nerv.ps1 fullstack run -Scenario leader-demo-main-chain` without `-NoBuild`; inspect `leader-demo-main-chain-evidence.json` and require PostgreSQL, Redis, rate-configured, cost-capitalized, MES unit cost, Inventory movement/link, stopped session, and empty cleanup residue.
- [ ] **Step 7: Commit.** Commit Task 3 files with message `test(demo): prove governed work-center costing`.

### Task 4: Final verification and ready PR

**Files:**
- Inspect all changed files and generated artifacts.

- [ ] **Step 1: Run the backend solution test gate.** `dotnet test backend/Nerv.IIP.sln` must finish with zero failures and no new warnings.
- [ ] **Step 2: Run frontend gates.** `pnpm -C frontend typecheck`, `pnpm -C frontend test`, and `pnpm -C frontend build`; run `pnpm -C frontend exec vp fmt --check <each touched frontend file>` and distinguish baseline failures if any.
- [ ] **Step 3: Run facade/schema/script gates.** Run facade coverage, ERP schema conventions/PostgreSQL acceptance, and script governance for any touched scripts.
- [ ] **Step 4: Review requirements against the diff.** Confirm no demo seed/database write, no scope leakage, no default rate, no weakened dead-letter behavior, exact two-hop artifacts, and real runtime evidence.
- [ ] **Step 5: Push and create a ready PR.** Use `gh`, title with MAN-595, body containing `Fixes #1070`, Linear MAN-595 URL, per-endpoint `exposed` declarations, docs impact, test evidence, and real session evidence. Do not merge.
