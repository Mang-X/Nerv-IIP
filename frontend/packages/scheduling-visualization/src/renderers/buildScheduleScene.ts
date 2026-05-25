import type { SchedulingScene, SchedulingSceneElement } from '../canvas/sceneTypes'
import { groupScheduleRows } from '../model/schedule'
import type { ScheduleFixture } from '../model/schedule'
import type { SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import { createTimeScale } from '../time-scale/timeScale'
import type { SchedulingZoom } from '../time-scale/timeScale'
import { buildScheduleOperationPositions } from '../time-scale/timelineLayout'
import type { SchedulingLinkMode } from '../components/types'
import { buildDependencyRoute } from './dependencyRouting'

export interface BuildScheduleSceneOptions {
  fixture: ScheduleFixture
  width: number
  rowHeight: number
  zoom: SchedulingZoom
  dependencyMode: SchedulingLinkMode
  selectedOperationId?: string
  showCapacity: boolean
  showConflicts: boolean
  today: string
  previewById: Record<string, SchedulingPreviewWindow>
}

const labelWidth = 230
function shouldRenderDependency(
  mode: SchedulingLinkMode,
  selectedId: string | undefined,
  sourceId: string,
  targetId: string,
) {
  if (mode === 'none') {
    return false
  }

  if (mode === 'all') {
    return true
  }

  return selectedId === sourceId || selectedId === targetId
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
  const operationPositions = buildScheduleOperationPositions({
    fixture: options.fixture,
    rows,
    width: options.width,
    rowHeight: options.rowHeight,
    zoom: options.zoom,
    labelWidth,
    previewById: options.previewById,
  })
  const positionByOperationId = new Map(
    operationPositions.map((position) => [position.operation.id, position]),
  )

  for (const highlight of options.fixture.calendarHighlights) {
    const rowIndex = rows.findIndex((row) => row.id === highlight.resourceId)
    if (rowIndex < 0) {
      continue
    }

    const x = labelWidth + scale.dateToX(highlight.start)
    const endX = labelWidth + scale.dateToX(highlight.end)
    const fillByKind = {
      'working-time': 'rgba(34, 197, 94, 0.08)',
      maintenance: 'rgba(245, 158, 11, 0.13)',
      downtime: 'rgba(220, 38, 38, 0.12)',
      changeover: 'rgba(14, 165, 233, 0.1)',
    } satisfies Record<typeof highlight.kind, string>

    elements.push({
      id: highlight.id,
      kind: 'calendar-highlight',
      x,
      y: rowIndex * options.rowHeight + 2,
      width: Math.max(endX - x, 8),
      height: options.rowHeight - 5,
      fill: fillByKind[highlight.kind],
      severity: highlight.severity,
      metadata: {
        resourceId: highlight.resourceId,
        label: highlight.label,
        kind: highlight.kind,
      },
    })
  }

  rows.forEach((row, index) => {
    if (index === rows.length - 1) {
      return
    }

    const y = index * options.rowHeight
    elements.push({
      id: `resource-line-${row.id}`,
      kind: 'grid-line',
      x: 0,
      y: y + options.rowHeight - 1,
      width: options.width,
      height: 1,
      fill: '#e2e8f0',
    })
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

  for (const dependency of options.fixture.dependencies) {
    if (
      !shouldRenderDependency(
        options.dependencyMode,
        options.selectedOperationId,
        dependency.sourceOperationId,
        dependency.targetOperationId,
      )
    ) {
      continue
    }

      const source = positionByOperationId.get(dependency.sourceOperationId)
      const target = positionByOperationId.get(dependency.targetOperationId)
      if (!source || !target) {
        continue
      }

      elements.push({
        id: dependency.id,
        kind: 'dependency',
        x: 0,
        y: 0,
        stroke: '#64748b',
        points: buildDependencyRoute({
          source: { left: source.left, top: source.top, width: source.width, height: source.height },
          target: { left: target.left, top: target.top, width: target.width, height: target.height },
          type: dependency.type,
        }),
        metadata: {
          sourceOperationId: dependency.sourceOperationId,
          targetOperationId: dependency.targetOperationId,
          type: dependency.type,
        },
      })
  }

  if (options.showConflicts) {
    for (const conflict of options.fixture.conflicts) {
      const operation = options.fixture.operations.find((item) => item.id === conflict.targetId)
      const previewResourceId = operation ? options.previewById[operation.id]?.resourceId : undefined
      const rowIndex = operation
        ? rows.findIndex((row) => row.id === (previewResourceId ?? operation.resourceId))
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
