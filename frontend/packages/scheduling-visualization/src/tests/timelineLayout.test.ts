import { describe, expect, it } from 'vitest'

import {
  buildGanttBarPositions,
  buildScheduleOperationPositions,
  buildTimelineTicks,
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
})
