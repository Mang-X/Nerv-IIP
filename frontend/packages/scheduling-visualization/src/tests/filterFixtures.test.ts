import { describe, expect, it } from 'vitest'

import { filterGanttFixture, filterScheduleFixture } from '../state/filterFixtures'
import { createMockGanttFixture, createMockScheduleFixture } from '../model/fixtures'

describe('filterFixtures', () => {
  it('filters gantt tasks by task name and keeps matching ancestors', () => {
    const fixture = filterGanttFixture(createMockGanttFixture(), 'routing')

    expect(fixture.tasks).toHaveLength(1)
    expect(fixture.tasks[0]?.id).toBe('phase-engineering')
    expect(fixture.tasks[0]?.children?.map((task) => task.id)).toEqual(['task-routing-review'])
    expect(fixture.conflicts.map((conflict) => conflict.id)).toEqual(['conflict-routing-capacity'])
  })

  it('filters schedule resources and operations by work order or resource code', () => {
    const fixture = filterScheduleFixture(createMockScheduleFixture(), 'wo-1002')

    expect(fixture.resources.map((resource) => resource.id)).toEqual(['wc-pack-01'])
    expect(fixture.operations.map((operation) => operation.id)).toEqual(['op-packing-1002'])
    expect(fixture.capacityBands.map((band) => band.id)).toEqual(['cap-pack-morning'])
    expect(fixture.conflicts.map((conflict) => conflict.id)).toEqual(['conflict-pack-overload'])
  })
})

