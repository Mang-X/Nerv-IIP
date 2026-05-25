import { describe, expect, it } from 'vitest'

import { createMockGanttFixture } from '../model/fixtures'
import { flattenGanttTasks } from '../model/gantt'

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
