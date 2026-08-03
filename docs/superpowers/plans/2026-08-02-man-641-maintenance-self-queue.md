# MAN-641 Maintenance Self Queue Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a PDA maintenance-technician self queue and authoritative read-only work-order detail for GitHub #1178.

**Architecture:** Add a dedicated principal-bound maintenance read composable instead of changing the repair page's organization-level recent list. Compose focused list filters/list rows and a read-only detail card from thin file-based routes; every list/detail request carries `scopeKind=self` and the authenticated principal ID, and route-bound response guards suppress stale or mismatched payloads.

**Tech Stack:** Vue 3 Composition API, TypeScript, Pinia Colada, generated `@nerv-iip/api-client`, NvUI Mobile, Vitest/Vue Test Utils, Playwright mock E2E.

---

### Task 1: Principal-bound maintenance read model

**Files:**
- Create: `frontend/apps/business-pda/src/composables/useMaintenanceSelfWorkOrders.ts`
- Test: `frontend/apps/business-pda/src/composables/useMaintenanceSelfWorkOrders.test.ts`

- [x] **Step 1: Write failing tests for self scope, filters, pagination, exact detail, no-scope suppression, and stale response rejection.**

  Assert generated query calls contain:

  ```ts
  expect(query).toMatchObject({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    scopeKind: 'self',
    scopeId: 'principal-1',
    status: 'accepted',
    deviceAssetId: 'device-1',
    keyword: '主轴',
    skip: 0,
    take: 20,
  })
  ```

- [x] **Step 2: Run the focused Vitest file and verify RED because the composable does not exist.**

  Run: `pnpm -C frontend --filter @nerv-iip/business-pda exec vitest run src/composables/useMaintenanceSelfWorkOrders.test.ts`

- [x] **Step 3: Implement the minimal composable.**

  Keep `organizationId`, `environmentId`, and `principalId` as the only scope source. Enable requests only when scope and read permission are present. Use `useTaskListPagination` for 20-row pages, and accept detail only when `data.workOrderId === requestedWorkOrderId`.

- [x] **Step 4: Re-run the focused test and verify GREEN.**

### Task 2: Self queue user interface

**Files:**
- Create: `frontend/apps/business-pda/src/components/maintenance/MaintenanceWorkOrderFilters.vue`
- Create: `frontend/apps/business-pda/src/components/maintenance/MaintenanceWorkOrderList.vue`
- Create: `frontend/apps/business-pda/src/pages/equipment/work-orders/index.vue`
- Test: `frontend/apps/business-pda/src/pages/equipment/work-orders/index.test.ts`

- [x] **Step 1: Write a failing page test.**

  Verify scope-not-ready does not claim a personal queue, status/device/keyword changes call the composable contracts, and selecting a row pushes the strong-ID route.

- [x] **Step 2: Run the page test and verify RED because the route/components do not exist.**

- [x] **Step 3: Implement a thin route that composes filters and TaskListShell-backed rows.**

  The visible title is `维修工单`; scope metadata says `当前维修人员（服务端 Self 范围）` only after a successful authoritative response. Device selection reuses `DeviceAssetPicker` and sends its stable `deviceAssetId` to the server filter.

- [x] **Step 4: Re-run the page test and verify GREEN.**

### Task 3: Authoritative read-only work-order detail

**Files:**
- Create: `frontend/apps/business-pda/src/components/maintenance/MaintenanceWorkOrderDetail.vue`
- Create: `frontend/apps/business-pda/src/components/maintenance/maintenanceWorkOrderPresentation.ts`
- Create: `frontend/apps/business-pda/src/pages/equipment/work-orders/[workOrderId].vue`
- Test: `frontend/apps/business-pda/src/pages/equipment/work-orders/[workOrderId].test.ts`

- [x] **Step 1: Write failing tests for all required fields, terminal read-only state, forbidden/invalid ID, and late old-route response.**

  The detail test must assert that no lifecycle write button exists even when the server returns `allowedActions`, while the returned actions and block reasons remain visible as read-only facts.

- [x] **Step 2: Run the detail test and verify RED because the route/components do not exist.**

- [x] **Step 3: Implement the exact-ID detail and exact device metadata readback.**

  Render device, location, priority, assignment, version, server `allowedActions`, localized `blockReasons`, and lifecycle. Terminal statuses and `terminal-status` show a read-only notice. Never infer an executable action.

- [x] **Step 4: Re-run the detail test and verify GREEN.**

### Task 4: Permission-trimmed entry and browser acceptance

**Files:**
- Modify: `frontend/apps/business-pda/src/composables/useWorkbenchHome.ts`
- Modify: `frontend/apps/business-pda/src/pages/tasks.vue`
- Modify: `frontend/apps/business-pda/src/pages/tasks.test.ts`
- Modify: `frontend/apps/business-pda/e2e/fixtures.ts`
- Modify: `frontend/apps/business-pda/e2e/equipment.spec.ts`
- Modify: `docs/architecture/mobile-pda-module-product-design.md`

- [x] **Step 1: Write failing entry and Playwright scenarios.**

  Cover list-to-detail, missing principal scope, and terminal read-only detail. Capture list requests and assert `scopeKind=self`, current `scopeId`, and server filter/pagination query parameters.

- [x] **Step 2: Run focused Vitest and Playwright and verify RED.**

- [x] **Step 3: Add the permission-trimmed `维修工单` entry, route mocks, and product documentation.**

- [x] **Step 4: Re-run focused checks and verify GREEN.**

### Task 5: Completion gates and ready PR

**Files:**
- Verify every touched file and the complete branch diff.

- [x] **Step 1: Run per-file `vp fmt --check`.**
- [x] **Step 2: Run PDA `typecheck`, `test`, and `build`.**
- [x] **Step 3: Run the focused mock Playwright equipment spec.**
- [x] **Step 4: Inspect `git diff --check`, status, and branch diff against `origin/main`.**
- [x] **Step 5: Commit, push `codex/man-641-pda-maintenance-self-queue`, and create a ready PR whose body contains `Fixes #1178`, exact tests, and documentation impact.**
- [x] **Step 6: Verify the PR is non-draft, open, targets `main`, and its head SHA equals local HEAD; do not merge.**

### Task 6: Review hardening for public IDs, alarm provenance, and retry recovery

- [x] Normalize canonical work-order/device GUIDs to lowercase at route, request, and response boundaries; keep business codes case-sensitive.
- [x] Require MasterData device detail `active` and nonblank `snapshotVersion` facts before rendering authoritative detail.
- [x] Require matching nonempty canonical GUIDs in create payload `workOrderId` and `operationReceipt.resourceId`; malformed matching strings fail closed.
- [x] Re-read a unique alarm in the current organization/environment and resolve both alarm/request device references to the same MasterData `DeviceAssetId` before an alarm-sourced create. This changes no public endpoint shape and leaves the facade classification `exposed` unchanged.
- [x] Clear stale alarm provenance after explicit device replacement, while retaining the Gateway as the final association gate.
- [x] Retry exact principal/assignment identity enrichment together with authoritative list/detail refreshes, and expose an actionable detail identity refresh.
- [x] Make Playwright list mocks apply Maintenance status/device/keyword filters before total and slicing; cover uppercase GUIDs and malformed create receipts.
