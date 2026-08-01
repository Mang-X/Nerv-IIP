# MAN-637 PDA MES Server Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the PDA current-operation journey consume the existing server-authoritative scope, strong IDs, gates, blockers and receipts end to end.

**Architecture:** Keep the route view as the presentation/orchestration surface and the MES composable as the scope-bound data/write boundary. Reuse the existing BusinessGateway contract without editing OpenAPI or generated code.

**Tech Stack:** Vue 3 Composition API, TypeScript, Pinia Colada, Vitest/Vue Test Utils, Playwright, NvUI Mobile.

---

### Task 1: Lock the server-authoritative action contract with failing tests

**Files:**
- Modify: `frontend/apps/business-pda/src/composables/useBusinessMes.test.ts`
- Modify: `frontend/apps/business-pda/src/pages/mes/operation.test.ts`

- [ ] Add a composable test that calls `startTask(workOrderId, operationTaskId, options)`, returns an exact row whose `status` would locally permit start but whose `allowedActions` is empty, and expects no mutation plus a lifecycle-refresh error.
- [ ] Add a page test whose row status is `Queued` but `allowedActions` is empty and assert that no Start button renders.
- [ ] Add page tests for server blocker/evaluation/device facts, a Completed row with empty actions, pair-bearing mutation calls/results, and accepted/unconfirmed not rendering success.
- [ ] Run the two test files and verify they fail for missing server-driven behavior, not setup errors.

### Task 2: Implement the minimal composable and page behavior

**Files:**
- Modify: `frontend/apps/business-pda/src/composables/useBusinessMes.ts`
- Modify: `frontend/apps/business-pda/src/pages/mes/operation.vue`

- [ ] Change operation actions to accept both strong IDs and pre-read the exact pair in the frozen selected scope.
- [ ] Validate the requested action against normalized server `allowedActions`; permit only same-key idempotent replay as governed by the existing pending-intent mechanism.
- [ ] Render only recognized server actions, readable blocker categories/details, `evaluatedAtUtc`, device and pair facts; keep empty actions read-only.
- [ ] Preserve the selected pair in error/success result state, clear it on 409 refresh, and rely on `confirmBusinessConsoleOperation` before success.
- [ ] Run the focused tests until green, then refactor labels/normalization without changing behavior.

### Task 3: Add the 375x812 browser journey

**Files:**
- Modify: `frontend/apps/business-pda/e2e/fixtures.ts`
- Modify: `frontend/apps/business-pda/e2e/mes.spec.ts`

- [ ] Populate realistic server `allowedActions`, `blockReasons`, `evaluatedAtUtc`, device and display fields in fixtures.
- [ ] Add a 375×812 test proving an allowed current task can execute and the POST/readback stay on the same pair.
- [ ] Add a blocked task test proving predecessor/material/equipment/quality reasons are visible and Start is absent.
- [ ] Add conflict/unconfirmed/terminal assertions while retaining the existing direct-route and history-switch coverage.
- [ ] Run the MES Playwright spec and then the complete PDA e2e suite.

### Task 4: Synchronize documentation and verify all gates

**Files:**
- Modify: `docs/architecture/mobile-pda-module-product-design.md`
- Modify: `docs/architecture/mobile-pda-testing-and-smoke.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `frontend/apps/docs/docs/getting-started/planning-to-finished-goods.md`

- [ ] Document the shipped server-authoritative PDA operation journey and its user-visible failure semantics.
- [ ] Run PDA typecheck, test, build and e2e.
- [ ] Run `pnpm -C frontend exec vp fmt --check <frontend-relative-file>` for every touched frontend file and the repository formatter for touched Markdown files where supported.
- [ ] If the machine can start a managed full stack, run only `./nerv.ps1 fullstack run` with the relevant scenario; otherwise record the exact environmental limitation.
- [ ] Review `git diff`, commit, push, create a ready PR to `main` with `Fixes #1174`, then verify base/head/draft state without merging.
