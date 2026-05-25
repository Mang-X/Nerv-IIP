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
})
