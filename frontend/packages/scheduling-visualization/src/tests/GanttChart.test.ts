import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import GanttChart from '../components/GanttChart.vue'
import { createLargeMockGanttFixture, createMockGanttFixture } from '../model/fixtures'

const surfaceSpies = vi.hoisted(() => ({
  clear: vi.fn(),
  addRect: vi.fn(),
  addText: vi.fn(),
  addPath: vi.fn(),
  flush: vi.fn(),
  dispose: vi.fn(),
}))

vi.mock('../canvas/createLeaferSurface', () => ({
  createLeaferSurface: () => ({
    clear: surfaceSpies.clear,
    addRect: surfaceSpies.addRect,
    addText: surfaceSpies.addText,
    addPath: surfaceSpies.addPath,
    flush: surfaceSpies.flush,
    dispose: surfaceSpies.dispose,
  }),
}))

function dispatchPointer(target: Element, type: string, clientX: number, clientY = 20) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'clientX', { value: clientX })
  Object.defineProperty(event, 'clientY', { value: clientY })
  Object.defineProperty(event, 'pointerId', { value: 1 })
  target.dispatchEvent(event)
}

function waitForFrame() {
  return new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
}

describe('GanttChart', () => {
  beforeEach(() => {
    surfaceSpies.clear.mockClear()
    surfaceSpies.addRect.mockClear()
    surfaceSpies.addText.mockClear()
    surfaceSpies.addPath.mockClear()
    surfaceSpies.flush.mockClear()
    surfaceSpies.dispose.mockClear()
  })

  it('renders rows and emits selection from row buttons', async () => {
    const wrapper = mount(GanttChart, {
      props: {
        fixture: createMockGanttFixture(),
        expandedTaskIds: ['phase-engineering'],
      },
    })

    expect(wrapper.find('[data-test="gantt-chart"]').exists()).toBe(true)
    await wrapper.get('[data-test="gantt-row-task-routing-review"]').trigger('click')

    expect(wrapper.emitted('select')?.[0]).toEqual([{ kind: 'task', id: 'task-routing-review' }])
  })

  it('renders a time axis and filters rows by query', () => {
    const wrapper = mount(GanttChart, {
      props: {
        fixture: createMockGanttFixture(),
        expandedTaskIds: ['phase-engineering'],
        query: 'routing',
      },
    })

    expect(wrapper.find('[data-test="timeline-axis"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="gantt-row-task-routing-review"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="gantt-row-task-ebom-release"]').exists()).toBe(false)
  })

  it('emits a preview command when a task bar is dragged', async () => {
    const wrapper = mount(GanttChart, {
      attachTo: document.body,
      props: {
        fixture: createMockGanttFixture(),
        expandedTaskIds: ['phase-engineering'],
      },
    })

    const bar = wrapper.get('[data-test="gantt-bar-task-routing-review"]')
    dispatchPointer(bar.element, 'pointerdown', 100)
    dispatchPointer(bar.element, 'pointermove', 140)
    dispatchPointer(bar.element, 'pointerup', 140)
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('previewCommand')?.[0]?.[0]).toEqual(
      expect.objectContaining({
        targetId: 'task-routing-review',
        kind: 'move',
      }),
    )
  })

  it('keeps the task label column frozen while the timeline scrolls horizontally', async () => {
    const wrapper = mount(GanttChart, {
      attachTo: document.body,
      props: {
        fixture: createMockGanttFixture(),
        expandedTaskIds: ['phase-engineering'],
      },
    })

    const viewport = wrapper.get('[data-test="gantt-viewport"]')
    Object.defineProperty(viewport.element, 'scrollLeft', { configurable: true, value: 180 })
    await viewport.trigger('scroll')
    await waitForFrame()
    await wrapper.vm.$nextTick()

    expect(wrapper.get('[data-test="gantt-row-task-routing-review"]').attributes('style'))
      .toContain('left: 180px')
  })

  it('redraws Leafer overlays as soon as the viewport scrolls', async () => {
    const wrapper = mount(GanttChart, {
      attachTo: document.body,
      props: {
        fixture: createMockGanttFixture(),
        expandedTaskIds: ['phase-engineering'],
      },
    })

    await waitForFrame()
    const clearsBeforeScroll = surfaceSpies.clear.mock.calls.length
    const viewport = wrapper.get('[data-test="gantt-viewport"]')
    Object.defineProperty(viewport.element, 'scrollLeft', { configurable: true, value: 180 })
    await viewport.trigger('scroll')
    await waitForFrame()

    expect(surfaceSpies.clear.mock.calls.length).toBeGreaterThan(clearsBeforeScroll)
    expect(surfaceSpies.flush.mock.calls.length).toBeGreaterThan(0)
  })

  it('resets horizontal scroll state when zoom changes', async () => {
    const wrapper = mount(GanttChart, {
      attachTo: document.body,
      props: {
        fixture: createMockGanttFixture(),
        expandedTaskIds: ['phase-engineering'],
        zoom: 'day',
      },
    })

    const viewport = wrapper.get('[data-test="gantt-viewport"]')
    Object.defineProperty(viewport.element, 'scrollLeft', {
      configurable: true,
      writable: true,
      value: 180,
    })
    await viewport.trigger('scroll')
    await waitForFrame()
    await wrapper.setProps({ zoom: 'week' })
    await wrapper.vm.$nextTick()

    expect(wrapper.get('[data-test="gantt-row-task-routing-review"]').attributes('style'))
      .toContain('left: 0px')
  })

  it('keeps large Gantt DOM bounded to visible rows and timeline ticks', async () => {
    const fixture = createLargeMockGanttFixture({
      taskCount: 1200,
      days: 730,
      dependencyEvery: 12,
    })

    const wrapper = mount(GanttChart, {
      attachTo: document.body,
      props: {
        fixture,
        expandedTaskIds: fixture.tasks.map((task) => task.id),
        maxViewportHeight: 260,
        width: 960,
      },
    })

    await waitForFrame()
    await wrapper.vm.$nextTick()

    expect(wrapper.findAll('[data-test^="gantt-row-"]').length).toBeLessThanOrEqual(12)
    expect(wrapper.findAll('[data-test^="gantt-bar-"]').length).toBeLessThanOrEqual(12)
    expect(wrapper.findAll('.timeline-axis__tick').length).toBeLessThan(40)
  }, 20000)

  it('shows a pointer-following tooltip for task bars', async () => {
    const wrapper = mount(GanttChart, {
      attachTo: document.body,
      props: {
        fixture: createMockGanttFixture(),
        expandedTaskIds: ['phase-engineering'],
      },
    })

    dispatchPointer(wrapper.get('[data-test="gantt-bar-task-routing-review"]').element, 'pointerenter', 320, 180)
    await wrapper.vm.$nextTick()

    expect(wrapper.get('[data-test="scheduling-pointer-tooltip"]').text()).toContain('ROUTE-REV')
  })
})
