# Maintenance Plan Trigger Edit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver issue #945 as one end-to-end, governed edit flow for maintenance-plan trigger configuration and close the remaining edit gap in #794.

**Architecture:** BusinessMaintenance owns trigger invariants and cursor recalculation; a tenant-scoped PUT command exposes the aggregate method. BusinessGateway provides the authenticated `plans.manage` facade, generated client artifacts carry the contract to Business Console, and a shared Vue dialog supports create/edit without widening update scope beyond trigger configuration.

**Tech Stack:** .NET 10, CleanDDD, EF Core, FastEndpoints, FluentValidation, BusinessGateway, OpenAPI/Hey API, Vue 3 Composition API, Pinia Colada, NvUI, Vitest, Vue Test Utils, pnpm 11.13.1.

## Global Constraints

- Read `docs/architecture/implementation-readiness.md` before implementation.
- Keep Connector Host, platform control-plane, and business-service boundaries unchanged.
- Use FastEndpoints registries and existing authorization policies; do not add Minimal API mappings.
- Classify the new business endpoint as `exposed` and deliver both facade and generated client in the same PR.
- Do not hand-edit OpenAPI snapshots or generated client files.
- Use test-first RED/GREEN cycles for every behavior change.
- Preserve the user's existing `skills-lock.json` modification.

---

### Task 1: Domain trigger-update semantics

