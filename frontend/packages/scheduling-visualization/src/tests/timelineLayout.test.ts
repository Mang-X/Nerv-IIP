import { describe, expect, it } from 'vitest'

import {
  buildGanttBarPositions,
  buildScheduleOperationPositions,
  buildTimelineTicks,
  calculateTimelineContentWidth,
  shiftWindowByPixels,
} from '../time-scale/timelineLayout'
import { createMockGanttFixture, createMockScheduleFixture } from '../model/fixtures'
import { flattenGanttTasks } from '../model/gantt'
import { groupScheduleRows } from '../model/schedule'

describe('timelineLayout', () => {
  it('builds timeline ticks with label offset from the chart label column', () => {
    const ticks = buildTimelineTicks({
      start: '2026-05-01T00:00:00.000Z',
      end: '2026-05-04T00:00:00.000Z',
      width: 600,
      labelWidth: 220,
      zoom: 'day',
    })

    expect(ticks[0]).toEqual(expect.objectContaining({ x: 220, label: 'May 1' }))
    expect(ticks.at(-1)?.x).toBe(820)
  })

  it('builds gantt bar positions for visible task rows', () => {
    const fixture = createMockGanttFixture()
    const rows = flattenGanttTasks(fixture.tasks, new Set(['phase-engineering']))
    const positions = buildGanttBarPositions({
      fixture,
      rows,
      width: 960,
      rowHeight: 44,
      zoom: 'day',
      labelWidth: 220,
      previewById: {},
    })

    expect(positions.map((position) => position.task.id)).toContain('task-routing-review')
    expect(positions.find((position) => position.task.id === 'task-routing-review')).toEqual(
      expect.objectContaining({ top: 95 }),
    )
  })

  it('builds schedule operation positions for resource rows', () => {
    const fixture = createMockScheduleFixture()
    const rows = groupScheduleRows(fixture.resources, fixture.operations)
    const positions = buildScheduleOperationPositions({
      fixture,
      rows,
      width: 960,
      rowHeight: 52,
      zoom: 'day',
      labelWidth: 230,
      previewById: {},
    })

    expect(positions.map((position) => position.operation.id)).toContain('op-packing-1001')
    expect(positions.find((position) => position.operation.id === 'op-packing-1001')?.width)
      .toBeGreaterThan(24)
  })

  it('keeps schedule operation blocks within their resource rows', () => {
    const fixture = createMockScheduleFixture()
    const rows = groupScheduleRows(fixture.resources, fixture.operations)
    const rowHeight = 52
    const positions = buildScheduleOperationPositions({
      fixture,
      rows,
      width: 960,
      rowHeight,
      zoom: 'day',
      labelWidth: 230,
      previewById: {},
    })

    for (const position of positions) {
      const rowTop = Math.floor(position.top / rowHeight) * rowHeight
      expect(position.top).toBeGreaterThanOrEqual(rowTop)
      expect(position.top + position.height).toBeLessThanOrEqual(rowTop + rowHeight)
      expect(position.resourceId).toBe(position.operation.resourceId)
    }
  })

  it('uses preview resource ids for schedule operation positions', () => {
    const fixture = createMockScheduleFixture()
    const rows = groupScheduleRows(fixture.resources, fixture.operations)
    const position = buildScheduleOperationPositions({
      fixture,
      rows,
      width: 960,
      rowHeight: 52,
      zoom: 'day',
      labelWidth: 230,
      previewById: {
        'op-packing-1001': {
          start: '2026-05-06T08:00:00.000Z',
          end: '2026-05-06T10:00:00.000Z',
          resourceId: 'wc-mix-02',
        },
      },
    }).find((item) => item.operation.id === 'op-packing-1001')

    expect(position).toEqual(expect.objectContaining({
      resourceId: 'wc-mix-02',
      top: 60,
    }))
  })

  it('expands timeline content width by zoom so bar positions stay linked to scale changes', () => {
    const range = {
      start: '2026-05-01T00:00:00.000Z',
      end: '2026-05-22T00:00:00.000Z',
      labelWidth: 220,
      minWidth: 960,
    }

    const dayWidth = calculateTimelineContentWidth({ ...range, zoom: 'day' })
    const weekWidth = calculateTimelineContentWidth({ ...range, zoom: 'week' })
    const monthWidth = calculateTimelineContentWidth({ ...range, zoom: 'month' })

    expect(dayWidth).toBeGreaterThan(weekWidth)
    expect(weekWidth).toBeGreaterThan(monthWidth)
    expect(monthWidth).toBeGreaterThanOrEqual(960)
  })

  it('keeps dragged windows at their original duration when clamped at the range edge', () => {
    const shifted = shiftWindowByPixels({
      start: '2026-05-04T00:00:00.000Z',
      end: '2026-05-08T00:00:00.000Z',
      deltaX: 2000,
      rangeStart: '2026-05-01T00:00:00.000Z',
      rangeEnd: '2026-05-22T00:00:00.000Z',
      width: 1344,
      zoom: 'day',
    })

    const durationDays = (
      new Date(shifted.end).getTime() - new Date(shifted.start).getTime()
    ) / (24 * 60 * 60 * 1000)

    expect(shifted.end).toBe('2026-05-22T00:00:00.000Z')
    expect(durationDays).toBe(4)
  })
})
