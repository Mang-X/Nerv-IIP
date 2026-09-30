import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it } from 'vitest'
import { toModel } from '../model/aps-mapper'
import { samplePlan } from '../model/fixtures'
import GanttChart from './GanttChart.vue'
import ResourceSchedulerBoard from './ResourceSchedulerBoard.vue'
import ReadonlyScheduleTimeline from './ReadonlyScheduleTimeline.vue'

async function settle() {
  await flushPromises()
  await nextTick()
  await flushPromises()
}

// 无 DHTMLX vendor 时(CI/单测环境)不挂载任何引擎,只断言与引擎无关的容器与占位。
// 「一 task 一节点」+ selectTask 的引擎级覆盖移到 engine/conformance.selfcheck.test.ts。

describe('GanttChart', () => {
  it('keeps current execution facts on cards and tooltips without a commercial engine', async () => {
    const model = toModel({
      ...samplePlan,
      assignments: [
        {
          ...samplePlan.assignments![0],
          currentExecution: {
            workOrderProgress: { completedQuantity: 25, plannedQuantity: 100 },
            isMaterialReady: false,
            materialReadyUtc: null,
          },
        },
      ],
    })
    const wrapper = mount(GanttChart, { props: { model, readOnly: true } })
    await settle()
    const card = wrapper.get('[data-task-id="a1"]')
    expect(card.text()).toContain('工单进度25 / 100')
    expect(card.text()).toContain('缺料 · 未知到料日')
    expect(card.attributes('title')).toContain('工单进度 25 / 100')
    wrapper.unmount()
  })

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

  it('只读卡片显示真实预计到料日期，无 ETA 的风险不造日期', async () => {
    const withEta = toModel({
      ...samplePlan,
      materialRisks: [
        {
          orderId: 'WO-001',
          operationId: 'op-10',
          reasonCodes: ['material-shortage'],
          shortages: [],
          message: '需在开工前完成备料',
          materialReadyUtc: '2026-09-28T08:00:00.000Z',
        },
        {
          orderId: 'WO-001',
          operationId: 'op-20',
          reasonCodes: ['material-shortage'],
          shortages: [],
          message: '需在开工前完成备料',
          materialReadyUtc: null,
        },
      ],
    })
    const wrapper = mount(GanttChart, {
      props: { model: withEta, scale: 'hour', readOnly: true },
      attachTo: document.body,
    })
    await settle()

    const withEtaTask = wrapper.find('[data-task-id="a1"]')
    const withoutEtaTask = wrapper.find('[data-task-id="a2"]')

    expect(withEtaTask.text()).toContain('预计到料 09-28')
    expect(withEtaTask.attributes('aria-label')).toContain('预计到料 09-28')
    expect(withoutEtaTask.text()).toContain('缺料待备')
    expect(withoutEtaTask.text()).not.toContain('预计到料')
    expect(withoutEtaTask.attributes('aria-label')).toContain('缺料待备')
    expect(withoutEtaTask.attributes('aria-label')).not.toContain('预计到料')
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

describe('ReadonlyScheduleTimeline assembly dependencies', () => {
  it('keeps a visible dependency when child completion and parent start touch on the same resource row', () => {
    const model = toModel({
      ...samplePlan,
      assignments: [
        {
          ...samplePlan.assignments![0],
          assignmentId: 'child',
          orderId: 'WO-CHILD',
          startUtc: '2026-06-10T00:00:00.000Z',
          endUtc: '2026-06-10T01:00:00.000Z',
        },
        {
          ...samplePlan.assignments![0],
          assignmentId: 'parent',
          orderId: 'WO-PARENT',
          startUtc: '2026-06-10T01:00:00.000Z',
          endUtc: '2026-06-10T02:00:00.000Z',
        },
      ],
      assemblyDependencies: [{ childOrderId: 'WO-CHILD', parentOrderId: 'WO-PARENT' }],
    })
    const wrapper = mount(ReadonlyScheduleTimeline, {
      props: { model, view: 'resource', scale: 'hour' },
    })
    // 08:00–16:00 的 720px 轴：子件 09:00 完工与母单 09:00 开工同为 x=90，行中心 y=32。
    // 路径绕过工序条顶沿，连续排程也必须留下可见连线。
    expect(wrapper.get('[data-dependency-id="child->parent"] path').attributes('d')).toBe(
      'M 90 140 H 102 V 112 H 78 V 140 H 90',
    )
    wrapper.unmount()
  })

  it('draws the child completion to parent start and removes the line when the relationship is absent', async () => {
    const plan = {
      ...samplePlan,
      assignments: [
        ...samplePlan.assignments!,
        {
          ...samplePlan.assignments![0],
          assignmentId: 'assembly',
          orderId: 'WO-ASSEMBLY',
          resourceId: 'WC-ASSEMBLY',
          workCenterId: 'WC-ASSEMBLY',
          startUtc: '2026-06-10T12:00:00.000Z',
          endUtc: '2026-06-10T14:00:00.000Z',
        },
      ],
      assemblyDependencies: [{ childOrderId: 'WO-001', parentOrderId: 'WO-ASSEMBLY' }],
    }
    const wrapper = mount(ReadonlyScheduleTimeline, {
      props: { model: toModel(plan), view: 'resource' },
    })
    const link = wrapper.get('[data-dependency-id="a2->assembly"]')
    expect(link.attributes('data-source')).toBe('a2')
    expect(link.attributes('data-target')).toBe('assembly')
    expect(link.get('path').attributes('d')).toBe('M 360 432 H 360 V 724 H 360')
    await wrapper.setProps({ model: toModel({ ...plan, assemblyDependencies: [] }) })
    expect(wrapper.find('[data-dependency-id]').exists()).toBe(false)
    wrapper.unmount()
  })
})
