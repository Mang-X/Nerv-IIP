# Scheduling Dependency Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve Gantt and schedule dependency routing so same-row tasks connect directly when separated, close or overlapping tasks use a short top/bottom bridge, and existing frozen-column protection remains intact.

**Architecture:** Keep routing as a pure TypeScript function in `frontend/packages/scheduling-visualization/src/renderers/dependencyRouting.ts`. Gantt and schedule scene builders continue to call the same function, so one behavior change covers both component modes.

**Tech Stack:** TypeScript, Vitest, Vue package preview, Leafer canvas scene rendering.

---

### Task 1: Route Shape Tests

**Files:**
- Modify: `frontend/packages/scheduling-visualization/src/tests/dependencyRouting.test.ts`
- Modify: `frontend/packages/scheduling-visualization/src/renderers/dependencyRouting.ts`
- Optional docs: `frontend/DESIGN/components/scheduling-visualization.md`

- [x] **Step 1: Write failing route tests**

Add tests for direct same-row finish-start links, same-row tight bridge links, cross-row tight bridge links, and frozen-column clamping.

- [x] **Step 2: Run the route test**

Run: `pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/dependencyRouting.test.ts`

Expected before implementation: at least the new direct/bridge assertions fail against the current router.

- [x] **Step 3: Implement the router**

Update `dependencyRouting.ts` to choose:
- direct side-center line for same-row forward tasks with enough gap,
- top or bottom center bridge for same-row close/overlapping tasks,
- row-gap center bridge for cross-row close tasks,
- existing external-lane fallback for other dependency types and backward/overlap cases.

- [x] **Step 4: Verify package behavior**

Run:
`pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck`
`pnpm -C frontend --filter @nerv-iip/scheduling-visualization test`

- [x] **Step 5: Browser verify**

Use the package preview at `http://127.0.0.1:5120` and inspect base Gantt after selecting/drags. Confirm same-row/cross-row links render without side-column intrusion or canvas residue.
