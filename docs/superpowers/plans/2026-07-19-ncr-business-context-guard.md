# NCR Business Context Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make NCR disposition and close commands unavailable without an organization/environment context, while preserving existing field validation and automatically recovering when context arrives.

**Architecture:** Keep `ncrs.vue` as the existing route-level composition surface and reuse the shared `hasBusinessContext(filters)` predicate as the single context truth. Derive button availability from reactive filters and re-check the same predicate at both command boundaries before field validation or facade calls. Extend the existing page test seam so the real component behavior is tested without changing generated clients, facade contracts, or NCR lifecycle logic.

**Tech Stack:** Vue 3 Composition API, TypeScript, Vue Test Utils, Vitest, existing NvUI and notification boundaries.

---

### Task 1: Prove the missing NCR command guards

**Files:**

- Modify: `frontend/apps/business-console/src/pages/quality/quality-location.test.ts`

- [ ] **Step 1: Extend the existing NCR mock seam**

Track `submitDisposition` and `closeNcr` spies, allow the mock filters to start with an empty organization/environment, and reset all new state in `beforeEach`.

- [ ] **Step 2: Write failing page behavior tests**

Mount the real NCR page, open its NCR, and assert:

- disposition and close availability are false when either business-context field is empty;
- direct command invocation reports `业务范围尚未就绪，请稍后重试。` and calls neither facade action;
- restoring both fields reactively makes both actions available;
- dropping context again before the close confirmation command executes is stopped by the command-entry guard;
- an otherwise invalid form does not emit the business-context error when context is present.

- [ ] **Step 3: Run the focused test and verify RED**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/business-console exec vitest run src/pages/quality/quality-location.test.ts --reporter=dot
```

Expected: the new assertions fail because `canSubmitDisposition`, `canCloseNcr`, `submitNcrDisposition`, and `submitCloseNcr` do not yet enforce business context.

### Task 2: Add the minimal shared-context guard

**Files:**

- Modify: `frontend/apps/business-console/src/pages/quality/ncrs.vue`

- [ ] **Step 1: Reuse shared boundaries**

Import `hasBusinessContext` from `@/composables/businessContextBinding` and `notifyError` from `@/utils/notify`.

- [ ] **Step 2: Make both action states reactive to context**

Add `hasBusinessContext(filters)` to `canSubmitDisposition` and `canCloseNcr` without changing their existing NCR/field predicates.

- [ ] **Step 3: Guard both command entries**

At the start of each command, check the business context, notify with `业务范围尚未就绪，请稍后重试。`, and return before body construction or facade action. Keep the existing `can*` return immediately after that guard so field validation remains unchanged and is not replaced by a generic toast.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Task 1 command. Expected: all tests pass with no warnings or unhandled errors.

### Task 3: Verify and deliver the isolated change

**Files:**

- Verify: `frontend/apps/business-console/src/pages/quality/ncrs.vue`
- Verify: `frontend/apps/business-console/src/pages/quality/quality-location.test.ts`
- Verify: `docs/superpowers/plans/2026-07-19-ncr-business-context-guard.md`

- [ ] **Step 1: Run the required frontend gates**

```powershell
pnpm -C frontend --filter @nerv-iip/business-console typecheck
pnpm -C frontend --filter @nerv-iip/business-console test
pnpm -C frontend --filter @nerv-iip/business-console build
```

Expected: all pass.

- [ ] **Step 2: Run touched-file and diff checks**

```powershell
pnpm -C frontend exec vp fmt --check apps/business-console/src/pages/quality/ncrs.vue apps/business-console/src/pages/quality/quality-location.test.ts
$content = ((Get-Content -Raw 'docs/superpowers/plans/2026-07-19-ncr-business-context-guard.md') -replace "`r`n", "`n").TrimEnd("`n")
$formatted = (((Get-Content -Raw 'docs/superpowers/plans/2026-07-19-ncr-business-context-guard.md' | pnpm -C frontend exec vp fmt --stdin-filepath=ncr-business-context-guard.md) -join "`n") -replace "`r`n", "`n").TrimEnd("`n")
if ($formatted -ne $content) { throw 'Plan markdown is not formatted' }
git diff --check
```

Expected: both pass.

- [ ] **Step 3: Perform independent review**

Dispatch an independent subagent to review the issue acceptance criteria, exact diff, tests, context-loss race, close-confirmation path, scope, and NvUI/generated boundaries. Apply factually valid findings and request a fresh re-review until there are no findings.

- [ ] **Step 4: Commit and create the ready PR**

Commit only the three scoped files, push the `codex/` branch, and create a ready GitHub PR whose title starts with `#980`. Include Fix, Tests, Risk, OpenAPI or schema impact, 产品文档影响, and `Fixes #980`; do not merge.
