import type { GanttFixture, GanttRow } from '../model/gantt'
import type { ScheduleCalendarHighlight, ScheduleFixture, ScheduleOperation, ScheduleRow } from '../model/schedule'
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
  lane: number
  laneCount: number
  hasVisualOverlap: boolean
  hasTimeOverlap: boolean
}

export interface ScheduleCalendarHighlightPosition {
  highlight: ScheduleCalendarHighlight
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

export function calculateTimelineScaleWidth(options: {
  start: string
  end: string
  zoom: SchedulingZoom
}) {
  const start = new Date(options.start).getTime()
  const end = new Date(options.end).getTime()
  const dayCount = Math.max(Math.ceil((end - start) / dayInMilliseconds) + 1, 1)

  return dayCount * pixelsPerDayByZoom[options.zoom]
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
  const scaledWidth = options.labelWidth + calculateTimelineScaleWidth(options)

  return Math.max(options.minWidth, scaledWidth)
}

export function buildTimelineTicks(options: BuildTimelineTickOptions): TimelineTick[] {
  const scale = createTimeScale({
    start: options.start,
    end: options.end,
    width: options.width,
    zoom: options.zoom,
  })

  const minimumLabelGap = options.zoom === 'day' ? 72 : options.zoom === 'week' ? 76 : 116
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

function rangesOverlap(start: string, end: string, otherStart: string, otherEnd: string) {
  return new Date(start).getTime() < new Date(otherEnd).getTime()
    && new Date(end).getTime() > new Date(otherStart).getTime()
}

function canRunInParallel(operation: ScheduleOperation, otherOperation: ScheduleOperation) {
  return operation.resourceUsageMode === 'parallel-capacity'
    && otherOperation.resourceUsageMode === 'parallel-capacity'
    && Boolean(operation.parallelGroupId)
    && operation.parallelGroupId === otherOperation.parallelGroupId
}

export function buildGanttBarPositions(options: {
  fixture: GanttFixture
  rows: GanttRow[]
  width: number
  rowHeight: number
  zoom: SchedulingZoom
  labelWidth: number
  scaleWidth?: number
  previewById: Record<string, SchedulingPreviewWindow>
}): GanttBarPosition[] {
  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.scaleWidth ?? options.width - options.labelWidth,
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
  scaleWidth?: number
  previewById: Record<string, SchedulingPreviewWindow>
}): ScheduleOperationPosition[] {
  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.scaleWidth ?? options.width - options.labelWidth,
    zoom: options.zoom,
  })
  const rowIndexByResourceId = new Map(options.rows.map((row, index) => [row.id, index]))
  const height = Math.max(Math.min(options.rowHeight - 16, 36), 24)
  const minOperationWidth = 36

  const positioned = options.fixture.operations.flatMap((operation) => {
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
      rowIndex,
      window,
      top: rowIndex * options.rowHeight + Math.round((options.rowHeight - height) / 2),
      left,
      width: Math.max(endX - left, minOperationWidth),
      height,
      lane: 0,
      laneCount: 1,
      hasVisualOverlap: false,
      hasTimeOverlap: false,
    }]
  })

  const byResource = new Map<string, typeof positioned>()
  for (const position of positioned) {
    byResource.set(position.resourceId, [...(byResource.get(position.resourceId) ?? []), position])
  }

  for (const resourcePositions of byResource.values()) {
    const ordered = resourcePositions.sort((a, b) => a.left - b.left)

    for (const position of ordered) {
      const visualEnd = position.left + position.width
      position.hasVisualOverlap = ordered.some((other) =>
        other !== position
        && position.left < other.left + other.width
        && visualEnd > other.left,
      )
      position.hasTimeOverlap = ordered.some((other) =>
        other !== position
        && rangesOverlap(position.window.start, position.window.end, other.window.start, other.window.end),
      )
    }

    const parallelPositions = ordered.filter((position) =>
      ordered.some((other) =>
        other !== position
        && rangesOverlap(position.window.start, position.window.end, other.window.start, other.window.end)
        && canRunInParallel(position.operation, other.operation),
      ),
    )
    const laneEnds: number[] = []
    for (const position of parallelPositions) {
      const visualEnd = position.left + position.width
      const lane = laneEnds.findIndex((end) => position.left >= end + 6)
      position.lane = lane >= 0 ? lane : laneEnds.length
      laneEnds[position.lane] = visualEnd
    }

    const laneCount = Math.max(laneEnds.length, 1)
    const laneGap = 3
    const availableHeight = Math.max(options.rowHeight - 12, 24)
    const stackedHeight = Math.max(
      Math.floor((availableHeight - laneGap * (laneCount - 1)) / laneCount),
      10,
    )
    const rowTop = ordered[0]?.rowIndex ? ordered[0].rowIndex * options.rowHeight : 0

    for (const position of ordered) {
      const usesParallelLane = parallelPositions.includes(position) && laneCount > 1
      const effectiveHeight = usesParallelLane ? stackedHeight : height
      position.laneCount = usesParallelLane ? laneCount : 1
      position.height = effectiveHeight
      position.top = rowTop + Math.round((options.rowHeight - availableHeight) / 2)
        + (usesParallelLane ? position.lane * (effectiveHeight + laneGap) : Math.round((availableHeight - height) / 2))
    }
  }

  return positioned.map(({ rowIndex: _rowIndex, window: _window, ...position }) => position)
}

export function buildScheduleCalendarHighlightPositions(options: {
  fixture: ScheduleFixture
  rows: ScheduleRow[]
  width: number
  rowHeight: number
  zoom: SchedulingZoom
  labelWidth: number
  scaleWidth?: number
}): ScheduleCalendarHighlightPosition[] {
  const scale = createTimeScale({
    start: options.fixture.rangeStart,
    end: options.fixture.rangeEnd,
    width: options.scaleWidth ?? options.width - options.labelWidth,
    zoom: options.zoom,
  })
  const rowIndexByResourceId = new Map(options.rows.map((row, index) => [row.id, index]))

  return options.fixture.calendarHighlights.flatMap((highlight) => {
    const rowIndex = rowIndexByResourceId.get(highlight.resourceId)
    if (rowIndex === undefined) {
      return []
    }

    const left = options.labelWidth + scale.dateToX(highlight.start)
    const endX = options.labelWidth + scale.dateToX(highlight.end)

    return [{
      highlight,
      resourceId: highlight.resourceId,
      top: rowIndex * options.rowHeight + 3,
      left,
      width: Math.max(endX - left, 12),
      height: options.rowHeight - 7,
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
  scaleWidth?: number
}) {
  const width = options.scaleWidth ?? options.width
  const scale = createTimeScale({
    start: options.rangeStart,
    end: options.rangeEnd,
    width,
    zoom: options.zoom,
  })
  const startX = scale.dateToX(options.start)
  const endX = scale.dateToX(options.end)
  const clampedDeltaX = Math.min(Math.max(options.deltaX, -startX), width - endX)

  return {
    start: scale.xToDate(startX + clampedDeltaX).toISOString(),
    end: scale.xToDate(endX + clampedDeltaX).toISOString(),
  }
}
