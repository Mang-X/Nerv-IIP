# MAN-637 PDA MES Server Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the PDA current-operation journey consume the existing server-authoritative scope, strong IDs, gates, blockers and receipts end to end.

**Architecture:** Keep the route view as the route/query/action orchestration surface, move its detail/gate/result presentation into a page-private component, and keep the MES composable as the scope-bound data/write boundary. Extend the existing BusinessGateway operation-task list with an optional exact `operationTaskId` filter, export the Gateway OpenAPI, regenerate the client and preserve the reportable request as a separate type without that filter. The facade remains `exposed` through `listBusinessConsoleMesOperationTasks` and the stable api-client barrel.

**Tech Stack:** Vue 3 Composition API, TypeScript, Pinia Colada, Vitest/Vue Test Utils, Playwright, NvUI Mobile.

---

### Task 1: Lock the server-authoritative action contract with failing tests

**Files:**

- Modify: `frontend/apps/business-pda/src/composables/useBusinessMes.test.ts`
- Modify: `frontend/apps/business-pda/src/pages/mes/operation.test.ts`

- [x] Add a composable test that calls `startTask(workOrderId, operationTaskId, options)`, returns an exact row whose `status` would locally permit start but whose `allowedActions` is empty, and expects no mutation plus a lifecycle-refresh error.
- [x] Add a page test whose row status is `Queued` but `allowedActions` is empty and assert that no Start button renders.
- [x] Add page tests for server blocker/evaluation/device facts, a Completed row with empty actions, pair-bearing mutation calls/results, and accepted/unconfirmed not rendering success.
- [x] Run the two test files and verify they fail for missing server-driven behavior, not setup errors.

### Task 2: Implement the minimal composable and page behavior

**Files:**

- Modify: `frontend/apps/business-pda/src/composables/useBusinessMes.ts`
- Modify: `frontend/apps/business-pda/src/pages/mes/operation.vue`

- [x] Change operation actions to accept both strong IDs and pre-read the exact pair in the frozen selected scope.
- [x] Validate the requested action against normalized server `allowedActions`; permit only same-key idempotent replay as governed by the existing pending-intent mechanism.
- [x] Render only recognized server actions, readable blocker categories/details, `evaluatedAtUtc`, device and pair facts; keep empty actions read-only.
- [x] Preserve the selected pair in error/success result state, clear it on 409 refresh, and rely on `confirmBusinessConsoleOperation` before success.
- [x] Run the focused tests until green, then refactor labels/normalization without changing behavior.

### Task 3: Add the 375x812 browser journey

**Files:**

- Modify: `frontend/apps/business-pda/e2e/fixtures.ts`
- Modify: `frontend/apps/business-pda/e2e/mes.spec.ts`

- [x] Populate realistic server `allowedActions`, `blockReasons`, `evaluatedAtUtc`, device and display fields in fixtures.
- [x] Add a 375×812 test proving an allowed current task can execute and the POST/readback stay on the same pair.
- [x] Add a blocked task test proving predecessor/material/equipment/quality reasons are visible and Start is absent.
- [x] Add conflict/unconfirmed/terminal assertions while retaining the existing direct-route and history-switch coverage.
- [x] Run the MES Playwright spec and then the complete PDA e2e suite.

### Task 4: Synchronize documentation and verify all gates

**Files:**

- Modify: `docs/architecture/mobile-pda-module-product-design.md`
- Modify: `docs/architecture/mobile-pda-testing-and-smoke.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `frontend/apps/docs/docs/getting-started/planning-to-finished-goods.md`

- [x] Document the shipped server-authoritative PDA operation journey and its user-visible failure semantics.
- [x] Run PDA typecheck, test, build and e2e.
- [x] Run `pnpm -C frontend exec vp fmt --check <frontend-relative-file>` for every touched frontend file and the repository formatter for touched Markdown files where supported.
- [x] Record that this machine has no reusable MAN-637 managed AppHost scenario; do not substitute unmanaged startup, database seeding or HTTP 200 for public business evidence.
- [x] Review `git diff`, commit, push, create a ready PR to `main` with `Fixes #1174`, then verify base/head/draft state without merging.

### Task 5: Close exact-link and retry-context review gaps

**Files:**

- Modify: `backend/gateway/BusinessGateway/src/Nerv.IIP.BusinessGateway.Web/Endpoints/Mes/BusinessConsoleMesEndpoints.cs`
- Modify: `frontend/apps/business-pda/src/composables/useBusinessMes.ts`
- Modify: `frontend/apps/business-pda/src/pages/mes/operation.vue`
- Add: `frontend/apps/business-pda/src/pages/mes/components/MesOperationExecutionPanel.vue`
- Add: `frontend/apps/business-pda/src/pages/mes/components/operationPresentation.ts`

- [x] Export the optional exact `operationTaskId` facade query through OpenAPI/codegen/stable barrel, declare it `exposed`, and prove reportable isolation.
- [x] Send the exact filter from complete deep links through initial and paginated list requests; keep scan/restored keyword behavior separate.
- [x] Freeze principal/org/env/manage-scope/pair/operation type in the action context and reject retries before mutation when identity drifts.
- [x] Keep `operationTaskNo` distinct from `operationCode`; use an explicit unavailable task-instance label.
- [x] Extract detail/gate/result presentation into the route-excluded page-private component and protect the generated route table with a contract test.

### Task 6: Isolate asynchronous action results and remove raw predecessor IDs

**Files:**

- Modify: `frontend/apps/business-pda/src/composables/useBusinessMes.ts`
- Modify: `frontend/apps/business-pda/src/pages/mes/operation.vue`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Readiness/MesOperationTaskActionReadinessEvaluator.cs`
- Modify: `frontend/apps/business-pda/e2e/fixtures.ts`
- Modify: focused unit, service-contract and Playwright tests

- [x] Freeze a monotonic page generation plus route/principal/org-env/read/manage-scope identity for initial actions and retries; discard stale success/error and refresh the current context without showing “操作失败”.
- [x] Render predecessor blockers from authoritative `operationSequence` as “工序 N” and prove evaluator/service/PDA output omits current and predecessor raw IDs.
- [x] Keep every production-shape browser operation-task fixture at `operationTaskNo=null`; retain non-null coverage only in a clearly named unit test.
- [x] Capture TDD red for all three gaps, then turn the focused unit, backend and 375×812 browser tests green.
