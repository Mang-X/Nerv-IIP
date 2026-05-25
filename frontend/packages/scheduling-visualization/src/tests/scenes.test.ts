import { describe, expect, it } from 'vitest'

import { createMockGanttFixture, createMockScheduleFixture } from '../model/fixtures'
import { buildGanttScene } from '../renderers/buildGanttScene'
import { buildScheduleScene } from '../renderers/buildScheduleScene'

describe('scene builders', () => {
  it('builds Gantt non-interactive canvas layers without duplicating DOM task bars', () => {
    const fixture = createMockGanttFixture()
    const scene = buildGanttScene({
      fixture,
      expandedTaskIds: new Set(['phase-engineering']),
      width: 960,
      rowHeight: 36,
      zoom: 'day',
      showDependencies: true,
      showBaselines: true,
      showConflicts: true,
      today: '2026-05-06T00:00:00.000Z',
      previewById: {},
    })

    expect(scene.elements.some((element) => element.kind === 'bar')).toBe(false)
    expect(scene.elements.some((element) => element.kind === 'progress')).toBe(false)
    expect(scene.elements.some((element) => element.kind === 'milestone')).toBe(false)
    expect(scene.elements.some((element) => element.kind === 'baseline')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'dependency')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'conflict')).toBe(true)
    expect(scene.height).toBeGreaterThan(100)
  })

  it('builds schedule non-interactive canvas layers without duplicating DOM operation bars', () => {
    const fixture = createMockScheduleFixture()
    const scene = buildScheduleScene({
      fixture,
      width: 960,
      rowHeight: 44,
      zoom: 'day',
      showCapacity: true,
      showConflicts: true,
      today: '2026-05-06T00:00:00.000Z',
      previewById: {},
    })

    expect(scene.elements.some((element) => element.kind === 'bar')).toBe(false)
    expect(scene.elements.some((element) => element.kind === 'progress')).toBe(false)
    expect(scene.elements.some((element) => element.kind === 'capacity')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'conflict')).toBe(true)
  })
})
