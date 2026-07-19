# MAN-447 / #801 Inspection Task Follow-up Implementation Plan

> **For Codex:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Close the remaining factual acceptance gap for the WMS quality inspection task flow and remove the two reviewed UI nits without changing service contracts or neighboring domain pages.

**Architecture:** Keep the existing BusinessGateway facade and generated client unchanged. The inspection record page remains the task-submit orchestrator, the inspection-task page only selects the correct table pagination mode, and the existing quality composable remains the server-state/query boundary. Real acceptance uses the governed full-stack session and backend facts; no optimistic or mocked state counts as evidence.

**Tech Stack:** Vue 3 Composition API, Pinia, TanStack Vue Query, Vitest/Vue Test Utils, NvUI, Playwright, Nerv-IIP managed full-stack scripts.

---

### Task 1: Guard task submission until business context is ready

**Files:**

- Modify: `frontend/apps/business-console/src/pages/quality/inspections.vue`
- Test: `frontend/apps/business-console/src/pages/quality/quality-location.test.ts`

1. Add a failing test proving task-flow submission is disabled while organization/environment context is empty and becomes enabled after context arrives.
2. Run the targeted test and confirm the intended failure.
3. Add an explicit `hasBusinessContext` guard at the submission boundary and include it in the computed eligibility.
4. Re-run the targeted test and confirm it passes.

### Task 2: Remove meaningless manual pagination in locator mode

**Files:**

- Modify: `frontend/apps/business-console/src/pages/quality/inspection-tasks.vue`
- Test: `frontend/apps/business-console/src/pages/quality/inspectionTasksPage.test.ts`

1. Add failing assertions that normal listing uses server/manual pagination while exact locator mode uses client pagination over the already-scanned result set.
2. Run the targeted test and confirm the intended failure.
3. Derive locator mode from the stable route fields and bind `NvDataTable.manual` accordingly.
4. Re-run the targeted test and confirm it passes.

### Task 3: Verify the frontend change

1. Run the focused quality tests.
2. Run business-console typecheck, test, and build.
3. Run `vp fmt --check` for every touched frontend/documentation file that the formatter supports.
4. Inspect the final diff for generated code, OpenAPI, navigation, or out-of-scope changes; none are expected.

### Task 4: Produce real end-to-end acceptance evidence

1. Run `nerv.ps1 fullstack run -Scenario smoke` as the governed environment preflight.
2. Start a governed diagnostic full-stack session only if needed for the custom flow.
3. Create/receive a real WMS inbound order, observe its quality task, execute the real inspection, and verify task closure from refreshed server state.
4. Verify an overdue task is visibly distinguishable within three seconds and capture realistic screenshots/log identifiers.
5. Stop the exact diagnostic session before handoff. If infrastructure genuinely prevents the flow after scoped diagnosis, reopen #801 with the exact blocker and “待真机验收”.

### Task 5: Independent review and delivery

1. Spawn an independent code-review subagent after implementation and verification.
2. Resolve every factual finding, rerun affected checks, and request a final re-review until no findings remain.
3. Update #801 with factual acceptance evidence (or reopen it if blocked).
4. Push the branch and create a ready PR scoped to MAN-447/#801; do not merge it.
