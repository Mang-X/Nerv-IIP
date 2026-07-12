# Maintenance Actual Technician Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve planned technician assignment while recording and reporting the actual primary technician selected at work-order completion.

**Architecture:** Add nullable `ActualTechnicianUserId` to the Maintenance aggregate and completion contract, defaulting it to the planned assignment when completion omits it. Propagate the additive contract through BusinessGateway and generated API artifacts, then add the existing worker selector to the Business Console completion sheet.

**Tech Stack:** .NET 10, CleanDDD/NetCorePal, EF Core/PostgreSQL, FastEndpoints, Vue 3 TypeScript, NvUI, Vitest, pnpm 11.1.2.

## Global Constraints

- Keep `AssignedTechnicianUserId` as the planned assignment; never overwrite it during completion.
- Keep the existing completion endpoint route and `exposed` facade classification.
- Do not hand-edit OpenAPI snapshots or generated client files.
- Do not introduce a cross-service foreign key for technician references.
- App code imports only `Nv*` components through bare `@nerv-iip/ui` boundaries.
- Preserve unrelated changes in `skills-lock.json` and untracked files.

---

### Task 1: Domain completion semantics

**Files:**
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenanceWorkOrderAggregate/MaintenanceWorkOrder.cs`
- Test: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/MaintenanceAggregateTests.cs`

**Interfaces:**
- Produces: `MaintenanceWorkOrder.ActualTechnicianUserId` and optional `actualTechnicianUserId` argument on `Complete(...)`.

- [ ] Add failing tests proving an explicit actual technician differs from but does not replace assignment, and omission falls back to assignment.
- [ ] Run the focused domain tests and verify failure due to the missing property/signature.
- [ ] Add the nullable property and normalize `actualTechnicianUserId`, using `AssignedTechnicianUserId` as fallback.
- [ ] Re-run focused domain tests and commit the passing domain slice.

### Task 2: Persistence, command, endpoint, and reliability query

**Files:**
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceEntityTypeConfigurations.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceCommands.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/MaintenanceQueries.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/<timestamp>_AddMaintenanceActualTechnician.cs` and designer
- Test: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointContractTests.cs`
- Test: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSchemaConventionTests.cs`

**Interfaces:**
- Consumes: domain `ActualTechnicianUserId`.
- Produces: optional `ActualTechnicianUserId` on completion request/command and read models; reliability grouping key `ActualTechnicianUserId ?? AssignedTechnicianUserId`.

- [ ] Add failing validator, handler, endpoint mapping, query grouping, and schema metadata tests.
- [ ] Run focused Web tests and verify the intended failures.
- [ ] Implement command/request propagation and additive read DTO fields.
- [ ] Configure nullable `actual_technician_user_id` varchar(150) with a clear external-reference comment.
- [ ] Generate the EF migration using the explicit PostgreSQL profile and inspect it for only the intended column/comment changes.
- [ ] Run focused Maintenance tests and commit the passing backend service slice.

### Task 3: BusinessGateway contract propagation

**Files:**
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessConsoleMaintenanceModels.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Application/BusinessServices/BusinessServiceClients.cs`
- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Maintenance/BusinessConsoleMaintenanceEndpoints.cs`
- Test: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayMaintenanceTelemetryTests.cs`
- Test: `backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/BusinessGatewayProxyTests.cs`

**Interfaces:**
- Consumes: Maintenance completion/read/reliability contracts.
- Produces: Business Console completion request with `ActualTechnicianUserId` and work-order response fields retaining both technician meanings.

- [ ] Add failing proxy and validator tests for actual-technician propagation and maximum length.
- [ ] Run focused Gateway tests and verify failures.
- [ ] Implement request, downstream transport, response, and validator changes.
- [ ] Run focused Gateway and facade coverage tests, then commit.

### Task 4: Governed schema docs and API generation

**Files:**
- Modify: `docs/architecture/database-schema-catalog.md`
- Generated: `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- Generated: `frontend/packages/api-client/src/generated/business-console/**`
- Generated/stable exports as required: `frontend/packages/api-client/src/business-console.ts`

**Interfaces:**
- Consumes: compiled Gateway OpenAPI contract.
- Produces: generated `actualTechnicianUserId?: string | null` client types.

- [ ] Update the Maintenance schema catalog entry for the nullable actual technician column.
- [ ] Run the repository-governed OpenAPI export command identified in `docs/architecture/api-contract-and-codegen.md`.
- [ ] Run `pnpm -C frontend generate:api` and verify generated types expose the new field without manual edits.
- [ ] Run API-client typecheck/contract tests and commit generated artifacts plus schema documentation.

### Task 5: Business Console completion selector

**Files:**
- Modify: `frontend/apps/business-console/src/pages/maintenance/work-orders.vue`
- Test: `frontend/apps/business-console/src/pages/maintenance/maintenance-pages.test.ts`

**Interfaces:**
- Consumes: worker lookup/selector and generated completion request type.
- Produces: completion payload field `actualTechnicianUserId`, defaulted from the selected work order's `assignedTechnicianUserId`.

- [ ] Add a failing component test that opens completion, observes assigned-technician default, changes the worker, and asserts the actual-technician payload.
- [ ] Run the focused Vitest file and verify failure.
- [ ] Add the NvUI worker selector and reset/default behavior without altering planned assignment display.
- [ ] Re-run the focused test, business-console typecheck, and commit.

### Task 6: Full verification and PR

**Files:**
- Modify only documentation required by verified contract/schema impact.

**Interfaces:**
- Produces: verified branch and PR closing #897.

- [ ] Run targeted Maintenance Domain/Web, BusinessGateway, facade coverage, migration/schema, frontend typecheck, frontend tests, and frontend build gates.
- [ ] Run `git diff --check`, inspect generated/migration diffs, and confirm unrelated files remain excluded.
- [ ] Update the design/plan checkboxes only if repository convention requires it; otherwise leave them as execution records.
- [ ] Commit final verification/doc adjustments, push the branch, and create a PR containing `Fix / Tests / Risk / OpenAPI or schema impact`, facade declaration, docs impact, and `Fixes #897`.
- [ ] Stop after PR creation and wait for review.
