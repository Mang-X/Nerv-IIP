# MAN-517 Sales Order Demand Source Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bridge real ERP sales-order lifecycle facts into version-ordered DemandPlanning demand sources and expose sales-order traceability in Planning.

**Architecture:** ERP publishes three concrete ADR 0011 lifecycle snapshot events from aggregate domain events. DemandPlanning consumes them through CAP with a durable inbox, dead-letter store, and order-version watermark, then reconciles per-line demand sources in one explicit transaction. Existing Gateway/Planning surfaces are extended only where the new site fact and drill-through require it.

**Tech Stack:** .NET 10, EF Core/PostgreSQL, NetCorePal domain events/UoW, DotNetCore.CAP Redis Streams, FastEndpoints, Vue 3, Vue Router, Vitest, pnpm/Vite+.

---

### Task 1: Freeze lifecycle contracts and ERP state transitions

**Files:**
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Erp/ErpIntegrationEvents.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesOrderAggregate/SalesOrder.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpDomainEvents.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesFinanceIntegrationEventConverters.cs`
- Test: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpSalesFinanceAggregateTests.cs`
- Test: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSalesOrderDemandIntegrationEventTests.cs`

- [ ] Write failing aggregate tests for initial release, credit-hold release versioning, released-line change, and cancellation facts.
- [ ] Run the focused domain tests and confirm failures are caused by missing lifecycle events.
- [ ] Add the three concrete contract records, payload/line snapshot types, domain events, and minimal aggregate transitions.
- [ ] Run domain tests to green.
- [ ] Write failing converter tests for envelope fields, full line snapshots, event types, version, correlation, and the safe stable idempotency key.
- [ ] Implement the three converters and run converter plus repository integration-contract tests to green.

### Task 2: Persist the authoritative sales-order site

**Files:**
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesOrderAggregate/SalesOrder.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Sales/ErpSalesCommands.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpSalesFinanceEndpoints.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpSalesFinanceEntityTypeConfigurations.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/20260718143000_AddSalesOrderDemandBridge.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Test: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSalesFinanceEndpointContractTests.cs`
- Test: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`

- [ ] Write failing command/endpoint/schema tests requiring non-empty `SiteCode` and a commented `site_code` column.
- [ ] Run the focused tests and confirm the expected failures.
- [ ] Thread `SiteCode` through request, command, aggregate, list model, and mapping; add validation.
- [ ] Generate the PostgreSQL migration with the governed EF command and update catalog comments.
- [ ] Run the focused ERP tests to green.

### Task 3: Add DemandPlanning projection persistence

**Files:**
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/DemandSourceAggregate/DemandSource.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/IntegrationEvents/SalesOrderDemandProjection.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/IntegrationEvents/ProcessedIntegrationEvent.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/DemandSourceEntityTypeConfiguration.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/SalesOrderDemandProjectionEntityTypeConfiguration.cs`
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/20260718144500_AddSalesOrderDemandBridge.cs`
- Test: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/DemandPlanningAggregateTests.cs`
- Test: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningSchemaConventionTests.cs`

- [ ] Write failing domain tests for active update and explainable zero-quantity cancellation.
- [ ] Run them red, implement the minimal DemandSource lifecycle fields/methods, and run green.
- [ ] Write failing model/schema tests for line uniqueness, watermark, inbox, dead-letter tables, comments, and indexes.
- [ ] Add the persistence entities/configuration and required messaging project references.
- [ ] Generate the migration and run schema tests to green.

### Task 4: Implement the CAP consumer with explicit atomic persistence

**Files:**
- Create: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEventHandlers/ErpSalesOrderDemandIntegrationEventHandlers.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Program.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Nerv.IIP.Business.DemandPlanning.Web.csproj`
- Test: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/ErpSalesOrderDemandConsumerTests.cs`

- [ ] Write failing tests for release projection, multi-line uniqueness, duplicate delivery, stale version no-op, version-gap snapshot convergence, cancellation tombstone, invalid payload dead-letter, and explicit `SaveChangesAsync` persistence.
- [ ] Run focused consumer tests and observe the missing-handler failures.
- [ ] Implement a shared reconciler plus three concrete CAP subscriptions using concrete type names, consumer guard, persistent inbox/dead letter, watermark comparison, and explicit transaction/save.
- [ ] Run consumer tests to green and refactor only after green.

### Task 5: Preserve MRP traceability

**Files:**
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/RunMrpCommand.cs`
- Test: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/PlanningInputAdapterTests.cs`
- Test: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/MrpCalculatorTests.cs`

