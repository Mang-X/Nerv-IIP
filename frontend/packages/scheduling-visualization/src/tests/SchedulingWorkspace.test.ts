import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'

import SchedulingWorkspace from '../components/SchedulingWorkspace.vue'

vi.mock('../canvas/createLeaferSurface', () => ({
  createLeaferSurface: () => ({
    clear: vi.fn(),
    addRect: vi.fn(),
    addText: vi.fn(),
    addPath: vi.fn(),
    dispose: vi.fn(),
  }),
}))

describe('SchedulingWorkspace', () => {
  it('renders both modes and opens detail content after selection', async () => {
    const wrapper = mount(SchedulingWorkspace, {
      attachTo: document.body,
    })

    expect(wrapper.text()).toContain('Gantt')
    expect(wrapper.text()).toContain('Schedule')

    await wrapper.get('[data-test="gantt-row-task-routing-review"]').trigger('click')

    expect(document.body.textContent).toContain('Routing review')
  })
})
