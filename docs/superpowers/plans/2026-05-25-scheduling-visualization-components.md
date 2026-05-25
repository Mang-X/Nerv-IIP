# Scheduling Visualization Components Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `@nerv-iip/scheduling-visualization`, a reusable frontend package for mock-only Gantt and scheduling visualization components using Leafer UI and the existing Nerv-IIP design system.

**Architecture:** The package keeps domain-shaped mock data, time-scale math, command state and scene generation testable without a canvas. Vue components own state and controls, while one Leafer adapter turns immutable scene objects into canvas elements. No Console route, backend API, OpenAPI snapshot or generated client is changed in this slice.

**Tech Stack:** Vue 3 `<script setup lang="ts">`, TypeScript, Vitest, Vue Test Utils, Leafer UI, pnpm workspace, `@nerv-iip/ui` shadcn-vue primitives.

---

## File Structure

Create these files:

- `frontend/packages/scheduling-visualization/package.json` — package metadata, dependency on `leafer-ui`, package scripts.
- `frontend/packages/scheduling-visualization/tsconfig.json` — package TypeScript config extending workspace base.
- `frontend/packages/scheduling-visualization/src/index.ts` — stable package exports.
- `frontend/packages/scheduling-visualization/src/model/gantt.ts` — Gantt model types and flattening helpers.
- `frontend/packages/scheduling-visualization/src/model/schedule.ts` — schedule model types and row grouping helpers.
- `frontend/packages/scheduling-visualization/src/model/fixtures.ts` — deterministic mock fixtures.
- `frontend/packages/scheduling-visualization/src/time-scale/timeScale.ts` — ISO date to pixel math and zoom definitions.
- `frontend/packages/scheduling-visualization/src/time-scale/visibleRange.ts` — visible row and time window helpers.
- `frontend/packages/scheduling-visualization/src/state/useSchedulingCommands.ts` — local preview command stack with undo/redo/reset.
- `frontend/packages/scheduling-visualization/src/state/useSchedulingSelection.ts` — typed selection helpers.
- `frontend/packages/scheduling-visualization/src/canvas/sceneTypes.ts` — canvas-independent scene shape.
- `frontend/packages/scheduling-visualization/src/canvas/leaferTypes.ts` — narrow adapter types for Leafer.
- `frontend/packages/scheduling-visualization/src/canvas/createLeaferSurface.ts` — Leafer instance lifecycle wrapper.
- `frontend/packages/scheduling-visualization/src/renderers/buildGanttScene.ts` — Gantt model to scene objects.
- `frontend/packages/scheduling-visualization/src/renderers/buildScheduleScene.ts` — schedule model to scene objects.
- `frontend/packages/scheduling-visualization/src/renderers/renderSceneToLeafer.ts` — scene objects to Leafer elements.
- `frontend/packages/scheduling-visualization/src/components/GanttChart.vue` — Gantt chart UI and canvas mount.
- `frontend/packages/scheduling-visualization/src/components/ScheduleChart.vue` — schedule chart UI and canvas mount.
- `frontend/packages/scheduling-visualization/src/components/SchedulingToolbar.vue` — shared controls for zoom, toggles, undo/redo/reset.
- `frontend/packages/scheduling-visualization/src/components/SchedulingDetailSheet.vue` — selected item details.
- `frontend/packages/scheduling-visualization/src/components/SchedulingWorkspace.vue` — package-local composition harness with fixtures.
- `frontend/packages/scheduling-visualization/src/tests/timeScale.test.ts` — time-scale unit tests.
- `frontend/packages/scheduling-visualization/src/tests/ganttRows.test.ts` — Gantt flattening tests.
- `frontend/packages/scheduling-visualization/src/tests/scheduleRows.test.ts` — schedule grouping tests.
- `frontend/packages/scheduling-visualization/src/tests/commands.test.ts` — preview command tests.
- `frontend/packages/scheduling-visualization/src/tests/scenes.test.ts` — scene builder tests.
- `frontend/packages/scheduling-visualization/src/tests/renderSceneToLeafer.test.ts` — adapter mapping tests using a fake surface.
- `frontend/packages/scheduling-visualization/src/tests/GanttChart.test.ts` — component tests.
- `frontend/packages/scheduling-visualization/src/tests/ScheduleChart.test.ts` — component tests.
- `frontend/packages/scheduling-visualization/src/tests/SchedulingWorkspace.test.ts` — package harness tests.
- `frontend/DESIGN/components/scheduling-visualization.md` — component design-system contract.
- `frontend/DESIGN/patterns/blocks/scheduling-workspace.md` — composition pattern contract.

Modify these files:

