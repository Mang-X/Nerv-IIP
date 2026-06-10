import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it } from 'vitest'
import { toModel } from '../model/aps-mapper'
import { samplePlan } from '../model/fixtures'
import SchedulingWorkbench from './SchedulingWorkbench.vue'

async function settle() {
  await flushPromises()
  await nextTick()
  await flushPromises()
}

describe('SchedulingWorkbench', () => {
  it('renders the order gantt view', async () => {
    const wrapper = mount(SchedulingWorkbench, {
      props: { model: toModel(samplePlan), engineKind: 'native', defaultView: 'order' },
      attachTo: document.body,
    })
    await settle()
    expect(wrapper.find('[data-view="order"]').exists()).toBe(true)
    // 两个视图切换标签都在
    const tabLabels = wrapper.findAll('[role="tab"]').map((t) => t.text())
    expect(tabLabels.some((t) => t.includes('工单甘特'))).toBe(true)
    expect(tabLabels.some((t) => t.includes('资源排产板'))).toBe(true)
    wrapper.unmount()
  })

  it('renders the resource board view', async () => {
    const wrapper = mount(SchedulingWorkbench, {
      props: { model: toModel(samplePlan), engineKind: 'native', defaultView: 'resource' },
      attachTo: document.body,
    })
    await settle()
    expect(wrapper.find('[data-view="resource"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('surfaces conflicts in business language in the side panel', async () => {
    const wrapper = mount(SchedulingWorkbench, {
      props: { model: toModel(samplePlan), engineKind: 'native' },
      attachTo: document.body,
    })
    await settle()
    expect(wrapper.text()).toContain('产能不足')
    wrapper.unmount()
  })
})
