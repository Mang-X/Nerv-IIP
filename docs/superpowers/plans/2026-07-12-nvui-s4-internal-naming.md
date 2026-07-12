# NvUI S4 Internal Naming Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close NvUI's transitional internal naming by moving the PC layer from `pro/` to `pc/`, renaming implementations and selectors, and introducing the canonical `NvPagination`.

**Architecture:** Preserve the bare package export boundary and component behavior while aligning source paths, filenames, local imports, slots, and CSS selectors with the frozen NvUI surface model. Contract tests define the forbidden transitional vocabulary before the mechanical migration begins.

**Tech Stack:** Vue 3 SFCs, TypeScript, Vitest, Vite+, pnpm 11.

## Global Constraints

- `frontend/packages/ui/src/components/ui/**` remains byte-for-byte unchanged.
- Keep `blocks/`, `layout/`, `screen/`, and `touch/` as surface/layer directories.
- Do not introduce deep package imports or generated-code edits.
- Preserve component props, emits, behavior, CSS values, and the bare `@nerv-iip/ui` entry point.
- Replace `NvDataTablePagination` with `NvPagination`; do not retain a compatibility alias.

---

### Task 1: Define S4 closure contracts

**Files:**
- Modify: `frontend/packages/ui/src/nvui-naming.contract.test.ts`
- Modify: `frontend/packages/ui/src/design-system.contract.test.ts`

**Interfaces:**
- Consumes: repository source tree and package barrel files.
- Produces: failing assertions for `components/pro`, `*Pro.vue`, transitional selectors/slots, and the obsolete pagination export.

- [ ] Add exact forbidden-pattern assertions and an explicit shadcn purity assertion.
- [ ] Run the focused contract tests and confirm they fail on the current transitional files.
- [ ] Keep the failure output as the RED evidence for the migration.

### Task 2: Move and rename the PC component layer

**Files:**
- Move: `frontend/packages/ui/src/components/pro/**` to `frontend/packages/ui/src/components/pc/**`
- Modify: `frontend/packages/ui/src/index.ts`
- Modify: local imports and tests under `frontend/packages/ui/src/**`

**Interfaces:**
- Consumes: canonical `Nv*` exports already exposed by the transitional barrels.
- Produces: the same exports backed by `components/pc/**` and canonical `Nv*.vue` implementation files.

- [ ] Rename every component implementation and test file to its canonical `Nv*` name.
- [ ] Rewrite local component identifiers/imports without changing props, emits, or behavior.
- [ ] Change the root barrel from `components/pro` to `components/pc`.
- [ ] Run package typecheck and focused component tests until the moved PC layer is green.

### Task 3: Correct the PC pagination boundary

**Files:**
- Move: `frontend/packages/ui/src/components/pc/data-table/DataTablePaginationPro.vue` to `frontend/packages/ui/src/components/pc/pagination/NvPagination.vue`
- Modify: `frontend/packages/ui/src/components/pc/data-table/NvDataTable.vue`
- Modify: application and documentation consumers of `NvDataTablePagination`

**Interfaces:**
- Produces: `NvPagination` with the existing pagination props/emits contract.
- Removes: `NvDataTablePagination`.

- [ ] Export `NvPagination` from the PC barrel.
- [ ] Compose it from `NvDataTable` and migrate all bare-package consumers.
- [ ] Run focused pagination and data-table tests.

### Task 4: Close slots and CSS selectors

**Files:**
- Modify: NvUI SFCs and CSS under `frontend/packages/ui/src/**`
- Modify: affected app/design-system CSS references under `frontend/apps/**`

**Interfaces:**
- Produces: `.nv-*` PC selectors, `.nv-scr-*` screen selectors, and `nv-*` slots.

- [ ] Replace live `.ds-*` definitions/references with `.nv-*` names.
- [ ] Replace live `.sb-*` definitions/references with `.nv-scr-*` names.
- [ ] Replace transitional NvUI `data-slot` values with `nv-*` values.
- [ ] Run focused contract tests and fix only factual missed references.

### Task 5: Synchronize documentation

**Files:**
- Modify: `docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md`
- Move/modify: `frontend/packages/ui/src/components/pc/MIGRATION.md`
- Modify: affected files under `frontend/DESIGN/**` and design-system docs.

**Interfaces:**
- Produces: current contributor guidance for `pc/` and `NvPagination`.

- [ ] Correct directory and pagination mappings without rewriting historical rationale.
- [ ] Remove live instructions that teach `pro`, `.ds-*`, or `.sb-*` identifiers.
- [ ] Check documentation references against the final diff.

### Task 6: Verify and publish

**Files:**
- Verify: all changed files.

**Interfaces:**
- Produces: a review-ready branch and PR closing #896.

- [ ] Run focused NvUI tests.
- [ ] Run `pnpm -C frontend typecheck`.
- [ ] Run `pnpm -C frontend test`.
- [ ] Run `pnpm -C frontend build` and the design-system documentation build.
- [ ] Run final forbidden-pattern scans and inspect `git diff`/`git status`.
- [ ] Commit only issue changes, push the branch, and create a PR with Fix, Tests, Risk, OpenAPI or schema impact, documentation impact, and `Fixes #896`.