- `frontend/package.json` — workspace dependency graph is updated through pnpm lock changes only; no new root script is required.
- `frontend/pnpm-workspace.yaml` — already includes `packages/*`; no change expected.
- `frontend/tsconfig.base.json` — add `@nerv-iip/scheduling-visualization`.
- `frontend/vite.config.ts` — add alias and include package in workspace build/typecheck inputs if required by current Vite+ task matching.
- `frontend/DESIGN/index.md` — add quick-reference rows.
- `frontend/DESIGN/roadmaps/business-console-readiness.md` — record scheduling visualization contract introduced.
- `docs/architecture/technology-stack-references.md` — update Leafer UI status from candidate-only to package dependency after code lands.
- `docs/architecture/gantt-scheduling-visualization-rfc.md` — update current status to point at the package.
- `docs/architecture/implementation-readiness.md` — add a status line after verification succeeds.

## Task 1: Package Scaffold And Workspace Wiring

**Files:**
- Create: `frontend/packages/scheduling-visualization/package.json`
- Create: `frontend/packages/scheduling-visualization/tsconfig.json`
- Create: `frontend/packages/scheduling-visualization/src/index.ts`
- Modify: `frontend/tsconfig.base.json`
- Modify: `frontend/vite.config.ts`
- Generated: `frontend/pnpm-lock.yaml`

- [ ] **Step 1: Write the package export contract test**

Create `frontend/packages/scheduling-visualization/src/index.contract.test.ts`:

```ts
import { describe, expect, it } from 'vitest'

import * as scheduling from './index'

describe('@nerv-iip/scheduling-visualization public exports', () => {
  it('exports the stable component and fixture APIs', () => {
    expect(Object.keys(scheduling).sort()).toEqual([
      'GanttChart',
      'ScheduleChart',
      'SchedulingDetailSheet',
      'SchedulingToolbar',
      'SchedulingWorkspace',
      'createMockGanttFixture',
      'createMockScheduleFixture',
    ])
  })
})
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/index.contract.test.ts
```

Expected: command fails because the package and script do not exist yet.

- [ ] **Step 3: Add package metadata**

Create `frontend/packages/scheduling-visualization/package.json`:

```json
{
  "name": "@nerv-iip/scheduling-visualization",
  "version": "0.1.0",
  "private": true,
  "type": "module",
  "exports": {
    ".": "./src/index.ts"
  },
  "scripts": {
    "test": "vp test run src",
    "typecheck": "vue-tsc --noEmit -p tsconfig.json"
  },
  "dependencies": {
    "@nerv-iip/ui": "workspace:*",
    "leafer-ui": "^2.1.0",
    "lucide-vue-next": "1.0.0",
    "vue": "3.5.34"
  }
}
```

Create `frontend/packages/scheduling-visualization/tsconfig.json`:

```json
{
  "extends": "../../tsconfig.base.json",
  "compilerOptions": {
    "composite": false
  },
  "include": ["src/**/*.ts", "src/**/*.vue"]
}
```

Create `frontend/packages/scheduling-visualization/src/index.ts` with temporary component exports as named constants so the contract can fail only on missing real implementation in later tasks:

```ts
export const GanttChart = undefined
export const ScheduleChart = undefined
export const SchedulingDetailSheet = undefined
export const SchedulingToolbar = undefined
export const SchedulingWorkspace = undefined

export function createMockGanttFixture() {
  return undefined
}

export function createMockScheduleFixture() {
  return undefined
}
```

- [ ] **Step 4: Wire workspace aliases**

Modify `frontend/tsconfig.base.json` paths:

```json
"@nerv-iip/scheduling-visualization": [
  "packages/scheduling-visualization/src/index.ts"
]
```

Modify `frontend/vite.config.ts` alias:

```ts
'@nerv-iip/scheduling-visualization': fileURLToPath(
  new URL('./packages/scheduling-visualization/src/index.ts', import.meta.url),
),
```

- [ ] **Step 5: Install dependencies**

Run:

```powershell
pnpm -C frontend install
```

Expected: command exits 0 and updates `frontend/pnpm-lock.yaml` with `leafer-ui` under the new package.

- [ ] **Step 6: Run scaffold verification**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/index.contract.test.ts
pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck
```

Expected: both commands exit 0.

- [ ] **Step 7: Commit scaffold**

Run:

```powershell
git add frontend/packages/scheduling-visualization frontend/tsconfig.base.json frontend/vite.config.ts frontend/pnpm-lock.yaml
git commit -m "feat: scaffold scheduling visualization package"
```

## Task 2: Models, Fixtures And Time Scale

**Files:**
- Create: `frontend/packages/scheduling-visualization/src/model/gantt.ts`
- Create: `frontend/packages/scheduling-visualization/src/model/schedule.ts`
- Create: `frontend/packages/scheduling-visualization/src/model/fixtures.ts`
- Create: `frontend/packages/scheduling-visualization/src/time-scale/timeScale.ts`
- Create: `frontend/packages/scheduling-visualization/src/time-scale/visibleRange.ts`
- Create: `frontend/packages/scheduling-visualization/src/tests/timeScale.test.ts`
- Create: `frontend/packages/scheduling-visualization/src/tests/ganttRows.test.ts`
- Create: `frontend/packages/scheduling-visualization/src/tests/scheduleRows.test.ts`
- Modify: `frontend/packages/scheduling-visualization/src/index.ts`

- [ ] **Step 1: Write time-scale tests**

Create `frontend/packages/scheduling-visualization/src/tests/timeScale.test.ts`:

```ts
import { describe, expect, it } from 'vitest'

