# Scheduling Visualization Components Design

## Goal

Build a reusable frontend package for mock-only Gantt and scheduling visualization components using Leafer UI for canvas rendering and the existing Nerv-IIP shadcn-vue design system for controls, panels and page composition.

This work implements the first executable slice after `docs/architecture/gantt-scheduling-visualization-rfc.md`: a frontend-only component package and Console demo. It does not connect to real APS, MES, DemandPlanning, WMS, ERP or PlatformGateway APIs.

## Selected Approach

Create a new workspace package named `@nerv-iip/scheduling-visualization` under `frontend/packages/scheduling-visualization`.

The package owns all visualization models, mock fixtures, time-scale math, command preview state, Leafer adapter code and Vue components. The Console app consumes the package through stable exports only, in the same way it consumes `@nerv-iip/ui`, `@nerv-iip/app-shell` and `@nerv-iip/api-client`.

This keeps the first implementation reusable and avoids placing a complex scheduling product surface directly inside `frontend/apps/console`.

## Scope

### Included

1. A reusable Gantt chart component for task hierarchy, milestones, baseline bars, progress, dependencies, conflicts, today line, selection, zoom, pan and expand/collapse.
2. A reusable schedule chart component for resources/work centers, operation bars, capacity bands, load histogram, locked operations, overload/conflict markers, selection and zoom.
3. Local-only interaction preview for moving and resizing tasks or operations, including undo/redo and reset.
4. Mock data fixtures with at least two scenarios: a mixed project Gantt scenario and a manufacturing schedule scenario.
5. A protected Console demo page that renders both components with fixture switching and a detail sheet for the selected item.
6. Package-level tests for time-scale math, row flattening, command undo/redo and renderer scene generation.
7. Component tests for `GanttChart`, `ScheduleChart` and the Console demo page.
8. Design System updates after component completion, including DESIGN component/pattern documentation and roadmap status.

### Excluded

1. No backend endpoints, OpenAPI changes or generated API-client changes.
2. No APS optimization, finite-capacity scheduling engine or domain rules in the frontend.
3. No persisted changes; drag/resize operations remain preview-only.
4. No object storage, PDF, Excel or image export in this slice.
5. No production performance guarantee for 10,000 rows. The first target is responsive rendering for the included fixtures and deterministic scene generation for tests.

## Package Structure

```text
frontend/packages/scheduling-visualization/
  package.json
  tsconfig.json
  src/
    index.ts
    components/
      GanttChart.vue
      ScheduleChart.vue
      SchedulingToolbar.vue
      SchedulingDetailSheet.vue
    canvas/
      createLeaferSurface.ts
      leaferTypes.ts
      sceneTypes.ts
    renderers/
      buildGanttScene.ts
      buildScheduleScene.ts
      renderSceneToLeafer.ts
    model/
      gantt.ts
      schedule.ts
      fixtures.ts
    state/
      useSchedulingCommands.ts
      useSchedulingSelection.ts
    time-scale/
      timeScale.ts
      visibleRange.ts
    tests/
      timeScale.test.ts
      ganttRows.test.ts
      scheduleRows.test.ts
      commands.test.ts
      scenes.test.ts
      GanttChart.test.ts
      ScheduleChart.test.ts
```

The package imports shared UI primitives from `@nerv-iip/ui`. It must not deep-import shadcn-vue registry files.

## Public API

`frontend/packages/scheduling-visualization/src/index.ts` exports:

```ts
export { default as GanttChart } from './components/GanttChart.vue'
export { default as ScheduleChart } from './components/ScheduleChart.vue'
export { default as SchedulingToolbar } from './components/SchedulingToolbar.vue'
export { default as SchedulingDetailSheet } from './components/SchedulingDetailSheet.vue'
export { createMockGanttFixture, createMockScheduleFixture } from './model/fixtures'
export type {
  GanttTask,
  GanttDependency,
  GanttConflict,
  GanttChartProps,
  GanttSelection,
} from './model/gantt'
export type {
  ScheduleResource,
  ScheduleOperation,
  ScheduleCapacityBand,
  ScheduleConflict,
  ScheduleChartProps,
  ScheduleSelection,
} from './model/schedule'
```

The Console app imports only from `@nerv-iip/scheduling-visualization`.

## Data Model

### Gantt

`GanttTask` includes `id`, `parentId`, `name`, `code`, `start`, `end`, `progress`, `status`, `assignee`, `baselineStart`, `baselineEnd`, `isMilestone`, `isLocked`, `children`, and `conflictIds`.

`GanttDependency` includes `id`, `sourceTaskId`, `targetTaskId`, and `type`, where `type` is one of `finish-start`, `start-start`, `finish-finish`, or `start-finish`.

`GanttConflict` includes `id`, `taskId`, `severity`, `title`, `description`, and `resolutionHint`.

### Schedule

`ScheduleResource` includes `id`, `name`, `kind`, `workCenterCode`, `capacityPerShift`, and `calendarLabel`.

`ScheduleOperation` includes `id`, `resourceId`, `workOrderCode`, `operationCode`, `name`, `skuCode`, `start`, `end`, `progress`, `status`, `isLocked`, `loadPercent`, and `conflictIds`.

