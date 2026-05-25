import type { SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import { createTimeScale } from '../time-scale/timeScale'
import type { SchedulingZoom } from '../time-scale/timeScale'
import type { SchedulingScene, SchedulingSceneElement } from '../canvas/sceneTypes'
import { flattenGanttTasks } from '../model/gantt'
import type { GanttFixture, GanttRow, GanttTask, SchedulingStatus } from '../model/gantt'

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
const barHeight = 14

const statusFill: Record<SchedulingStatus, string> = {
  planned: '#93c5fd',
  ready: '#38bdf8',
  running: '#2563eb',
  blocked: '#ef4444',
  done: '#16a34a',
}

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

function addGanttBars(
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
    const window = taskWindow(row, options.previewById)
    const x = labelWidth + scale.dateToX(window.start)
    const endX = labelWidth + scale.dateToX(window.end)
    const width = Math.max(row.isMilestone ? 16 : endX - x, 8)
    const barY = y + Math.round((options.rowHeight - barHeight) / 2)

    if (row.isMilestone) {
      elements.push({
        id: row.id,
        kind: 'milestone',
        x,
        y: barY,
        width: 16,
        height: 16,
        fill: '#0f172a',
        points: [
          { x: x + 8, y: barY },
          { x: x + 16, y: barY + 8 },
          { x: x + 8, y: barY + 16 },
          { x, y: barY + 8 },
        ],
        metadata: { taskId: row.id },
      })
      return
    }

    if (options.showBaselines && row.baselineStart && row.baselineEnd) {
      const baselineX = labelWidth + scale.dateToX(row.baselineStart)
      const baselineEndX = labelWidth + scale.dateToX(row.baselineEnd)
      elements.push({
        id: `baseline-${row.id}`,
        kind: 'baseline',
        x: baselineX,
        y: barY + barHeight + 4,
        width: Math.max(baselineEndX - baselineX, 6),
        height: 4,
        fill: '#94a3b8',
        metadata: { taskId: row.id },
      })
    }

    elements.push({
      id: row.id,
      kind: 'bar',
      x,
      y: barY,
      width,
      height: barHeight,
      fill: statusFill[row.status],
      stroke: row.isLocked ? '#0f172a' : '#1d4ed8',
      metadata: { taskId: row.id, status: row.status },
    })
    elements.push({
      id: `progress-${row.id}`,
      kind: 'progress',
      x,
      y: barY,
      width: Math.round(width * Math.min(Math.max(row.progress, 0), 100) / 100),
      height: barHeight,
      fill: '#0f172a',
      metadata: { taskId: row.id, progress: row.progress },
    })
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
  addGanttBars(elements, rows, options)
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
  }

  return {
    width: options.width,
    height,
    rowHeight: options.rowHeight,
    elements,
  }
}
