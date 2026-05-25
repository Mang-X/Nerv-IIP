import type { SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import { createTimeScale } from '../time-scale/timeScale'
import type { SchedulingZoom } from '../time-scale/timeScale'
import type { SchedulingScene, SchedulingSceneElement } from '../canvas/sceneTypes'
import { flattenGanttTasks } from '../model/gantt'
import type { GanttFixture, GanttRow, GanttTask } from '../model/gantt'

export interface BuildGanttSceneOptions {
  fixture: GanttFixture
  expandedTaskIds: Set<string>
  width: number
  rowHeight: number
  zoom: SchedulingZoom
  showDependencies: boolean
  showBaselines: boolean
  showConflicts: boolean
  today: string
  previewById: Record<string, SchedulingPreviewWindow>
}

const labelWidth = 220
function findTask(tasks: GanttTask[], taskId: string): GanttTask | undefined {
  for (const task of tasks) {
    if (task.id === taskId) {
      return task
    }

    const child = findTask(task.children ?? [], taskId)
    if (child) {
      return child
    }
  }

  return undefined
}

function taskWindow(row: GanttRow, previewById: Record<string, SchedulingPreviewWindow>) {
  return previewById[row.id] ?? { start: row.start, end: row.end }
}

function addGrid(
  elements: SchedulingSceneElement[],
  rows: GanttRow[],
  rowHeight: number,
  width: number,
) {
  rows.forEach((row, index) => {
    if (index === rows.length - 1) {
      return
    }

    const y = index * rowHeight
    elements.push({
      id: `row-line-${row.id}`,
      kind: 'grid-line',
      x: 0,
      y: y + rowHeight - 1,
      width,
      height: 1,
      fill: '#e2e8f0',
    })
  })
}

function addGanttBaselines(
  elements: SchedulingSceneElement[],
  rows: GanttRow[],
  options: BuildGanttSceneOptions,
) {
  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.width - labelWidth,
    zoom: options.zoom,
  })

  rows.forEach((row, index) => {
    const y = index * options.rowHeight

    if (options.showBaselines && row.baselineStart && row.baselineEnd) {
      const baselineX = labelWidth + scale.dateToX(row.baselineStart)
      const baselineEndX = labelWidth + scale.dateToX(row.baselineEnd)
      elements.push({
        id: `baseline-${row.id}`,
        kind: 'baseline',
        x: baselineX,
        y: y + options.rowHeight - 10,
        width: Math.max(baselineEndX - baselineX, 6),
        height: 4,
        fill: '#94a3b8',
        metadata: { taskId: row.id },
      })
    }
  })
}

function addDependencies(
  elements: SchedulingSceneElement[],
  rows: GanttRow[],
  options: BuildGanttSceneOptions,
) {
  if (!options.showDependencies) {
    return
  }

  const rowIndexById = new Map(rows.map((row, index) => [row.id, index]))
  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.width - labelWidth,
    zoom: options.zoom,
  })

  for (const dependency of options.fixture.dependencies) {
    const sourceTask = findTask(options.fixture.tasks, dependency.sourceTaskId)
    const targetTask = findTask(options.fixture.tasks, dependency.targetTaskId)
    const sourceIndex = rowIndexById.get(dependency.sourceTaskId)
    const targetIndex = rowIndexById.get(dependency.targetTaskId)
    if (!sourceTask || !targetTask || sourceIndex === undefined || targetIndex === undefined) {
      continue
    }

    const sourceWindow = options.previewById[sourceTask.id] ?? sourceTask
    const targetWindow = options.previewById[targetTask.id] ?? targetTask
    const sourceX = labelWidth + scale.dateToX(sourceWindow.end)
    const targetX = labelWidth + scale.dateToX(targetWindow.start)
    const sourceY = sourceIndex * options.rowHeight + options.rowHeight / 2
    const targetY = targetIndex * options.rowHeight + options.rowHeight / 2
    const elbowX = sourceX + Math.max((targetX - sourceX) / 2, 16)

    elements.push({
      id: dependency.id,
      kind: 'dependency',
      x: 0,
      y: 0,
      stroke: '#64748b',
      points: [
        { x: sourceX, y: sourceY },
        { x: elbowX, y: sourceY },
        { x: elbowX, y: targetY },
        { x: targetX, y: targetY },
      ],
      metadata: { sourceTaskId: dependency.sourceTaskId, targetTaskId: dependency.targetTaskId },
    })
  }
}

function addConflicts(
  elements: SchedulingSceneElement[],
  rows: GanttRow[],
  options: BuildGanttSceneOptions,
) {
  if (!options.showConflicts) {
    return
  }

  const rowIndexById = new Map(rows.map((row, index) => [row.id, index]))
  for (const conflict of options.fixture.conflicts) {
    const rowIndex = rowIndexById.get(conflict.taskId)
    if (rowIndex === undefined) {
      continue
    }

    elements.push({
      id: conflict.id,
      kind: 'conflict',
      x: options.width - 28,
      y: rowIndex * options.rowHeight + 10,
      width: 12,
      height: 12,
      fill: conflict.severity === 'critical' ? '#dc2626' : '#f59e0b',
      severity: conflict.severity,
      metadata: { taskId: conflict.taskId, title: conflict.title },
    })
  }
}

export function buildGanttScene(options: BuildGanttSceneOptions): SchedulingScene {
  const rows = flattenGanttTasks(options.fixture.tasks, options.expandedTaskIds)
  const height = Math.max(rows.length * options.rowHeight, options.rowHeight)
  const elements: SchedulingSceneElement[] = []

  addGrid(elements, rows, options.rowHeight, options.width)
  addGanttBaselines(elements, rows, options)
  addDependencies(elements, rows, options)
  addConflicts(elements, rows, options)

  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.width - labelWidth,
    zoom: options.zoom,
  })
  const todayX = labelWidth + scale.dateToX(options.today)
  if (todayX >= labelWidth && todayX <= options.width) {
    elements.push({
      id: 'today',
      kind: 'today',
      x: todayX,
      y: 0,
      width: 2,
      height,
      fill: '#0ea5e9',
    })
    elements.push({
      id: 'today-label',
      kind: 'row-label',
      x: todayX + 5,
      y: 5,
      text: 'Today',
      fill: '#0369a1',
    })
  }

  return {
    width: options.width,
    height,
    rowHeight: options.rowHeight,
    elements,
  }
}
