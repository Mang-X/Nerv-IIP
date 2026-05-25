import type { SchedulingScene, SchedulingSceneElement } from '../canvas/sceneTypes'
import type { SchedulingStatus } from '../model/gantt'
import { groupScheduleRows } from '../model/schedule'
import type { ScheduleFixture } from '../model/schedule'
import type { SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import { createTimeScale } from '../time-scale/timeScale'
import type { SchedulingZoom } from '../time-scale/timeScale'

export interface BuildScheduleSceneOptions {
  fixture: ScheduleFixture
  width: number
  rowHeight: number
  zoom: SchedulingZoom
  showCapacity: boolean
  showConflicts: boolean
  today: string
  previewById: Record<string, SchedulingPreviewWindow>
}

const labelWidth = 230
const barHeight = 16

const statusFill: Record<SchedulingStatus, string> = {
  planned: '#bfdbfe',
  ready: '#38bdf8',
  running: '#2563eb',
  blocked: '#ef4444',
  done: '#16a34a',
}

export function buildScheduleScene(options: BuildScheduleSceneOptions): SchedulingScene {
  const rows = groupScheduleRows(options.fixture.resources, options.fixture.operations)
  const height = Math.max(rows.length * options.rowHeight, options.rowHeight)
  const elements: SchedulingSceneElement[] = []
  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.width - labelWidth,
    zoom: options.zoom,
  })

  rows.forEach((row, index) => {
    const y = index * options.rowHeight
    elements.push({
      id: `resource-${row.id}`,
      kind: 'row-label',
      x: 0,
      y: y + 9,
      text: `${row.workCenterCode} ${row.name}`,
      fill: '#0f172a',
      metadata: { resourceId: row.id },
    })
    elements.push({
      id: `resource-line-${row.id}`,
      kind: 'grid-line',
      x: 0,
      y: y + options.rowHeight - 1,
      width: options.width,
      height: 1,
      fill: '#e2e8f0',
    })

    for (const operationId of row.operationIds) {
      const operation = options.fixture.operations.find((item) => item.id === operationId)
      if (!operation) {
        continue
      }

      const preview = options.previewById[operation.id]
      const start = preview?.start ?? operation.start
      const end = preview?.end ?? operation.end
      const x = labelWidth + scale.dateToX(start)
      const endX = labelWidth + scale.dateToX(end)
      const width = Math.max(endX - x, 8)
      const barY = y + Math.round((options.rowHeight - barHeight) / 2)

      elements.push({
        id: operation.id,
        kind: 'bar',
        x,
        y: barY,
        width,
        height: barHeight,
        fill: statusFill[operation.status],
        stroke: operation.isLocked ? '#0f172a' : '#1d4ed8',
        metadata: {
          operationId: operation.id,
          resourceId: operation.resourceId,
          workOrderCode: operation.workOrderCode,
        },
      })
      elements.push({
        id: `progress-${operation.id}`,
        kind: 'progress',
        x,
        y: barY,
        width: Math.round(width * Math.min(Math.max(operation.progress, 0), 100) / 100),
        height: barHeight,
        fill: '#0f172a',
        metadata: { operationId: operation.id, progress: operation.progress },
      })
    }
  })

  if (options.showCapacity) {
    for (const band of options.fixture.capacityBands) {
      const rowIndex = rows.findIndex((row) => row.id === band.resourceId)
      if (rowIndex < 0) {
        continue
      }

      const x = labelWidth + scale.dateToX(band.start)
      const endX = labelWidth + scale.dateToX(band.end)
      elements.push({
        id: band.id,
        kind: 'capacity',
        x,
        y: rowIndex * options.rowHeight + options.rowHeight - 9,
        width: Math.max(endX - x, 8),
        height: 5,
        fill: band.isOverloaded ? '#f97316' : '#22c55e',
        metadata: {
          resourceId: band.resourceId,
          loadPercent: band.loadPercent,
          capacityPercent: band.capacityPercent,
        },
      })
    }
  }

  if (options.showConflicts) {
    for (const conflict of options.fixture.conflicts) {
      const operation = options.fixture.operations.find((item) => item.id === conflict.targetId)
      const rowIndex = operation
        ? rows.findIndex((row) => row.id === operation.resourceId)
        : rows.findIndex((row) => row.id === conflict.targetId)
      if (rowIndex < 0) {
        continue
      }

      elements.push({
        id: conflict.id,
        kind: 'conflict',
        x: options.width - 28,
        y: rowIndex * options.rowHeight + 12,
        width: 12,
        height: 12,
        fill: conflict.severity === 'critical' ? '#dc2626' : '#f59e0b',
        severity: conflict.severity,
        metadata: { targetId: conflict.targetId, title: conflict.title },
      })
    }
  }

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
