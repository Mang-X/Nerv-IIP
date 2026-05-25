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

function dispatchPointer(target: Element, type: string, clientX: number) {
  const event = new Event(type, { bubbles: true, cancelable: true })
  Object.defineProperty(event, 'clientX', { value: clientX })
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
})
