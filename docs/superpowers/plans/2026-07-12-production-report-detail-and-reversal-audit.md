# Production Report Detail and Reversal Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a complete production-report detail read path and principal-derived reversal audit record for issues #881 and #882.

**Architecture:** MES remains the source of production-report and consumption facts. BusinessGateway exposes the detail through its authenticated facade and injects the reversal actor; the generated client feeds an on-demand dialog query. Reversal audit is stored on the negative production-report row through a nullable backward-compatible column.

**Tech Stack:** .NET 10, FastEndpoints, MediatR/NetCorePal, EF Core, PostgreSQL migrations, Vue 3, TypeScript, TanStack Query, Vitest, pnpm.

## Global Constraints

- Read and write every business record with organization and environment scope.
- Use FastEndpoints registries and the facade coverage matrix for new or changed HTTP endpoints.
- Do not hand-edit OpenAPI snapshots or generated client code.
- Gateway public reversal requests must not accept actor identity from callers.
- Follow red-green-refactor and retain the existing `skills-lock.json` modification untouched.

---

### Task 1: MES Production Report Detail

**Files:**
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Queries/Production/MesProductionQueries.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- Modify: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MesEndpointContractTests.cs`

**Interfaces:**
- Produces: `GetProductionReportQuery` and `GET /api/business/v1/mes/production-reports/{reportNo}` with operation ID `getBusinessMesProductionReport`.
- Response includes the existing report fact and `ConsumedMaterialLots` entries with material ID, material-lot ID, consumed quantity, UOM code, and material-issue request number.

- [ ] Add registry, projection, tenant-isolation, missing-record, and multi-lot tests.
- [ ] Run the focused MES tests and confirm they fail for the missing detail capability.
- [ ] Implement the query, response records, endpoint, and endpoint-registry entry.
- [ ] Run the focused MES tests and confirm they pass.
- [ ] Review the diff for list-query regressions and provider-specific database usage.

### Task 2: Gateway Detail Facade and Client Contract

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `docs/architecture/facade-coverage-matrix.json`

**Interfaces:**
- Consumes: MES detail endpoint from Task 1.
- Produces: `GET /api/business-console/v1/mes/production-reports/{reportNo}` with operation ID `getBusinessConsoleMesProductionReport` and `MesReportingRead` authorization.

- [ ] Add failing authorization, proxy-forwarding, response-content, OpenAPI, and facade-coverage tests.
- [ ] Run focused Gateway tests and confirm the expected failures.
- [ ] Implement models, client method, facade endpoint, and `exposed` facade-matrix row.
- [ ] Run focused Gateway and facade-coverage tests and confirm they pass.
- [ ] Review path escaping, query scoping, and response fidelity.

### Task 3: Principal-Derived Reversal Audit

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Commands/Production/MesProductionCommands.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Endpoints/Mes/MesEndpoints.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Domain/AggregatesModel/ProductionReportAggregate/ProductionReport.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/EntityConfigurations/ProductionReportEntityTypeConfiguration.cs`
- Modify: MES reversal, persistence, endpoint-contract, and schema-convention tests that construct reversal commands.
- Create: governed MES EF migration adding `reversed_by`.
- Modify: `docs/architecture/database-schema-catalog.md`

**Interfaces:**
- Public Gateway request omits actor identity.
- Gateway injects authenticated `ActorRef`; MES command requires it; reversal report persists it as `ReversedBy`.

- [ ] Add failing spoof-resistance, actor-validation, idempotency, persistence, schema, and migration tests.
- [ ] Run focused tests and confirm failures are caused by the missing actor audit path.
- [ ] Implement principal injection, dedicated downstream request, MES validation/fingerprint propagation, domain persistence, EF mapping, migration, and catalog update.
- [ ] Update existing command call sites with explicit test actors.
- [ ] Run focused Gateway and MES tests and confirm they pass.
- [ ] Review historical-row compatibility and confirm the public OpenAPI has no writable actor field.

### Task 4: Governed Contract Export and Frontend Dialog

**Files:**
- Generate: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Generate: `frontend/packages/api-client/src/generated/business-console/*`
- Modify: `frontend/packages/api-client/src/business-console.ts`
- Modify: `frontend/apps/business-console/src/composables/useBusinessMes.ts`
- Modify: `frontend/apps/business-console/src/composables/useBusinessMes.test.ts`
- Modify: `frontend/apps/business-console/src/pages/mes/production-reports.vue`
- Modify: `frontend/apps/business-console/src/pages/mes/production-reports.test.ts`
- Modify: relevant product documentation describing production-report reversal.

**Interfaces:**
- Consumes: Gateway detail operation from Task 2.
- Produces: stable client exports and an on-demand detail query used by the reversal dialog.

- [ ] Add failing client-export and dialog tests for open-time loading, multi-lot rendering, empty state, load failure lockout, and stale-data prevention.
- [ ] Run focused frontend tests and confirm expected failures.
- [ ] Export Gateway OpenAPI through the governed command and run `pnpm -C frontend generate:api`.
- [ ] Add stable exports, composable query, dialog states, and consumed-lot presentation using NvUI boundaries.
- [ ] Run focused tests, business-console typecheck, and business-console build.
- [ ] Perform the required post-implementation UX/IA self-check and update product documentation.

### Task 5: Integration Review, Verification, and PR

**Files:**
- Review all files changed by Tasks 1-4.
- Modify only files required to correct review findings.

**Interfaces:**
- Produces one reviewable PR closing #881 and #882.

- [ ] Run a specification-compliance review for both issues.
- [ ] Run a code-quality review and address verified findings.
- [ ] Run targeted MES, BusinessGateway, facade coverage, frontend tests, typecheck, and build.
- [ ] Run the broader required backend/frontend verification and distinguish baseline failures from regressions.
- [ ] Confirm generated artifacts are cleanly reproducible and `skills-lock.json` remains excluded.
- [ ] Commit intentional changes, push the branch, and create one PR with `Fix / Tests / Risk / OpenAPI or schema impact`, endpoint facade declarations, docs impact, and `Fixes #881` / `Fixes #882`.
