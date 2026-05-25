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
})