**Files:**
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/MaintenanceAggregateTests.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`

**Interfaces:**
- Produces: `void MaintenancePlan.UpdateTriggerConfiguration(string? interval, decimal? runtimeHourInterval)`.
- Preserves: `LastGeneratedOn`, `LastGeneratedRuntimeHours`, and unchanged next-due cursors.

- [ ] Add failing aggregate tests for calendar/runtime removal, addition, changed intervals, unchanged normalized values, and atomic rejection of invalid configurations.
- [ ] Run the domain-test filter and confirm failures are caused by the missing update method.
- [ ] Implement normalization, complete pre-validation, change detection, and independent cursor recalculation in the aggregate.
- [ ] Re-run the domain tests and confirm all new cases pass.

### Task 2: Tenant-scoped BusinessMaintenance update command and endpoint

**Files:**
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointContractTests.cs`
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceCommandLockTests.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceCommands.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Program.cs`

**Interfaces:**
- Produces: `UpdateMaintenancePlanCommand(OrganizationId, EnvironmentId, PlanId, Interval, RuntimeHourInterval)`.
- Produces: `PUT /api/business/v1/maintenance/plans/{planId}` with operation ID `updateMaintenancePlan`.

- [ ] Add failing validator, handler tenant-scope, persistence/projection, lock-equivalence, and endpoint-registry tests.
- [ ] Run the focused Maintenance Web tests and confirm the new tests fail for missing types/contracts.
- [ ] Implement the command validator and async handler; query by plan ID plus organization/environment and throw `KnownException` when absent.
- [ ] Register a command lock using the existing Maintenance PM organization/environment key.
- [ ] Add the PUT request/response, endpoint class, registry row, manage permission, and internal-service policy.
- [ ] Re-run focused Maintenance Web tests and confirm the update flows pass.

### Task 3: BusinessGateway facade and facade-coverage declaration

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleMaintenanceModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Maintenance/BusinessConsoleMaintenanceEndpoints.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayMaintenanceTelemetryTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayAuthorizationTests.cs`
- Modify: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayOpenApiTests.cs`
- Modify: `docs/architecture/facade-coverage-matrix.json`
- Modify: `docs/architecture/facade-coverage-matrix.md`

**Interfaces:**
- Produces: `BusinessConsoleUpdateMaintenancePlanRequest` with organization, environment, nullable calendar interval, and nullable runtime interval.
- Produces: `IBusinessMaintenanceClient.UpdatePlanAsync(planId, request, cancellationToken)`.
- Produces: `PUT /api/business-console/v1/maintenance/plans/{planId}` with operation ID `updateBusinessConsoleMaintenancePlan`.

- [ ] Add failing proxy, client, authorization, OpenAPI-operation, validator, and facade-coverage tests for the PUT route.
- [ ] Run the focused Gateway and facade-coverage tests and confirm the intended RED failures.
- [ ] Implement the models, client forwarding, authorized facade endpoint, and validator with create-equivalent trigger rules.
- [ ] Register the service endpoint as `exposed`, update the generated summary counts, and point to the Gateway operation ID.
- [ ] Re-run focused Gateway and facade-coverage tests and confirm both hops are verified.

### Task 4: Governed OpenAPI export and generated API client

**Files:**
- Generated: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Generated: `frontend/packages/api-client/src/generated/business-console/**`
- Modify: `frontend/packages/api-client/src/business-console.ts`

**Interfaces:**
- Produces: `updateBusinessConsoleMaintenancePlanMutationOptions` and generated request/response types through the stable `@nerv-iip/api-client` barrel.

- [ ] Export the BusinessGateway OpenAPI snapshot with the governed script.
- [ ] Run `pnpm -C frontend generate:api` to regenerate the client.
- [ ] Add only stable barrel aliases/exports that the generator cannot own.
- [ ] Run the OpenAPI/client drift verification and the API-client type checks.

### Task 5: Shared Vue plan dialog and update mutation

**Files:**
- Create: `frontend/apps/business-console/src/components/maintenance/MaintenancePlanFormDialog.vue`
- Modify: `frontend/apps/business-console/src/pages/maintenance/plans.vue`
- Modify: `frontend/apps/business-console/src/composables/useBusinessMaintenance.ts`
- Modify: `frontend/apps/business-console/src/pages/maintenance/maintenance-pages.test.ts`
- Modify or create focused composable test beside `useBusinessMaintenance.ts` if existing coverage is not suitable.

**Interfaces:**
- Consumes: generated `BusinessConsoleUpdateMaintenancePlanRequest` and update mutation options.
- Produces: shared dialog emits typed create or update submissions; page opens it from create and row-edit actions.

- [ ] Add failing black-box tests for three-state edit prefill, read-only identity, runtime-to-calendar `null` clearing, calendar-to-runtime clearing, combined mode, submit-time field invalid state, success/failure lifecycle, toast, and scoped query refresh.
- [ ] Run only the affected Vitest files and confirm expected RED failures.
- [ ] Implement `useMaintenancePlans.updatePlan` with the generated mutation and awaited scoped refetch.
- [ ] Extract the shared dialog using Vue 3 `<script setup lang="ts">`, minimal reactive source state, typed props/emits, bare NvUI imports, and submit-time validation.
- [ ] Add an `NvRowActions` edit entry to `plans.vue`, preserve the generate dialog, and wire create/update to distinct mutations and human-readable feedback.
- [ ] Re-run affected Vitest files until GREEN, then run business-console typecheck.

### Task 6: Documentation and complete verification

**Files:**
- Modify: `docs/architecture/maintenance-module-product-design.md`
- Modify: `docs/architecture/implementation-readiness.md` only if current delivered-capability wording needs the #945 closeout recorded.

- [ ] Replace the #945 backend-gap note with delivered update/facade/edit-flow facts and explain that #794's edit gap is closed.
- [ ] Run touched-file formatting checks with `pnpm -C frontend exec vp fmt --check <file>`.
- [ ] Run focused Maintenance, Gateway, facade-coverage, and frontend tests again from a clean command invocation.
- [ ] Run `dotnet test backend/Nerv.IIP.sln`.
- [ ] Run `pnpm -C frontend typecheck`, `pnpm -C frontend test`, and `pnpm -C frontend build`.
- [ ] Run applicable OpenAPI drift and governed verification scripts.
- [ ] Review `git diff`, confirm `skills-lock.json` is excluded, and verify every #945 acceptance item against code and test evidence.
- [ ] Commit all issue changes, push `codex/issue-945-maintenance-plan-edit`, and create one PR with `Fixes #945` and `Refs #794`, including facade, docs, test, risk, OpenAPI, and schema-impact notes.
