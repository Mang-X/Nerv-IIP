# Frontend Business Console Component Readiness Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #143 as design-system readiness for business console pages, with `frontend/DESIGN` as the canonical spec.

**Architecture:** `frontend/packages/ui` owns primitives and wrappers. App pages import only from `@nerv-iip/ui`. FileUpload talks to FileStorage public upload-session/tus/download-grant contracts and never to MinIO directly.

**Tech Stack:** Vue 3, Tailwind CSS v4, shadcn-vue, Reka UI, lucide icons, optional Uppy core/headless + `@uppy/tus` for resumable uploads.

---

## Specification

Use `frontend/DESIGN/roadmaps/business-console-readiness.md` as the design-system contract. Do not treat this plan as the source of visual truth.

## Task 1: Update DESIGN Before Code

- [ ] **Step 1: Create or update component docs**

Add component docs before implementation:

1. `frontend/DESIGN/components/tabs.md`
2. `frontend/DESIGN/components/sheet.md`
3. `frontend/DESIGN/components/date-picker.md`
4. `frontend/DESIGN/components/chart.md`
5. `frontend/DESIGN/components/file-upload.md`
6. `frontend/DESIGN/components/progress.md`
7. `frontend/DESIGN/components/scroll-area.md`

- [ ] **Step 2: Update index and backlog**

Update `frontend/DESIGN/index.md` and `frontend/DESIGN/components/install-backlog.md` so future agents can see what is installed, what is still pending and which workflow owns each component.

## Task 2: Install And Export shadcn-vue Primitives

- [ ] **Step 1: Install primitives from `frontend/`**

Run:

```powershell
pnpm dlx shadcn-vue@latest add tabs sheet popover calendar range-calendar chart progress scroll-area
```

- [ ] **Step 2: Export public parts**

Update `frontend/packages/ui/src/index.ts` to export all public parts from the installed primitives.

- [ ] **Step 3: Add export contract tests**

Update `frontend/packages/ui/src/design-system.contract.test.ts` or add a focused export test so new primitives are covered by stable `@nerv-iip/ui` exports.

## Task 3: Build FileUpload Wrapper

- [ ] **Step 1: Add transport abstraction**

Create a FileUpload transport boundary that can create FileStorage upload sessions and then use either server-proxy or tus instructions.

- [ ] **Step 2: Add Uppy tus adapter when resumability is needed**

Prefer Uppy core/headless plus `@uppy/tus` behind the FileUpload wrapper. Do not expose Uppy Dashboard as the default rendered shell.

- [ ] **Step 3: Implement shadcn-styled UI**

Use existing `Button`, `Input`, `Progress`, `Alert`, `Badge`, `Empty`, `Spinner` and `Tooltip` primitives for the upload shell.

- [ ] **Step 4: Test public behavior**

Test:

1. Accepted and rejected file type/size.
2. Upload progress and retry state.
3. Completed output contains `fileId`.
4. Public state never exposes `objectKey` or direct object storage URL.

## Task 4: Build Chart And Date Wrappers Only Where Needed

- [ ] **Step 1: Chart primitive**

Export shadcn-vue chart primitives first. Add domain wrappers only after a repeated business page need appears, such as KPI mini-chart, production trend or stock movement trend.

- [ ] **Step 2: Date picker composition**

Compose Popover + Calendar/RangeCalendar into compact date/date-range controls suitable for toolbar filters and forms.

## Task 5: Verification

- [ ] **Step 1: Run frontend quality gates**

Run:

```powershell
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

Expected: commands pass for touched areas.

- [ ] **Step 2: Run focused visual checks when pages consume components**

When any app page consumes these primitives, verify desktop and mobile screenshots with Playwright and check for text overflow, overlapping controls and broken focus states.

