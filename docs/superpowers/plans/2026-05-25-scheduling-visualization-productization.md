# Scheduling Visualization Productization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Productize the mock-only scheduling visualization package with typed host interfaces, time axis, filtering, row virtualization, drag preview, and browser preview verification.

**Architecture:** Keep all scheduling visuals in `@nerv-iip/scheduling-visualization`. Charts accept immutable fixtures and preview maps, then emit typed selection and preview commands. `SchedulingWorkspace` remains a composition shell with mock fallback data and public integration events.

**Tech Stack:** Vue 3 SFCs, TypeScript, Vitest/Vue Test Utils, Vite, Leafer UI through local adapter, `@nerv-iip/ui`, lucide-vue-next.

---

## Component Map

| Unit | Responsibility |
| --- | --- |
| `time-scale/timelineLayout.ts` | Convert tasks/operations to DOM overlay positions and expose time-axis ticks. |
| `components/TimelineAxis.vue` | Render a compact sticky time-axis header from `TimeScaleTick[]`. |
| `state/filterFixtures.ts` | Pure filtering helpers for Gantt and schedule fixtures. |
| `components/GanttChart.vue` | Render filtered/virtualized rows, axis, Leafer scene, task overlays, and emit preview commands. |
| `components/ScheduleChart.vue` | Render filtered/virtualized resources, axis, Leafer scene, operation overlays, and emit preview commands. |
| `components/SchedulingToolbar.vue` | Add query input and commit/reset preview actions. |
| `components/SchedulingWorkspace.vue` | Re-emit public host integration events and apply preview commands locally. |
| `preview/` and `index.html` | Package-local browser preview only. |

## Tasks

### Task 1: Tests For Public Productization Behavior

- [x] Add failing tests for time axis rendering in `GanttChart` and `ScheduleChart`.
- [x] Add failing tests for search filtering and visible row virtualization helpers.
- [x] Add failing tests for chart drag preview events.
- [x] Add failing tests for workspace event re-emission and toolbar search.

### Task 2: Pure Layout And Filtering Helpers

- [x] Implement `time-scale/timelineLayout.ts`.
- [x] Implement `state/filterFixtures.ts`.
- [x] Export helper types from `src/index.ts`.
- [x] Run targeted helper tests.

### Task 3: Chart Components

- [x] Add `TimelineAxis.vue`.
- [x] Update `GanttChart.vue` with query, virtual row range, task overlays, and drag preview emit.
- [x] Update `ScheduleChart.vue` with query, virtual row range, operation overlays, and drag preview emit.
- [x] Run chart component tests.

### Task 4: Workspace And Toolbar Interfaces

- [x] Update `SchedulingToolbar.vue` with query input, commit preview, and reset preview controls.
- [x] Update `SchedulingWorkspace.vue` with public props/events and local command stack application.
- [x] Run workspace tests.

### Task 5: Package Preview And Docs

- [x] Add package-local Vite preview files.
- [x] Add `dev` script to the package.
- [x] Update Design System and architecture docs to describe the new public integration interface.

### Task 6: Verification And Browser Check

- [x] Run package test/typecheck.
- [x] Run frontend typecheck/test/build.
- [x] Start the package preview dev server.
- [x] Use a browser automation tool to inspect desktop and mobile viewports, switch modes, search, select, and drag-preview a bar.
- [x] Commit and push changes.