- [ ] Write a failing test proving cancelled zero-quantity sales demand is excluded while active `SO-DEMO-001` reaches pegging and planned-work-order suggestions.
- [ ] Run red, add the minimal active-demand filtering/source propagation, then run green.

### Task 6: Extend exposed contracts and frontend drill-through

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `frontend/apps/business-console/src/composables/useBusinessErp.ts`
- Modify: `frontend/apps/business-console/src/pages/erp/sales/orders.vue`
- Modify: `frontend/apps/business-console/src/components/planning/PlanningWorkbench.vue`
- Test: `frontend/apps/business-console/src/pages/erp/sales.test.ts`
- Test: `frontend/apps/business-console/src/components/planning/PlanningWorkbench.test.ts`
- Generated: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/OpenApi/business-gateway-console.v1.json`
- Generated: `frontend/packages/api-client/src/generated/**`
- Verify: `frontend/packages/api-client/src/business-console.ts`

- [ ] Write failing frontend tests for required sales-order site input, Planning source link, and route-query keyword hydration.
- [ ] Implement the minimal form/model/link/query behavior and run focused Vitest tests green.
- [ ] Declare the changed create-sales-order service endpoint and existing facade as `exposed` in the facade coverage matrix if its contract row needs updating.
- [ ] Export Gateway OpenAPI with `scripts/export-gateway-openapi.ps1`; run `pnpm -C frontend generate:api`; do not hand-edit generated files.
- [ ] Run api-client and frontend typecheck/tests/build plus touched-file formatting.

### Task 7: Add real PostgreSQL + Redis cross-process acceptance

**Files:**
- Create: `backend/tests/Nerv.IIP.Business.FullChain.Tests/SalesOrderDemandPlanningPostgresRedisAcceptanceTests.cs`
- Modify: `backend/tests/Nerv.IIP.Business.FullChain.Tests/Nerv.IIP.Business.FullChain.Tests.csproj`
- Create or modify: `scripts/verify-erp-sales-order-demand-planning.ps1`
- Test: `scripts/tests/erp-sales-order-demand-planning-verify-script.Tests.ps1`

- [ ] Write the failing script contract test requiring governed helpers, process cleanup, Redis provider, separate ERP/DemandPlanning processes, and evidence output.
- [ ] Implement the disposable PostgreSQL/Redis acceptance harness and separate-process service launch without embedding secrets.
- [ ] Run the script governance gate and contract test.
- [ ] Run the real acceptance and prove initial release, duplicate replay, stale/out-of-order event, change, and cancellation convergence.

### Task 8: Synchronize governance and product documentation

**Files:**
- Modify: `docs/architecture/integration-event-consumption-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/facade-coverage-matrix.json` when required by the changed endpoint contract
- Modify: `docs/architecture/facade-coverage-matrix.md` when narrative changes
- Modify: `frontend/apps/docs/docs/getting-started/planning-to-finished-goods.md`

- [ ] Add the three ERP producer/consumer rows and explain snapshot/version/tombstone semantics.
- [ ] Document ERP/DemandPlanning schema additions and comments.
- [ ] Document reusable `SO-DEMO-001` prerequisites: customer/credit, approved quotation, site, release, MRP, and traceability checks.
- [ ] Verify no MAN-518 fulfillment timeline or MAN-524 whole-chain scope entered the diff.

### Task 9: Verify, review, and publish the ready PR

**Files:** all files changed above.

- [ ] Run focused tests after every red-green cycle, then full `dotnet test backend/Nerv.IIP.sln`.
- [ ] Run integration contract, facade coverage, migration/schema, OpenAPI drift, frontend typecheck/test/build, touched-file format, and script-governance gates.
- [ ] Run `git diff --check`, inspect the complete diff against `origin/main`, and verify generated artifacts were produced by the governed commands.
- [ ] Use `requesting-code-review` for a final fact-based review and address only MAN-517 findings.
- [ ] Use `verification-before-completion` to record fresh evidence.
- [ ] Use `finishing-a-development-branch`, commit, push, and create a ready PR via `gh` titled `MAN-517 #958 ...` with Fix / Tests / Risk / OpenAPI or schema impact / 产品文档影响 and `Fixes #958`.
- [ ] Stop after PR creation; do not merge.
