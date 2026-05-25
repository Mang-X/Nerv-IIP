import type { GanttFixture, GanttRow } from '../model/gantt'
import type { ScheduleFixture, ScheduleOperation, ScheduleRow } from '../model/schedule'
import type { SchedulingPreviewWindow } from '../state/useSchedulingCommands'
import type { SchedulingZoom, TimeScaleTick } from './timeScale'
import { createTimeScale } from './timeScale'

export interface TimelineTick extends TimeScaleTick {
  x: number
}

export interface BuildTimelineTickOptions {
  start: string
  end: string
  width: number
  labelWidth: number
  zoom: SchedulingZoom
}

export interface GanttBarPosition {
  task: GanttRow
  top: number
  left: number
  width: number
}

export interface ScheduleOperationPosition {
  operation: ScheduleOperation
  resourceId: string
  top: number
  left: number
  width: number
  height: number
}

const dayInMilliseconds = 24 * 60 * 60 * 1000

const pixelsPerDayByZoom: Record<SchedulingZoom, number> = {
  day: 64,
  week: 44,
  month: 36,
}

function windowFor(
  id: string,
  fallback: SchedulingPreviewWindow,
  previewById: Record<string, SchedulingPreviewWindow>,
) {
  return previewById[id] ?? fallback
}

export function calculateTimelineContentWidth(options: {
  start: string
  end: string
  zoom: SchedulingZoom
  labelWidth: number
  minWidth: number
}) {
  const start = new Date(options.start).getTime()
  const end = new Date(options.end).getTime()
  const dayCount = Math.max(Math.ceil((end - start) / dayInMilliseconds) + 1, 1)
  const scaledWidth = options.labelWidth + dayCount * pixelsPerDayByZoom[options.zoom]

  return Math.max(options.minWidth, scaledWidth)
}

export function buildTimelineTicks(options: BuildTimelineTickOptions): TimelineTick[] {
  const scale = createTimeScale({
    start: options.start,
    end: options.end,
    width: options.width,
    zoom: options.zoom,
  })

  const minimumLabelGap = options.zoom === 'day' ? 48 : 58
  const ticks = scale.ticks.map((tick) => ({
    ...tick,
    x: options.labelWidth + tick.x,
  }))

  const visibleTicks: TimelineTick[] = []
  ticks.forEach((tick, index) => {
    const isFirstTick = index === 0
    const isLastTick = index === ticks.length - 1
    const previousVisible = visibleTicks.at(-1)
    if (isLastTick && previousVisible && tick.x - previousVisible.x < minimumLabelGap) {
      visibleTicks[visibleTicks.length - 1] = tick
      return
    }

    if (isFirstTick || !previousVisible || tick.x - previousVisible.x >= minimumLabelGap) {
      visibleTicks.push(tick)
    }
  })

  return visibleTicks
}

export function buildGanttBarPositions(options: {
  fixture: GanttFixture
  rows: GanttRow[]
  width: number
  rowHeight: number
  zoom: SchedulingZoom
  labelWidth: number
  previewById: Record<string, SchedulingPreviewWindow>
}): GanttBarPosition[] {
  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.width - options.labelWidth,
    zoom: options.zoom,
  })

  return options.rows.map((row, index) => {
    const window = windowFor(row.id, { start: row.start, end: row.end }, options.previewById)
    const left = options.labelWidth + scale.dateToX(window.start)
    const endX = options.labelWidth + scale.dateToX(window.end)

    return {
      task: row,
      top: index * options.rowHeight + 7,
      left,
      width: Math.max(row.isMilestone ? 16 : endX - left, row.isMilestone ? 16 : 32),
    }
  })
}

export function buildScheduleOperationPositions(options: {
  fixture: ScheduleFixture
  rows: ScheduleRow[]
  width: number
  rowHeight: number
  zoom: SchedulingZoom
  labelWidth: number
  previewById: Record<string, SchedulingPreviewWindow>
}): ScheduleOperationPosition[] {
  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.width - options.labelWidth,
    zoom: options.zoom,
  })
  const rowIndexByResourceId = new Map(options.rows.map((row, index) => [row.id, index]))
  const height = Math.max(Math.min(options.rowHeight - 16, 36), 24)

  return options.fixture.operations.flatMap((operation) => {
    const window = windowFor(
      operation.id,
      { start: operation.start, end: operation.end, resourceId: operation.resourceId },
      options.previewById,
    )
    const resourceId = window.resourceId ?? operation.resourceId
    const rowIndex = rowIndexByResourceId.get(resourceId)
    if (rowIndex === undefined) {
      return []
    }

    const left = options.labelWidth + scale.dateToX(window.start)
    const endX = options.labelWidth + scale.dateToX(window.end)

    return [{
      operation,
      resourceId,
      top: rowIndex * options.rowHeight + Math.round((options.rowHeight - height) / 2),
      left,
      width: Math.max(endX - left, 72),
      height,
    }]
  })
}

export function shiftWindowByPixels(options: {
  start: string
  end: string
  deltaX: number
  rangeStart: string
  rangeEnd: string
  width: number
  zoom: SchedulingZoom
}) {
  const scale = createTimeScale({
    start: options.rangeStart,
    end: options.rangeEnd,
    width: options.width,
    zoom: options.zoom,
  })
  const startX = scale.dateToX(options.start)
  const endX = scale.dateToX(options.end)

  return {
    start: scale.xToDate(startX + options.deltaX).toISOString(),
    end: scale.xToDate(endX + options.deltaX).toISOString(),
  }
}