`ScheduleCapacityBand` includes `id`, `resourceId`, `start`, `end`, `loadPercent`, `capacityPercent`, and `isOverloaded`.

`ScheduleConflict` mirrors the Gantt conflict shape and attaches to either an operation or resource.

All date values are ISO strings at package boundaries and converted to `Date` only inside time-scale utilities.

## Rendering Architecture

Vue components calculate layout inputs and own all reactive state. Renderer functions convert immutable model snapshots into `SchedulingScene` objects. `renderSceneToLeafer` is the only layer that calls Leafer UI constructors.

This separation keeps most logic testable without a browser canvas and limits Leafer API churn to one adapter boundary.

Scene objects include:

1. background grid lines and non-working zones,
2. row labels and row separators,
3. task/operation bars,
4. milestone symbols,
5. dependency paths,
6. baseline bars,
7. progress overlays,
8. capacity histogram bars,
9. conflict markers,
10. selection and hover overlays.

## Interactions

1. Toolbar controls switch zoom between `day`, `week` and `month`.
2. Users can toggle dependencies, baselines, capacity and conflicts.
3. Gantt rows support expand/collapse.
4. Clicking a task, milestone, dependency, resource, operation or conflict emits a typed selection event.
5. Drag and resize operations update local preview state only. Preview commands can be undone, redone or reset.
6. Locked items render with a locked visual state and do not create preview commands.
7. The package emits `preview-change` events but never saves to a server.

## Console Demo

Add `frontend/apps/console/src/pages/business/scheduling/index.vue` with:

1. route meta requiring authentication,
2. a page header matching existing business console density,
3. tabs for `Gantt` and `Schedule`,
4. fixture selector,
5. toolbar controls,
6. chart area with fixed responsive dimensions,
7. detail sheet for the selected object,
8. mock-only data source comments in code and docs.

The page must not mention keyboard shortcuts or usage instructions as visible tutorial copy. Controls should be discoverable by labels, icons, tooltips and conventional placement.

## Design System Update Requirement

Formal completion includes updating the frontend design system, not just component code.

Required DESIGN changes:

1. Add `frontend/DESIGN/components/scheduling-visualization.md` describing the package-level components, anatomy, variants, states, accessibility expectations, responsive behavior and anti-patterns.
2. Add or update `frontend/DESIGN/patterns/blocks/scheduling-workspace.md` for the page pattern: toolbar, grid/canvas split, detail sheet, status badges and dense operational layout.
3. Update `frontend/DESIGN/index.md` quick-reference tables to include the scheduling visualization components and pattern.
4. Update `frontend/DESIGN/roadmaps/business-console-readiness.md` to mark the scheduling visualization design-system contract as introduced.
5. Keep `@nerv-iip/ui` as the stable shadcn-vue primitive boundary. The new package may compose `Button`, `Badge`, `Tabs`, `Sheet`, `Select`, `Tooltip`, `Progress`, `ScrollArea`, `Table` and `Card`, but must not fork their skins.

## Dependencies

Add `leafer-ui` as a dependency of `@nerv-iip/scheduling-visualization` only. Do not add it to `@nerv-iip/ui` or the Console app directly.

If tests need to isolate canvas behavior, renderer scene builders should be tested without Leafer. Leafer adapter smoke tests may mock the constructors rather than requiring a real WebGL context.

## Accessibility And Responsiveness

1. Toolbar controls must have accessible labels.
2. The active tab and selected item must be reflected in DOM state, not only canvas color.
3. Detail sheet content must have a title and structured labels.
4. The chart area must not cause page-level horizontal overflow at `390x844`.
5. Mobile may prioritize read-only viewing and selection; drag/resize preview can be desktop-only if touch handling is not stable in the first slice.
6. Color alone must not be the only conflict signal; conflicts use severity text/icon markers in details and visible markers on canvas.

## Verification

Minimum checks for completion:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test
pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck
pnpm -C frontend --filter @nerv-iip/console test -- src/pages/business/scheduling/index.test.ts
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

If browser tooling is available, also verify the Console demo at desktop `1366x900` and mobile `390x844` with screenshots or equivalent Playwright assertions for nonblank canvas rendering and no incoherent text overlap.

## Acceptance Criteria

1. `@nerv-iip/scheduling-visualization` exists as a workspace package with stable exports.
2. `GanttChart` renders mock task hierarchy, bars, progress, milestones, baselines, dependencies, conflicts and today line.
3. `ScheduleChart` renders mock resources, operations, capacity bands, load histogram, locked states and overload/conflict markers.
4. Toolbar controls update zoom and visibility toggles.
5. Selection opens a detail sheet in the Console demo.
6. Drag/resize preview commands support undo, redo and reset without server persistence.
7. Console demo page renders behind the existing auth guard.
8. Design System docs are updated as specified.
9. Required tests and frontend gates pass or any environment blocker is reported with command output and scope.

## Spec Self-Review

1. No incomplete scope remains; backend integration, export features and APS optimization are explicitly excluded.
2. The package boundary matches the RFC and frontend architecture docs.
3. The Design System update is part of the acceptance criteria, as requested.
4. Testable logic is separated from Leafer adapter calls so the plan can use TDD without depending on a real canvas for every test.
