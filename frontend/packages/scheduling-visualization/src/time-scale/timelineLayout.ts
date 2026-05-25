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
  top: number
  left: number
  width: number
}

function windowFor(
  id: string,
  fallback: SchedulingPreviewWindow,
  previewById: Record<string, SchedulingPreviewWindow>,
) {
  return previewById[id] ?? fallback
}

export function buildTimelineTicks(options: BuildTimelineTickOptions): TimelineTick[] {
  const scale = createTimeScale({
    start: options.start,
    end: options.end,
    width: options.width,
    zoom: options.zoom,
  })

  return scale.ticks.map((tick) => ({
    ...tick,
    x: options.labelWidth + tick.x,
  }))
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

  return options.fixture.operations.flatMap((operation) => {
    const rowIndex = rowIndexByResourceId.get(operation.resourceId)
    if (rowIndex === undefined) {
      return []
    }

    const window = windowFor(
      operation.id,
      { start: operation.start, end: operation.end },
      options.previewById,
    )
    const left = options.labelWidth + scale.dateToX(window.start)
    const endX = options.labelWidth + scale.dateToX(window.end)

    return [{
      operation,
      top: rowIndex * options.rowHeight + 10,
      left,
      width: Math.max(endX - left, 72),
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

