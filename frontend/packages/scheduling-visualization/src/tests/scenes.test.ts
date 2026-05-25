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
      dependencyMode: 'all',
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

  it('filters Gantt dependencies to the selected task chain', () => {
    const fixture = createMockGanttFixture()
    fixture.tasks.push({
      id: 'milestone-release-approved',
      name: 'Release approved',
      code: 'REL-OK',
      start: '2026-05-12T00:00:00.000Z',
      end: '2026-05-12T00:00:00.000Z',
      progress: 0,
      status: 'planned',
      isMilestone: true,
    })
    fixture.dependencies.push({
      id: 'dep-routing-approved',
      sourceTaskId: 'task-routing-review',
      targetTaskId: 'milestone-release-approved',
      type: 'finish-start',
    })
    const scene = buildGanttScene({
      fixture,
      expandedTaskIds: new Set(['phase-engineering']),
      width: 960,
      rowHeight: 36,
      zoom: 'day',
      dependencyMode: 'selection',
      selectedTaskId: 'task-ebom-release',
      showBaselines: true,
      showConflicts: true,
      today: '2026-05-06T00:00:00.000Z',
      previewById: {},
    })

    expect(scene.elements.filter((element) => element.kind === 'dependency').map((element) => element.id))
      .toEqual(['dep-ebom-routing', 'dep-routing-approved'])
  })

  it('filters schedule dependencies to the selected operation chain', () => {
    const fixture = createMockScheduleFixture()
    const scene = buildScheduleScene({
      fixture,
      width: 960,
      rowHeight: 44,
      zoom: 'day',
      dependencyMode: 'selection',
      selectedOperationId: 'op-packing-1001',
      showCapacity: true,
      showConflicts: true,
      today: '2026-05-06T00:00:00.000Z',
      previewById: {},
    })

    expect(scene.elements.filter((element) => element.kind === 'dependency').map((element) => element.id))
      .toEqual(['dep-mix-pack-1001', 'dep-pack-sequence'])
  })

  it('builds schedule non-interactive canvas layers without duplicating DOM operation bars', () => {
    const fixture = createMockScheduleFixture()
    const scene = buildScheduleScene({
      fixture,
      width: 960,
      rowHeight: 44,
      zoom: 'day',
      dependencyMode: 'all',
      showCapacity: true,
      showConflicts: true,
      today: '2026-05-06T00:00:00.000Z',
      previewById: {},
    })

    expect(scene.elements.some((element) => element.kind === 'bar')).toBe(false)
    expect(scene.elements.some((element) => element.kind === 'progress')).toBe(false)
    expect(scene.elements.some((element) => element.kind === 'capacity')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'calendar-highlight')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'dependency')).toBe(true)
    expect(scene.elements.some((element) => element.kind === 'conflict')).toBe(true)
  })

  it('hides schedule dependencies when link mode is none', () => {
    const fixture = createMockScheduleFixture()
    const scene = buildScheduleScene({
      fixture,
      width: 960,
      rowHeight: 44,
      zoom: 'day',
      dependencyMode: 'none',
      showCapacity: true,
      showConflicts: true,
      today: '2026-05-06T00:00:00.000Z',
      previewById: {},
    })

    expect(scene.elements.some((element) => element.kind === 'dependency')).toBe(false)
  })
})