import { createTimeScale } from '../time-scale/timeScale'

describe('createTimeScale', () => {
  it('maps ISO dates to stable pixels and back', () => {
    const scale = createTimeScale({
      start: '2026-05-01T00:00:00.000Z',
      end: '2026-05-11T00:00:00.000Z',
      width: 1000,
      zoom: 'day',
    })

    expect(scale.dateToX('2026-05-06T00:00:00.000Z')).toBe(500)
    expect(scale.xToDate(500).toISOString()).toBe('2026-05-06T00:00:00.000Z')
  })

  it('creates readable tick labels for week zoom', () => {
    const scale = createTimeScale({
      start: '2026-05-01T00:00:00.000Z',
      end: '2026-05-22T00:00:00.000Z',
      width: 840,
      zoom: 'week',
    })

    expect(scale.ticks.map((tick) => tick.label)).toEqual(['May 1', 'May 8', 'May 15', 'May 22'])
  })
})
```

- [ ] **Step 2: Write Gantt row tests**

Create `frontend/packages/scheduling-visualization/src/tests/ganttRows.test.ts`:

```ts
import { describe, expect, it } from 'vitest'

import { flattenGanttTasks } from '../model/gantt'
import { createMockGanttFixture } from '../model/fixtures'

describe('flattenGanttTasks', () => {
  it('flattens expanded hierarchy with depth and parent context', () => {
    const fixture = createMockGanttFixture()
    const rows = flattenGanttTasks(fixture.tasks, new Set(['phase-engineering']))

    expect(rows.slice(0, 3).map((row) => [row.id, row.depth, row.hasChildren])).toEqual([
      ['phase-engineering', 0, true],
      ['task-ebom-release', 1, false],
      ['task-routing-review', 1, false],
    ])
  })

  it('hides child rows when a parent is collapsed', () => {
    const fixture = createMockGanttFixture()
    const rows = flattenGanttTasks(fixture.tasks, new Set())

    expect(rows.some((row) => row.id === 'task-ebom-release')).toBe(false)
  })
})
```

- [ ] **Step 3: Write schedule grouping tests**

Create `frontend/packages/scheduling-visualization/src/tests/scheduleRows.test.ts`:

```ts
import { describe, expect, it } from 'vitest'

import { groupScheduleRows } from '../model/schedule'
import { createMockScheduleFixture } from '../model/fixtures'

describe('groupScheduleRows', () => {
  it('attaches operations to resource rows', () => {
    const fixture = createMockScheduleFixture()
    const rows = groupScheduleRows(fixture.resources, fixture.operations)

    expect(rows[0]).toMatchObject({
      id: 'wc-pack-01',
      operationIds: ['op-packing-1001', 'op-packing-1002'],
    })
  })
})
```

- [ ] **Step 4: Run tests to verify they fail**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/timeScale.test.ts src/tests/ganttRows.test.ts src/tests/scheduleRows.test.ts
```

Expected: command fails because model and time-scale modules do not exist yet.

- [ ] **Step 5: Implement model and fixtures**

Implement `gantt.ts` with these exported shapes and helper:

```ts
export type SchedulingStatus = 'planned' | 'ready' | 'running' | 'blocked' | 'done'
export type ConflictSeverity = 'info' | 'warning' | 'critical'

export interface GanttTask {
  id: string
  parentId?: string
  name: string
  code: string
  start: string
  end: string
  progress: number
  status: SchedulingStatus
  assignee?: string
  baselineStart?: string
  baselineEnd?: string
  isMilestone?: boolean
  isLocked?: boolean
  children?: GanttTask[]
  conflictIds?: string[]
}

export interface GanttDependency {
  id: string
  sourceTaskId: string
  targetTaskId: string
  type: 'finish-start' | 'start-start' | 'finish-finish' | 'start-finish'
}

export interface GanttConflict {
  id: string
  taskId: string
  severity: ConflictSeverity
  title: string
  description: string
  resolutionHint: string
}

export interface GanttRow extends GanttTask {
  depth: number
  hasChildren: boolean
}

export interface GanttFixture {
  id: string
  name: string
  rangeStart: string
  rangeEnd: string
  tasks: GanttTask[]
  dependencies: GanttDependency[]
  conflicts: GanttConflict[]
}

export type GanttSelection =
  | { kind: 'task'; id: string }
  | { kind: 'dependency'; id: string }
  | { kind: 'conflict'; id: string }

export interface GanttChartProps extends GanttFixture {
  expandedTaskIds?: string[]
  selected?: GanttSelection
}

export function flattenGanttTasks(tasks: GanttTask[], expandedTaskIds: Set<string>, depth = 0): GanttRow[] {
  return tasks.flatMap((task) => {
    const children = task.children ?? []
    const row: GanttRow = { ...task, depth, hasChildren: children.length > 0 }
    if (children.length === 0 || !expandedTaskIds.has(task.id)) {
      return [row]
    }

    return [row, ...flattenGanttTasks(children, expandedTaskIds, depth + 1)]
  })
}
```

