# NvUI Cascade Layer Bootstrap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure every NvUI host establishes the global cascade-layer order before Vue or component-library styles load.

**Architecture:** Keep the existing ADR 0020 host-owned `main.css` contract. Extend the UI design-system contract to guard JavaScript import order, then move the existing stylesheet import to the first line of every host entry.

**Tech Stack:** Vue 3.5, TypeScript, Vite+, Vitest, Tailwind CSS 4, Playwright CLI.

## Global Constraints

- Do not modify shadcn original files.
- Preserve the layer order `theme, nv-tokens, base, components, nv-components, utilities, nv-overrides, app`.
- Do not change component APIs, OpenAPI/generated files, backend code, or data contracts.
- Every production edit must follow a witnessed red-green TDD cycle.

---

### Task 1: Guard host bootstrap order

**Files:**
- Modify: `frontend/packages/ui/src/design-system.contract.test.ts`

**Interfaces:**
- Consumes: the five host `src/main.ts` files and their existing `src/assets/main.css` files.
- Produces: a contract that requires `import './assets/main.css'` to be the first import in every host entry.

- [x] **Step 1: Write the failing test**

Add an `appEntry(app)` reader and assert the first import statement equals `import './assets/main.css'` for `business-console`, `business-pda`, `console`, `design-system`, and `screen`.

- [x] **Step 2: Run test to verify it fails**

Run: `pnpm -C frontend --filter @nerv-iip/ui test -- src/design-system.contract.test.ts`

Expected: FAIL for all five hosts because their stylesheet import currently follows Vue/UI imports.

- [x] **Step 3: Keep the test unchanged for implementation**

The test must inspect source order rather than mock Vite or snapshot generated CSS.

### Task 2: Establish layer order before component evaluation

**Files:**
- Modify: `frontend/apps/business-console/src/main.ts`
- Modify: `frontend/apps/business-pda/src/main.ts`
- Modify: `frontend/apps/console/src/main.ts`
- Modify: `frontend/apps/design-system/src/main.ts`
- Modify: `frontend/apps/screen/src/main.ts`

**Interfaces:**
- Consumes: each app's unchanged `src/assets/main.css` layer declaration.
- Produces: deterministic host bootstrap order with no runtime API changes.

- [x] **Step 1: Implement the minimal fix**

Move this exact statement to line 1 in each file:

```ts
import './assets/main.css'
```

Remove its former occurrence; make no other import or application changes.

- [x] **Step 2: Run the targeted contract test**

Run: `pnpm -C frontend --filter @nerv-iip/ui test -- src/design-system.contract.test.ts`

Expected: PASS.

- [x] **Step 3: Run touched-file formatting checks**

Run `pnpm -C frontend exec vp fmt --check` for the contract test and all five `main.ts` files.

Expected: exit 0 without rewriting the first import.

### Task 3: Verify runtime and package safety

**Files:**
- Create: `frontend/packages/ui/DESIGN/audit.md`

**Interfaces:**
- Consumes: source audit, contract results, and browser computed styles.
- Produces: reviewable cross-surface evidence and final verification results.

- [x] **Step 1: Run package and app gates**

Run UI and UI Mobile typecheck/tests, Screen typecheck/test/build, and the relevant NvUI contract commands.

- [x] **Step 2: Run real-browser checks**

At 1920×1080, verify Screen panel padding/title margin/tag padding are non-zero. Check one PC and one Mobile host component for non-zero component spacing, and capture affected Screen screenshots plus chart/canvas non-empty evidence.

- [ ] **Step 3: Review the diff**

Compare against `origin/main`, confirm no business behavior, backend, OpenAPI/generated, shadcn original, or MAN-517 files changed, then request an independent code review.

- [ ] **Step 4: Commit and update the existing PR**

Commit with a `MAN-465 #819` message, push the existing branch, update PR #975 body with Fix/Tests/Risk/schema/docs impact, and stop without merging.
