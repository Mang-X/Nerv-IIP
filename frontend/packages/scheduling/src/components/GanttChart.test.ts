import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it } from 'vitest'
import { toModel } from '../model/aps-mapper'
import { samplePlan } from '../model/fixtures'
import GanttChart from './GanttChart.vue'
import ResourceSchedulerBoard from './ResourceSchedulerBoard.vue'

async function settle() {
  await flushPromises()
  await nextTick()
  await flushPromises()
}

describe('GanttChart', () => {
  it('renders one node per task via native engine', async () => {
    const model = toModel(samplePlan)
    const wrapper = mount(GanttChart, {
      props: { model, engineKind: 'native', scale: 'day' },
      attachTo: document.body,
    })
    await settle()
    const nodes = wrapper.element.querySelectorAll('[data-task-id]')
    expect(nodes.length).toBe(model.tasks.length)
    wrapper.unmount()
  })

  it('emits taskSelect when a bar is clicked', async () => {
    const model = toModel(samplePlan)
    const wrapper = mount(GanttChart, {
      props: { model, engineKind: 'native' },
      attachTo: document.body,
    })
    await settle()
    ;(wrapper.element.querySelector('[data-task-id="a1"]') as SVGGElement).dispatchEvent(
      new MouseEvent('click', { bubbles: true }),
    )
    expect(wrapper.emitted('taskSelect')?.[0]).toEqual(['a1'])
    wrapper.unmount()
  })

  it('shows loading skeleton when loading', () => {
    const wrapper = mount(GanttChart, { props: { loading: true, engineKind: 'native' } })
    expect(wrapper.find('[data-testid="gantt-skeleton"]').exists()).toBe(true)
  })
})

describe('ResourceSchedulerBoard', () => {
  it('renders operation nodes on resource rows via native engine', async () => {
    const model = toModel(samplePlan)
    const wrapper = mount(ResourceSchedulerBoard, {
      props: { model, engineKind: 'native', scale: 'day' },
      attachTo: document.body,
    })
    await settle()
    // 资源视图只渲染工序节点(order 分组不出条)。
    const opCount = model.tasks.filter((t) => t.type === 'operation').length
    expect(wrapper.element.querySelectorAll('[data-task-id]').length).toBe(opCount)
    wrapper.unmount()
  })
})
