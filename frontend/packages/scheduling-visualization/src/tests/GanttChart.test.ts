import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import GanttChart from '../components/GanttChart.vue'
import { createMockGanttFixture } from '../model/fixtures'

vi.mock('../canvas/createLeaferSurface', () => ({
  createLeaferSurface: () => ({
    clear: vi.fn(),
    addRect: vi.fn(),
    addText: vi.fn(),
    addPath: vi.fn(),
    dispose: vi.fn(),
  }),
}))

function dispatchPointer(target: Element, type: string, clientX: number) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'clientX', { value: clientX })
  Object.defineProperty(event, 'pointerId', { value: 1 })
  target.dispatchEvent(event)
}

function waitForFrame() {
  return new Promise<void>((resolve) => requestAnimationFrame(() => resolve()))
}

describe('GanttChart', () => {
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
})