Implement `schedule.ts` with equivalent exported interfaces and `groupScheduleRows(resources, operations)` returning each resource with sorted operation ids.

Implement `fixtures.ts` with deterministic IDs used by tests:

```ts
export function createMockGanttFixture(): GanttFixture {
  return {
    id: 'gantt-release-plan',
    name: 'Manufacturing release plan',
    rangeStart: '2026-05-01T00:00:00.000Z',
    rangeEnd: '2026-05-22T00:00:00.000Z',
    tasks: [
      {
        id: 'phase-engineering',
        name: 'Engineering release',
        code: 'ENG',
        start: '2026-05-01T00:00:00.000Z',
        end: '2026-05-08T00:00:00.000Z',
        progress: 65,
        status: 'running',
        children: [
          {
            id: 'task-ebom-release',
            parentId: 'phase-engineering',
            name: 'EBOM release',
            code: 'EBOM-REL',
            start: '2026-05-01T00:00:00.000Z',
            end: '2026-05-04T00:00:00.000Z',
            progress: 100,
            status: 'done',
          },
          {
            id: 'task-routing-review',
            parentId: 'phase-engineering',
            name: 'Routing review',
            code: 'ROUTE-REV',
            start: '2026-05-04T00:00:00.000Z',
            end: '2026-05-08T00:00:00.000Z',
            progress: 35,
            status: 'running',
            conflictIds: ['conflict-routing-capacity'],
          },
        ],
      },
      {
        id: 'milestone-production-ready',
        name: 'Production ready',
        code: 'MFG-GO',
        start: '2026-05-10T00:00:00.000Z',
        end: '2026-05-10T00:00:00.000Z',
        progress: 0,
        status: 'planned',
        isMilestone: true,
      },
    ],
    dependencies: [
      {
        id: 'dep-ebom-routing',
        sourceTaskId: 'task-ebom-release',
        targetTaskId: 'task-routing-review',
        type: 'finish-start',
      },
    ],
    conflicts: [
      {
        id: 'conflict-routing-capacity',
        taskId: 'task-routing-review',
        severity: 'warning',
        title: 'Capacity warning',
        description: 'Routing review overlaps a constrained work-center window.',
        resolutionHint: 'Move review completion before the constrained shift.',
      },
    ],
  }
}
```

- [ ] **Step 6: Implement time scale**

Implement `createTimeScale(options)` returning `{ start, end, width, zoom, ticks, dateToX, xToDate }`. Use UTC milliseconds and stable `Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })` labels. Use tick step of 1 day, 7 days or 30 days for `day`, `week`, `month`.

- [ ] **Step 7: Update public exports**

Replace temporary exports in `src/index.ts` with real exports from `model`, `fixtures` and `time-scale`.

- [ ] **Step 8: Run tests**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/timeScale.test.ts src/tests/ganttRows.test.ts src/tests/scheduleRows.test.ts
pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck
```

Expected: commands exit 0.

- [ ] **Step 9: Commit model foundation**

Run:

```powershell
git add frontend/packages/scheduling-visualization
git commit -m "feat: add scheduling visualization model foundation"
```

## Task 3: Preview Commands And Selection

**Files:**
- Create: `frontend/packages/scheduling-visualization/src/state/useSchedulingCommands.ts`
- Create: `frontend/packages/scheduling-visualization/src/state/useSchedulingSelection.ts`
- Create: `frontend/packages/scheduling-visualization/src/tests/commands.test.ts`
- Modify: `frontend/packages/scheduling-visualization/src/index.ts`

- [ ] **Step 1: Write command tests**

Create `frontend/packages/scheduling-visualization/src/tests/commands.test.ts`:

```ts
import { describe, expect, it } from 'vitest'

import { createSchedulingCommandStack } from '../state/useSchedulingCommands'

