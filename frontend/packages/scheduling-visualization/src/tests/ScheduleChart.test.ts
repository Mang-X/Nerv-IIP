import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import ScheduleChart from '../components/ScheduleChart.vue'
import { createMockScheduleFixture } from '../model/fixtures'

vi.mock('../canvas/createLeaferSurface', () => ({
  createLeaferSurface: () => ({
    clear: vi.fn(),
    addRect: vi.fn(),
    addText: vi.fn(),
    addPath: vi.fn(),
    dispose: vi.fn(),
  }),
}))

function dispatchPointer(target: Element, type: string, clientX: number, clientY = 20) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'clientX', { value: clientX })
  Object.defineProperty(event, 'clientY', { value: clientY })
  Object.defineProperty(event, 'pointerId', { value: 1 })
  target.dispatchEvent(event)
}

describe('ScheduleChart', () => {
  it('renders operations and emits selection from operation buttons', async () => {
    const wrapper = mount(ScheduleChart, {
      props: {
        fixture: createMockScheduleFixture(),
      },
    })

    expect(wrapper.find('[data-test="schedule-chart"]').exists()).toBe(true)
    await wrapper.get('[data-test="schedule-operation-op-packing-1001"]').trigger('click')

    expect(wrapper.emitted('select')?.[0]).toEqual([
      { kind: 'operation', id: 'op-packing-1001' },
    ])
  })

  it('renders a time axis and filters operations by query', () => {
    const wrapper = mount(ScheduleChart, {
      props: {
        fixture: createMockScheduleFixture(),
        query: 'wo-1002',
      },
    })

    expect(wrapper.find('[data-test="timeline-axis"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="schedule-operation-op-packing-1002"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="schedule-operation-op-packing-1001"]').exists()).toBe(false)
  })

  it('emits a preview command when an operation bar is dragged', async () => {
    const wrapper = mount(ScheduleChart, {
      attachTo: document.body,
      props: {
        fixture: createMockScheduleFixture(),
      },
    })

    const operation = wrapper.get('[data-test="schedule-operation-op-packing-1001"]')
    dispatchPointer(operation.element, 'pointerdown', 100)
    dispatchPointer(operation.element, 'pointermove', 136)
    dispatchPointer(operation.element, 'pointerup', 136)
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('previewCommand')?.[0]?.[0]).toEqual(
      expect.objectContaining({
        targetId: 'op-packing-1001',
        kind: 'move',
      }),
    )
  })

  it('shows a live preview while dragging an operation', async () => {
    const wrapper = mount(ScheduleChart, {
      attachTo: document.body,
      props: {
        fixture: createMockScheduleFixture(),
      },
    })

    const operation = wrapper.get('[data-test="schedule-operation-op-packing-1001"]')
    dispatchPointer(operation.element, 'pointerdown', 100, 24)
    dispatchPointer(operation.element, 'pointermove', 136, 24)
    await wrapper.vm.$nextTick()

    const previewOperation = wrapper.get('[data-test="schedule-operation-op-packing-1001"]')
    expect(previewOperation.classes()).toContain('schedule-operation--dragging')
    expect(previewOperation.attributes('data-preview-resource-id')).toBe('wc-pack-01')
  })

  it('emits a cross-resource preview command when an operation is dragged to another row', async () => {
    const wrapper = mount(ScheduleChart, {
      attachTo: document.body,
      props: {
        fixture: createMockScheduleFixture(),
      },
    })

    const scrollPlane = wrapper.get('[data-test="schedule-scroll-plane"]')
    scrollPlane.element.getBoundingClientRect = () => ({
      x: 0,
      y: 0,
      top: 0,
      left: 0,
      right: 960,
      bottom: 104,
      width: 960,
      height: 104,
      toJSON: () => ({}),
    } as DOMRect)

    const operation = wrapper.get('[data-test="schedule-operation-op-packing-1001"]')
    dispatchPointer(operation.element, 'pointerdown', 100, 24)
    dispatchPointer(operation.element, 'pointermove', 120, 76)
    dispatchPointer(operation.element, 'pointerup', 120, 76)
    await wrapper.vm.$nextTick()

    expect(wrapper.emitted('previewCommand')?.[0]?.[0]).toEqual(
      expect.objectContaining({
        targetId: 'op-packing-1001',
        after: expect.objectContaining({
          resourceId: 'wc-mix-02',
        }),
      }),
    )
  })

  it('keeps the resource label column frozen while the timeline scrolls horizontally', async () => {
    const wrapper = mount(ScheduleChart, {
      attachTo: document.body,
      props: {
        fixture: createMockScheduleFixture(),
      },
    })

    const viewport = wrapper.get('[data-test="schedule-viewport"]')
    Object.defineProperty(viewport.element, 'scrollLeft', { configurable: true, value: 160 })
    await viewport.trigger('scroll')
    await wrapper.vm.$nextTick()

    expect(wrapper.get('[data-test="schedule-resource-wc-pack-01"]').attributes('style'))
      .toContain('left: 160px')
  })

  it('resets horizontal scroll state when zoom changes', async () => {
    const wrapper = mount(ScheduleChart, {
      attachTo: document.body,
      props: {
        fixture: createMockScheduleFixture(),
        zoom: 'day',
      },
    })

    const viewport = wrapper.get('[data-test="schedule-viewport"]')
    Object.defineProperty(viewport.element, 'scrollLeft', {
      configurable: true,
      writable: true,
      value: 160,
    })
    await viewport.trigger('scroll')
    await wrapper.setProps({ zoom: 'week' })
    await wrapper.vm.$nextTick()

    expect(wrapper.get('[data-test="schedule-resource-wc-pack-01"]').attributes('style'))
      .toContain('left: 0px')
  })
})
