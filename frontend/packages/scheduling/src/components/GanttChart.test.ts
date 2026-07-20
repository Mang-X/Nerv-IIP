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

// 无 DHTMLX vendor 时(CI/单测环境)不挂载任何引擎,只断言与引擎无关的容器与占位。
// 「一 task 一节点」+ selectTask 的引擎级覆盖移到 engine/conformance.selfcheck.test.ts。

describe('GanttChart', () => {
  it('shows loading skeleton when loading', () => {
    const wrapper = mount(GanttChart, { props: { loading: true } })
    expect(wrapper.find('[data-testid="gantt-skeleton"]').exists()).toBe(true)
  })

  it('mounts with a model without throwing and renders the order container', async () => {
    const model = toModel(samplePlan)
    const wrapper = mount(GanttChart, {
      props: { model, scale: 'day' },
      attachTo: document.body,
    })
    await settle()
    expect(wrapper.find('[data-view="order"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('shows the placeholder when no DHTMLX engine is available', async () => {
    const wrapper = mount(GanttChart, {
      props: { model: toModel(samplePlan) },
      attachTo: document.body,
    })
    await settle()
    expect(wrapper.text()).toContain('排程引擎未加载')
    wrapper.unmount()
  })

  it('renders the package read-only timeline when DHTMLX is unavailable', async () => {
    const wrapper = mount(GanttChart, {
      props: { model: toModel(samplePlan), scale: 'hour', readOnly: true },
      attachTo: document.body,
    })
    await settle()

    const timeline = wrapper.find('[data-testid="readonly-schedule-timeline"]')
    expect(timeline.exists()).toBe(true)
    expect(timeline.text()).toContain('WO-001')
    expect(timeline.text()).toContain('冲突')
    expect(timeline.text()).toContain('锁定')
    expect(wrapper.find('[data-testid="engine-unavailable"]').exists()).toBe(false)

    await timeline.find('[data-task-id="a2"]').trigger('click')
    expect(wrapper.emitted('taskSelect')).toEqual([['a2']])
    wrapper.unmount()
  })

  it('shows a clear empty state when the schedule has no tasks', async () => {
    const model = { ...toModel(samplePlan), tasks: [], links: [] }
    const wrapper = mount(GanttChart, {
      props: { model },
      attachTo: document.body,
    })
    await settle()
    expect(wrapper.find('[data-testid="gantt-empty"]').text()).toContain('暂无排程任务')
    wrapper.unmount()
  })
})

describe('ResourceSchedulerBoard', () => {
  it('mounts and renders the resource container', async () => {
    const wrapper = mount(ResourceSchedulerBoard, {
      props: { model: toModel(samplePlan), scale: 'day' },
      attachTo: document.body,
    })
    await settle()
    expect(wrapper.find('[data-view="resource"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('groups the read-only timeline into resource lanes', async () => {
    const wrapper = mount(ResourceSchedulerBoard, {
      props: { model: toModel(samplePlan), scale: 'day', readOnly: true },
      attachTo: document.body,
    })
    await settle()

    expect(wrapper.find('[data-testid="readonly-schedule-timeline"]').exists()).toBe(true)
    expect(wrapper.findAll('[data-resource-lane]')).toHaveLength(2)
    wrapper.unmount()
  })

  it('aligns day geometry to local calendar boundaries', async () => {
    const model = toModel(samplePlan)
    model.horizon = {
      startUtc: '2026-06-10T00:00:00.000Z',
      endUtc: '2026-06-10T23:00:00.000Z',
    }
    model.tasks = model.tasks.map((task) =>
      task.id === 'a1'
        ? {
            ...task,
            startUtc: '2026-06-10T16:00:00.000Z',
            endUtc: '2026-06-10T18:00:00.000Z',
          }
        : task,
    )

    const wrapper = mount(ResourceSchedulerBoard, {
      props: { model, scale: 'day', readOnly: true },
      attachTo: document.body,
    })
    await settle()

    const task = wrapper.find('[data-task-id="a1"]')
    const taskStart = Date.parse('2026-06-10T16:00:00.000Z')
    const rangeStart = new Date('2026-06-10T00:00:00.000Z')
    rangeStart.setHours(0, 0, 0, 0)
    const rangeEnd = new Date('2026-06-10T23:00:00.000Z')
    rangeEnd.setHours(24, 0, 0, 0)
    const expectedLeft =
      ((taskStart - rangeStart.getTime()) / (rangeEnd.getTime() - rangeStart.getTime())) * 100
    expect(Number.parseFloat((task.element as HTMLElement).style.left)).toBeCloseTo(expectedLeft, 5)
    wrapper.unmount()
  })

  it('keeps tasks outside the declared horizon inside the timeline track', async () => {
    const model = toModel(samplePlan)
    model.horizon = {
      startUtc: '2026-06-10T00:00:00.000Z',
      endUtc: '2026-06-11T00:00:00.000Z',
    }
    model.tasks = model.tasks.map((task) =>
      task.id === 'a1'
        ? {
            ...task,
            startUtc: '2026-06-08T08:00:00.000Z',
            endUtc: '2026-06-08T10:00:00.000Z',
          }
        : task,
    )

    const wrapper = mount(ResourceSchedulerBoard, {
      props: { model, scale: 'day', readOnly: true },
      attachTo: document.body,
    })
    await settle()

    for (const task of wrapper.findAll('[data-task-id]')) {
      const element = task.element as HTMLElement
      const left = Number.parseFloat(element.style.left)
      const width = Number.parseFloat(element.style.width)
      expect(left).toBeGreaterThanOrEqual(0)
      expect(left + width).toBeLessThanOrEqual(100)
    }
    wrapper.unmount()
  })

  it('uses the mapped task text in the read-only fallback', async () => {
    const model = toModel(samplePlan)
    model.tasks = model.tasks.map((task) =>
      task.id === 'a1' ? { ...task, text: 'WO-001 · 第 10 道 · 激光切割' } : task,
    )

    const wrapper = mount(ResourceSchedulerBoard, {
      props: { model, scale: 'day', readOnly: true },
      attachTo: document.body,
    })
    await settle()

    expect(wrapper.find('[data-task-id="a1"]').text()).toContain('激光切割')
    wrapper.unmount()
  })
})