describe('createSchedulingCommandStack', () => {
  it('applies move preview commands and supports undo and redo', () => {
    const stack = createSchedulingCommandStack()

    stack.execute({
      id: 'cmd-move-routing',
      targetId: 'task-routing-review',
      kind: 'move',
      before: { start: '2026-05-04T00:00:00.000Z', end: '2026-05-08T00:00:00.000Z' },
      after: { start: '2026-05-05T00:00:00.000Z', end: '2026-05-09T00:00:00.000Z' },
    })

    expect(stack.previewById.value['task-routing-review']).toEqual({
      start: '2026-05-05T00:00:00.000Z',
      end: '2026-05-09T00:00:00.000Z',
    })

    stack.undo()
    expect(stack.previewById.value['task-routing-review']).toBeUndefined()

    stack.redo()
    expect(stack.previewById.value['task-routing-review']?.start).toBe('2026-05-05T00:00:00.000Z')
  })

  it('clears previews with reset', () => {
    const stack = createSchedulingCommandStack()

    stack.execute({
      id: 'cmd-resize',
      targetId: 'op-packing-1001',
      kind: 'resize',
      before: { start: '2026-05-06T08:00:00.000Z', end: '2026-05-06T10:00:00.000Z' },
      after: { start: '2026-05-06T08:00:00.000Z', end: '2026-05-06T11:00:00.000Z' },
    })
    stack.reset()

    expect(stack.canUndo.value).toBe(false)
    expect(stack.canRedo.value).toBe(false)
    expect(stack.previewById.value).toEqual({})
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/commands.test.ts
```

Expected: command fails because command state does not exist yet.

- [ ] **Step 3: Implement command stack**

Implement `createSchedulingCommandStack()` using `shallowRef` and `computed`. Export `SchedulingPreviewCommand`, `SchedulingPreviewWindow` and `SchedulingCommandStack`. The stack stores `done`, `undone` and `previewById`. `execute` appends a command and clears redo. `undo` removes the last done command and rebuilds previews from remaining done commands. `redo` reapplies the last undone command. `reset` clears everything.

- [ ] **Step 4: Implement selection helper**

Implement `useSchedulingSelection<TSelection>()` returning `selected`, `select(value)`, and `clearSelection()`.

- [ ] **Step 5: Export state APIs**

Update `src/index.ts` to export `createSchedulingCommandStack`, `useSchedulingSelection`, and their types.

- [ ] **Step 6: Run verification**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/commands.test.ts
pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck
```

Expected: commands exit 0.

- [ ] **Step 7: Commit command state**

Run:

```powershell
git add frontend/packages/scheduling-visualization
git commit -m "feat: add scheduling preview command state"
```

## Task 4: Scene Builders

**Files:**
- Create: `frontend/packages/scheduling-visualization/src/canvas/sceneTypes.ts`
- Create: `frontend/packages/scheduling-visualization/src/renderers/buildGanttScene.ts`
- Create: `frontend/packages/scheduling-visualization/src/renderers/buildScheduleScene.ts`
- Create: `frontend/packages/scheduling-visualization/src/tests/scenes.test.ts`
- Modify: `frontend/packages/scheduling-visualization/src/index.ts`

- [ ] **Step 1: Write scene tests**

Create `frontend/packages/scheduling-visualization/src/tests/scenes.test.ts`:

```ts
import { describe, expect, it } from 'vitest'

import { createMockGanttFixture, createMockScheduleFixture } from '../model/fixtures'
import { buildGanttScene } from '../renderers/buildGanttScene'
import { buildScheduleScene } from '../renderers/buildScheduleScene'

describe('scene builders', () => {
  it('builds Gantt bars, milestones, dependencies and conflicts', () => {
    const fixture = createMockGanttFixture()
    const scene = buildGanttScene({
      fixture,
      expandedTaskIds: new Set(['phase-engineering']),
      width: 960,
      rowHeight: 36,
      zoom: 'day',
      showDependencies: true,
      showBaselines: true,
      showConflicts: true,
      today: '2026-05-06T00:00:00.000Z',
      previewById: {},
    })

    expect(scene.elements.filter((element) => element.kind === 'bar').map((element) => element.id)).toContain('task-routing-review')
    expect(scene.elements.some((element) => element.kind === 'milestone')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'dependency')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'conflict')).toBe(true)
    expect(scene.height).toBeGreaterThan(100)
  })

  it('builds schedule operations and capacity bands', () => {
    const fixture = createMockScheduleFixture()
    const scene = buildScheduleScene({
      fixture,
      width: 960,
      rowHeight: 44,
      zoom: 'day',
      showCapacity: true,
      showConflicts: true,
      today: '2026-05-06T00:00:00.000Z',
      previewById: {},
    })

    expect(scene.elements.filter((element) => element.kind === 'bar').map((element) => element.id)).toContain('op-packing-1001')
    expect(scene.elements.some((element) => element.kind === 'capacity')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'conflict')).toBe(true)
  })
})
```

- [ ] **Step 2: Run scene tests to verify they fail**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/scenes.test.ts
```

Expected: command fails because scene builders do not exist yet.

- [ ] **Step 3: Define scene types**

Implement `sceneTypes.ts` with:

```ts
export type SchedulingSceneElementKind =
  | 'grid-line'
  | 'row-label'
  | 'bar'
  | 'progress'
  | 'milestone'
  | 'dependency'
  | 'baseline'
  | 'today'
  | 'capacity'
  | 'conflict'
  | 'selection'

export interface SchedulingSceneElement {
  id: string
  kind: SchedulingSceneElementKind
  x: number
  y: number
  width?: number
  height?: number
  text?: string
  fill?: string
  stroke?: string
  severity?: 'info' | 'warning' | 'critical'
  points?: Array<{ x: number; y: number }>
  metadata?: Record<string, string | number | boolean>
}

export interface SchedulingScene {
  width: number
  height: number
  rowHeight: number
  elements: SchedulingSceneElement[]
}
```

- [ ] **Step 4: Implement Gantt scene builder**

`buildGanttScene` should flatten rows, create a `createTimeScale` instance, emit grid and row-label elements, then emit bars or milestones per row. Add progress overlays for non-milestone tasks. Add dependency elements as simple three-point orthogonal paths. Add conflict markers when enabled. Add a today line if it falls inside the range.

- [ ] **Step 5: Implement schedule scene builder**

`buildScheduleScene` should group rows by resource, emit row-label elements, operation bars, progress overlays, capacity elements when enabled, conflict markers and a today line.

- [ ] **Step 6: Run verification**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/scenes.test.ts
pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck
```

Expected: commands exit 0.

- [ ] **Step 7: Commit scene builders**

Run:

```powershell
git add frontend/packages/scheduling-visualization
git commit -m "feat: build scheduling visualization scenes"
```

## Task 5: Leafer Adapter

**Files:**
- Create: `frontend/packages/scheduling-visualization/src/canvas/leaferTypes.ts`
- Create: `frontend/packages/scheduling-visualization/src/canvas/createLeaferSurface.ts`
- Create: `frontend/packages/scheduling-visualization/src/renderers/renderSceneToLeafer.ts`
- Create: `frontend/packages/scheduling-visualization/src/tests/renderSceneToLeafer.test.ts`
- Modify: `frontend/packages/scheduling-visualization/src/index.ts`

- [ ] **Step 1: Write adapter mapping test**

Create `frontend/packages/scheduling-visualization/src/tests/renderSceneToLeafer.test.ts`:

```ts
import { describe, expect, it, vi } from 'vitest'

import { renderSceneToLeafer } from '../renderers/renderSceneToLeafer'
import type { SchedulingScene } from '../canvas/sceneTypes'
import type { LeaferSurface } from '../canvas/leaferTypes'

describe('renderSceneToLeafer', () => {
  it('clears the surface and adds canvas elements', () => {
    const surface: LeaferSurface = {
      clear: vi.fn(),
      addRect: vi.fn(),
      addText: vi.fn(),
      addPath: vi.fn(),
      dispose: vi.fn(),
    }
    const scene: SchedulingScene = {
      width: 400,
      height: 160,
      rowHeight: 40,
      elements: [
        { id: 'row-1', kind: 'row-label', x: 0, y: 0, text: 'Work center', fill: '#111827' },
        { id: 'bar-1', kind: 'bar', x: 120, y: 12, width: 100, height: 16, fill: '#2563eb' },
        {
          id: 'dep-1',
          kind: 'dependency',
          x: 0,
          y: 0,
          stroke: '#64748b',
          points: [
            { x: 120, y: 20 },
            { x: 180, y: 20 },
            { x: 180, y: 60 },
          ],
        },
      ],
    }

    renderSceneToLeafer(surface, scene)

    expect(surface.clear).toHaveBeenCalledOnce()
    expect(surface.addText).toHaveBeenCalledWith(expect.objectContaining({ id: 'row-1' }))
    expect(surface.addRect).toHaveBeenCalledWith(expect.objectContaining({ id: 'bar-1' }))
    expect(surface.addPath).toHaveBeenCalledWith(expect.objectContaining({ id: 'dep-1' }))
  })
})
```

- [ ] **Step 2: Run adapter test to verify it fails**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/renderSceneToLeafer.test.ts
```

Expected: command fails because adapter files do not exist yet.

- [ ] **Step 3: Define adapter interface**

Implement `leaferTypes.ts` with a narrow `LeaferSurface` interface:

```ts
export interface LeaferRectInput {
  id: string
  x: number
  y: number
  width: number
  height: number
  fill?: string
  stroke?: string
  cornerRadius?: number
  metadata?: Record<string, string | number | boolean>
}

export interface LeaferTextInput {
  id: string
  x: number
  y: number
  text: string
  fill?: string
  fontSize?: number
  metadata?: Record<string, string | number | boolean>
}

export interface LeaferPathInput {
  id: string
  points: Array<{ x: number; y: number }>
  stroke?: string
  fill?: string
  metadata?: Record<string, string | number | boolean>
}

export interface LeaferSurface {
  clear(): void
  addRect(input: LeaferRectInput): void
  addText(input: LeaferTextInput): void
  addPath(input: LeaferPathInput): void
  dispose(): void
}
```

- [ ] **Step 4: Implement Leafer lifecycle wrapper**

Implement `createLeaferSurface(host: HTMLElement, width: number, height: number): LeaferSurface` using `new Leafer({ view: host, width, height, hittable: true, smooth: true, pixelRatio: window.devicePixelRatio || 1 })`. Use `Group`, `Rect`, `Text` and `Pen` from `leafer-ui`. Store a root group and clear it with `group.clear()` before each render. Implement `dispose()` by clearing the group and calling `destroy()` on the Leafer instance when the method exists; otherwise clear only.

- [ ] **Step 5: Implement scene renderer**

Implement `renderSceneToLeafer(surface, scene)` by mapping:

- `row-label` to `addText`,
- `bar`, `progress`, `baseline`, `today`, `capacity`, `conflict`, `selection`, `grid-line` to `addRect`,
- `milestone` and `dependency` to `addPath`.

- [ ] **Step 6: Run verification**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/renderSceneToLeafer.test.ts
pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck
```

Expected: commands exit 0.

- [ ] **Step 7: Commit adapter**

Run:

```powershell
git add frontend/packages/scheduling-visualization
git commit -m "feat: add leafer scene adapter"
```

## Task 6: Vue Components

**Files:**
- Create: `frontend/packages/scheduling-visualization/src/components/GanttChart.vue`
- Create: `frontend/packages/scheduling-visualization/src/components/ScheduleChart.vue`
- Create: `frontend/packages/scheduling-visualization/src/components/SchedulingToolbar.vue`
- Create: `frontend/packages/scheduling-visualization/src/components/SchedulingDetailSheet.vue`
- Create: `frontend/packages/scheduling-visualization/src/components/SchedulingWorkspace.vue`
- Create: `frontend/packages/scheduling-visualization/src/tests/GanttChart.test.ts`
- Create: `frontend/packages/scheduling-visualization/src/tests/ScheduleChart.test.ts`
- Create: `frontend/packages/scheduling-visualization/src/tests/SchedulingWorkspace.test.ts`
- Modify: `frontend/packages/scheduling-visualization/src/index.ts`

- [ ] **Step 1: Write component tests with mocked Leafer adapter**

Create `GanttChart.test.ts`, `ScheduleChart.test.ts`, and `SchedulingWorkspace.test.ts`. Mock `createLeaferSurface` so tests do not require real WebGL:

```ts
vi.mock('../canvas/createLeaferSurface', () => ({
  createLeaferSurface: () => ({
    clear: vi.fn(),
    addRect: vi.fn(),
    addText: vi.fn(),
    addPath: vi.fn(),
    dispose: vi.fn(),
  }),
}))
```

Test expectations:

- `GanttChart` renders a `data-test="gantt-chart"` root and emits `select` when a row button is clicked.
- `ScheduleChart` renders a `data-test="schedule-chart"` root and emits `select` when an operation button is clicked.
- `SchedulingWorkspace` renders `Gantt` and `Schedule` tabs, fixture selector, undo/redo/reset controls, and detail sheet text after selecting an item.

- [ ] **Step 2: Run component tests to verify they fail**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/GanttChart.test.ts src/tests/ScheduleChart.test.ts src/tests/SchedulingWorkspace.test.ts
```

Expected: command fails because components do not exist yet.

- [ ] **Step 3: Implement `SchedulingToolbar.vue`**

Use `<script setup lang="ts">`, `Button`, `Badge`, `Select`, `Tooltip` and lucide icons. Props: `zoom`, `showDependencies`, `showBaselines`, `showCapacity`, `showConflicts`, `canUndo`, `canRedo`. Emits: `update:zoom`, `update:showDependencies`, `update:showBaselines`, `update:showCapacity`, `update:showConflicts`, `undo`, `redo`, `reset`.

- [ ] **Step 4: Implement `GanttChart.vue`**

Use props matching `GanttChartProps` plus `zoom`, `showDependencies`, `showBaselines`, `showConflicts`, `previewById`, `today`. Render a split layout: left row list in DOM, right Leafer host in a fixed-height canvas region. Use `onMounted`, `watch`, and `onUnmounted` to create, re-render and dispose the surface. Emit typed `select` and `preview-change`.

- [ ] **Step 5: Implement `ScheduleChart.vue`**

Use props matching schedule fixture plus `zoom`, `showCapacity`, `showConflicts`, `previewById`, `today`. Render resource rows in DOM and Leafer host for operations and capacity. Emit typed `select` and `preview-change`.

- [ ] **Step 6: Implement `SchedulingDetailSheet.vue`**

Use `Sheet`, `Badge`, `Separator` and `Progress` from `@nerv-iip/ui`. Props: selected item summary or `undefined`, `open`. Emits: `update:open`. Include accessible title and structured label/value rows.

- [ ] **Step 7: Implement `SchedulingWorkspace.vue`**

Use mock fixtures, `Tabs`, `Select`, `Card`, `SchedulingToolbar`, `GanttChart`, `ScheduleChart`, `SchedulingDetailSheet`, `createSchedulingCommandStack`, and `useSchedulingSelection`. Keep state inside the component. Do not import router or app-shell.

- [ ] **Step 8: Update exports**

Export the five components and model/state APIs from `src/index.ts`.

- [ ] **Step 9: Run verification**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test -- src/tests/GanttChart.test.ts src/tests/ScheduleChart.test.ts src/tests/SchedulingWorkspace.test.ts
pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck
```

Expected: commands exit 0.

- [ ] **Step 10: Commit components**

Run:

```powershell
git add frontend/packages/scheduling-visualization
git commit -m "feat: add scheduling visualization components"
```

## Task 7: Design System Documentation

**Files:**
- Create: `frontend/DESIGN/components/scheduling-visualization.md`
- Create: `frontend/DESIGN/patterns/blocks/scheduling-workspace.md`
- Modify: `frontend/DESIGN/index.md`
- Modify: `frontend/DESIGN/roadmaps/business-console-readiness.md`
- Modify: `docs/architecture/technology-stack-references.md`
- Modify: `docs/architecture/gantt-scheduling-visualization-rfc.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Write design-system component contract**

Create `frontend/DESIGN/components/scheduling-visualization.md` with sections:

- Purpose
- Anatomy
- Variants: Gantt, Schedule, Workspace
- States: empty, selected, locked, conflict, preview, loading
- Accessibility
- Responsive behavior
- Anti-patterns
- Code boundary

The Code boundary section must say app code consumes `@nerv-iip/scheduling-visualization` and the package consumes `@nerv-iip/ui`; no app should deep-import package internals.

- [ ] **Step 2: Write workspace pattern contract**

Create `frontend/DESIGN/patterns/blocks/scheduling-workspace.md` describing toolbar placement, split row/canvas layout, detail sheet behavior, conflict status treatment and mock-only package harness limits.

- [ ] **Step 3: Update quick references**

Modify `frontend/DESIGN/index.md` to include:

```md
| Scheduling visualization | `frontend/DESIGN/components/scheduling-visualization.md` | Use for Gantt and resource scheduling timeline surfaces. | Do not use for CRUD tables, KPI dashboards, or backend-owned scheduling decisions. |
```

and:

```md
| Gantt or resource timeline workspace | Scheduling workspace | `frontend/DESIGN/patterns/blocks/scheduling-workspace.md` |
```

- [ ] **Step 4: Update roadmap and architecture docs**

Update `frontend/DESIGN/roadmaps/business-console-readiness.md` to record that the scheduling visualization contract exists as package-level foundation, with Console route integration remaining a follow-up.

Update `docs/architecture/technology-stack-references.md` so Leafer UI status says it is used by `@nerv-iip/scheduling-visualization`.

Update `docs/architecture/gantt-scheduling-visualization-rfc.md` to point at the implemented package and note that no real backend integration exists.

Update `docs/architecture/implementation-readiness.md` only after verification passes; add a current-conclusion line that the scheduling visualization package exists as mock-only frontend foundation.

- [ ] **Step 5: Run documentation checks**

Run:

```powershell
rg -n "scheduling-visualization|Scheduling visualization|Leafer UI" frontend/DESIGN docs/architecture docs/superpowers/specs/2026-05-25-scheduling-visualization-components-design.md
git diff --check
```

Expected: `rg` shows the package and docs references; `git diff --check` exits 0.

- [ ] **Step 6: Commit docs**

Run:

```powershell
git add frontend/DESIGN docs/architecture docs/superpowers/specs/2026-05-25-scheduling-visualization-components-design.md
git commit -m "docs: document scheduling visualization design system"
```

## Task 8: Full Verification And PR Update

**Files:**
- Modify only if verification exposes defects in files from earlier tasks.

- [ ] **Step 1: Run package checks**

Run:

```powershell
pnpm -C frontend --filter @nerv-iip/scheduling-visualization test
pnpm -C frontend --filter @nerv-iip/scheduling-visualization typecheck
```

Expected: both commands exit 0.

- [ ] **Step 2: Run frontend gates**

Run:

```powershell
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

Expected: all commands exit 0.

- [ ] **Step 3: Optional browser verification**

If a temporary local preview is created for visual verification, run it without committing Console route files. Check desktop `1366x900` and mobile `390x844` for nonblank canvas, no page-level horizontal overflow, and no incoherent text overlap. Remove any temporary preview files before final status.

- [ ] **Step 4: Inspect final diff and status**

Run:

```powershell
git status --short --branch
git diff --stat origin/codex/issue-78-gantt-rfc...HEAD
```

Expected: only intended files are committed; pre-existing `skills-lock.json` remains unstaged unless the user explicitly asks to include it.

- [ ] **Step 5: Push and update PR**

Run:

```powershell
git push
gh pr view 178 --json url,title,isDraft,headRefName,baseRefName
```

Expected: push exits 0 and PR #178 still targets `codex/issue-78-gantt-rfc` into `main`.

## Self-Review

Spec coverage:

- Package boundary: Task 1.
- Models, fixtures and time-scale math: Task 2.
- Local-only preview undo/redo/reset: Task 3.
- Canvas-independent scene generation: Task 4.
- Leafer UI adapter: Task 5.
- Gantt, Schedule and Workspace Vue components: Task 6.
- Design System updates: Task 7.
- Verification and PR update: Task 8.

Placeholder scan:

- No task uses incomplete implementation markers.
- Every task has exact file paths, commands and expected outcomes.

Type consistency:

- `GanttFixture`, `ScheduleFixture`, `SchedulingScene`, `LeaferSurface`, `GanttSelection` and `ScheduleSelection` are introduced before component tasks consume them.
- Public exports in Task 1 are temporary runtime symbols and are replaced by real exports in later tasks.
