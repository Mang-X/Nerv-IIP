import { flushPromises, mount } from '@vue/test-utils'
import type { ScheduleModel } from '@nerv-iip/scheduling'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { SchedulingToolbar, SchedulingLegend, TaskDetailPanel } from '@nerv-iip/scheduling'
import SchedulingDraftBoard from './SchedulingDraftBoard.vue'
import { useWorkingScheduleDraft } from '@/composables/useWorkingScheduleDraft'
import { NvInput } from '@nerv-iip/ui'

function chartStub(name: string) {
  return defineComponent({
    name,
    props: ['model', 'scale', 'readOnly'],
    emits: ['taskSelect', 'taskDragEnd', 'lockedDragAttempt'],
    setup(_props, { expose }) {
      const command = vi.fn()
      expose({ command })
      return { command }
    },
    render: () => h('div'),
  })
}
const charts = {
  GanttChart: chartStub('GanttChart'),
  ResourceSchedulerBoard: chartStub('ResourceSchedulerBoard'),
}
async function switchTab(wrapper: ReturnType<typeof mount>, text: string) {
  const tab = wrapper.findAll('[role="tab"]').find((item) => item.text().includes(text))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()
}
const model: ScheduleModel = {
  tasks: [
    {
      id: 'assignment-001',
      orderId: 'WO-001',
      operationId: 'OP-10',
      operationSequence: 10,
      type: 'operation',
      text: 'OP-10',
      resourceId: 'RES-1',
      workCenterId: 'WC-1',
      startUtc: '2026-07-24T08:00:00Z',
      endUtc: '2026-07-24T09:00:00Z',
      locked: false,
      hasConflict: false,
    },
  ],
  links: [],
  resources: [],
  loads: [],
  conflicts: [],
  unscheduled: [],
  changes: [],
  horizon: {
    startUtc: '2026-07-24T08:00:00Z',
    endUtc: '2026-07-24T09:00:00Z',
  },
  meta: {
    planId: 'plan-001',
    status: 'generated',
    algorithmVersion: 'aps-lite-v1',
  },
}

describe('SchedulingDraftBoard', () => {
  it('shows changed operation feedback after chart events and table edits without repreview (#4043)', async () => {
    const draft = useWorkingScheduleDraft()
    draft.loadPlan({
      planId: 'APS-260930-001',
      assignments: [
        {
          assignmentId: 'A-1',
          orderId: 'WO-01',
          operationId: '车削',
          resourceId: '机台一',
          workCenterId: '车削中心',
          startUtc: '2026-09-30T08:00:00Z',
          endUtc: '2026-09-30T10:00:00Z',
        },
        {
          assignmentId: 'A-2',
          orderId: 'WO-01',
          operationId: '精车',
          resourceId: '机台一',
          workCenterId: '车削中心',
          startUtc: '2026-09-30T10:00:00Z',
          endUtc: '2026-09-30T12:00:00Z',
        },
      ],
      calendars: [
        {
          calendarId: '白班',
          shiftWindows: [{ startUtc: '2026-09-30T08:00:00Z', endUtc: '2026-09-30T18:00:00Z' }],
        },
      ],
      validationContext: {
        horizonStartUtc: '2026-09-30T00:00:00Z',
        horizonEndUtc: '2026-10-01T00:00:00Z',
        resources: [
          {
            resourceId: '机台一',
            workCenterId: '车削中心',
            calendarId: '白班',
            capacityUnits: 1,
            utilizationRate: 1,
          },
        ],
        operations: [
          {
            orderId: 'WO-01',
            operationId: '车削',
            predecessorOperationIds: [],
            dueUtc: '2026-09-30T12:00:00Z',
            setupMinutes: 0,
          },
          {
            orderId: 'WO-01',
            operationId: '精车',
            predecessorOperationIds: ['车削'],
            dueUtc: '2026-09-30T12:00:00Z',
            setupMinutes: 0,
          },
        ],
        fixedReservations: [],
      },
    })
    const host = defineComponent({
      setup: () => () =>
        h(SchedulingDraftBoard, {
          model: draft.model.value,
          feedback: draft.feedback.value,
          onMove: draft.moveTask,
          onUpdate: draft.updateTask,
        }),
    })
    const wrapper = mount(host, { global: { stubs: charts } })
    wrapper.findComponent(charts.GanttChart).vm.$emit('taskDragEnd', {
      taskId: 'A-1',
      operationId: '车削',
      resourceId: '机台一',
      startUtc: '2026-09-30T18:00:00Z',
      endUtc: '2026-09-30T20:00:00Z',
      kind: 'move',
    })
    await flushPromises()
    expect(wrapper.get('[data-testid="scheduling-draft-task-detail"]').text()).toContain('日历外')
    expect(wrapper.get('[data-testid="scheduling-draft-task-detail"]').text()).toContain(
      '延期 480 分钟',
    )
    // 修改前序时，后继提示也必须直接可见，不能只藏在未选中的详情里。
    expect(wrapper.get('[data-testid="draft-feedback-attention"]').text()).toContain('精车')
    expect(wrapper.get('[data-testid="draft-feedback-attention"]').text()).toContain('前序倒置')
    draft.undo()
    await flushPromises()
    expect(wrapper.get('[data-testid="scheduling-draft-task-detail"]').text()).not.toContain(
      '日历外',
    )
    expect(wrapper.get('[data-testid="scheduling-draft-task-detail"]').text()).toContain(
      '提前 120 分钟',
    )
    await switchTab(wrapper, '资源排产板')
    wrapper.findComponent(charts.ResourceSchedulerBoard).vm.$emit('taskDragEnd', {
      taskId: 'A-1',
      operationId: '车削',
      resourceId: '机台一',
      startUtc: '2026-09-30T08:00:00Z',
      endUtc: '2026-09-30T19:00:00Z',
      kind: 'resize',
    })
    await flushPromises()
    expect(wrapper.get('[data-testid="scheduling-draft-task-detail"]').text()).toContain('日历外')
    await switchTab(wrapper, '表格编辑')
    const row = wrapper.get('tbody tr')
    row.findAllComponents(NvInput)[1]!.vm.$emit('update:modelValue', '2026-09-30T12:00:00Z')
    await flushPromises()
    expect(row.text()).toContain('按期')
    expect(row.text()).not.toContain('日历外')
    wrapper.unmount()
  })
  it('keeps table cells aligned with their visible headers', async () => {
    const wrapper = mount(SchedulingDraftBoard, {
      props: { model },
      global: {
        stubs: {
          GanttChart: charts.GanttChart,
          ResourceSchedulerBoard: charts.ResourceSchedulerBoard,
        },
      },
    })

    const tableTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()

    // 列：工单/工序 · 实际排程段 · 资源 · 开始 · 结束 · 物料 · 设备状态 · 锁定 · 待排
    const cells = wrapper.findAll('tbody td')
    expect(cells).toHaveLength(10)
    expect((cells[3]!.find('input').element as HTMLInputElement).value).toBe('2026-07-24T08:00:00Z')
    expect(cells[5]!.text()).toContain('齐套')
    expect(cells[6]!.text()).toContain('正常')
    expect(cells[8]!.text()).toContain('移回待排')
  })

  it('shows real segments and keeps the operation lock available without collapsing the gaps (#4004)', async () => {
    const operation = model.tasks[0]!
    const wrapper = mount(SchedulingDraftBoard, {
      props: {
        model: {
          ...model,
          tasks: [
            {
              ...operation,
              endUtc: '2026-07-25T09:00:00Z',
              segments: [
                { startUtc: operation.startUtc, endUtc: operation.endUtc },
                { startUtc: '2026-07-25T08:00:00Z', endUtc: '2026-07-25T09:00:00Z' },
              ],
            },
          ],
        },
      },
      global: { stubs: { GanttChart: true, ResourceSchedulerBoard: true } },
    })
    const tab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格编辑'))!
    await tab.trigger('focus')
    await tab.trigger('mousedown')
    await flushPromises()
    const row = wrapper.find('tbody tr')
    expect(row.text()).toContain('WO-001')
    expect(row.text()).toContain('OP-10')
    expect(row.text()).toContain('第 1 段')
    expect(row.text()).toContain('第 2 段')
    expect(row.findAll('input').every((input) => input.attributes('disabled') !== undefined)).toBe(
      true,
    )
    const lock = row.findAll('button').find((button) => button.text() === '锁定')!
    expect(lock.attributes('disabled')).toBeUndefined()
    await lock.trigger('click')
    expect(wrapper.emitted('lock')).toEqual([['assignment-001', true]])
  })

  // 产品裁决（#1291）：齐套是开工门槛不是排产门槛 —— 缺料工序照排，
  // 草案表格与横幅必须显式提示「需在开工前完成备料」，而不是把它当未排。
  it('renders material risk hints for scheduled-but-short operations', async () => {
    const risk = {
      orderId: 'WO-001',
      operationId: 'OP-10',
      reasonCodes: ['material-shortage'],
      shortages: [
        {
          materialId: 'RM-OIL-01',
          materialLotId: null,
          requiredQuantity: 145.86,
          availableQuantity: 0,
          shortageQuantity: 145.86,
        },
      ],
      message: '物料未齐套：RM-OIL-01 缺 145.86。已按计划排入,需在开工前完成备料。',
    }
    const riskyModel: ScheduleModel = {
      ...model,
      tasks: [{ ...model.tasks[0]!, materialRisk: risk }],
      materialRisks: [risk],
    }

    const wrapper = mount(SchedulingDraftBoard, {
      props: { model: riskyModel },
      global: {
        stubs: {
          GanttChart: charts.GanttChart,
          ResourceSchedulerBoard: charts.ResourceSchedulerBoard,
        },
      },
    })

    const banner = wrapper.find('[data-testid="scheduling-material-risks"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('需在开工前完成备料')
    expect(banner.text()).toContain('RM-OIL-01')

    const tableTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.findAll('tbody td')[5]!.text()).toContain('缺料待备')
  })

  // #1320:设备「状态未知」是数据盲区,不是不可用 —— 横幅 + 表格列都要如实说明。
  it('surfaces equipment data risks as a banner and a table chip', async () => {
    const risk = {
      orderId: 'WO-001',
      operationId: 'OP-10',
      resourceId: 'DEV-CNC-01',
      reasonCodes: ['equipment.sourceStale'],
      message: '设备 DEV-CNC-01 状态未知(采集数据已过期)。已按计划排入,开工前请人工确认设备可用。',
    }
    const riskyModel: ScheduleModel = {
      ...model,
      tasks: [{ ...model.tasks[0]!, equipmentRisk: risk }],
      equipmentRisks: [risk],
    }

    const wrapper = mount(SchedulingDraftBoard, {
      props: { model: riskyModel },
      global: {
        stubs: {
          GanttChart: charts.GanttChart,
          ResourceSchedulerBoard: charts.ResourceSchedulerBoard,
        },
      },
    })

    const banner = wrapper.find('[data-testid="scheduling-equipment-risks"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('开工前请人工确认设备可用')
    expect(banner.text()).toContain('DEV-CNC-01')

    const tableTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.findAll('tbody td')[6]!.text()).toContain('状态未知')
  })

  it('emits persistOverride with the task id, which the page must map to operationId', async () => {
    // 接线契约：板只上报 task.id（'assignment-001'），页面侧必须换算成 task.operationId（'OP-10'）
    // 再进 override 路径参数——fixture 两者刻意不同；operationId 进 path 由
    // useBusinessScheduling.test.ts 的 override body 映射测试把守。
    expect(model.tasks[0]!.id).not.toBe(model.tasks[0]!.operationId)

    const wrapper = mount(SchedulingDraftBoard, {
      props: { model },
      global: {
        stubs: {
          GanttChart: charts.GanttChart,
          ResourceSchedulerBoard: charts.ResourceSchedulerBoard,
        },
      },
    })

    const tableTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()

    const persistButton = wrapper
      .findAll('button')
      .find((button) => button.text().includes('持久锁定'))!
    await persistButton.trigger('click')

    expect(wrapper.emitted('persistOverride')).toEqual([['assignment-001']])
  })

  it('forwards locked drag attempts to its parent', () => {
    const wrapper = mount(SchedulingDraftBoard, {
      props: { model },
      global: {
        stubs: {
          GanttChart: charts.GanttChart,
          ResourceSchedulerBoard: charts.ResourceSchedulerBoard,
        },
      },
    })

    wrapper.findComponent({ name: 'GanttChart' }).vm.$emit('lockedDragAttempt', 'assignment-001')

    expect(wrapper.emitted('lockedAttempt')).toEqual([['assignment-001']])
  })
  // #4038 验收：控制状态、图面命令与详情均以同一草案为准。
  it('shares scale, search selection and page-local details across both charts', async () => {
    const wrapper = mount(SchedulingDraftBoard, { props: { model }, global: { stubs: charts } })
    await flushPromises()
    const toolbar = wrapper.findComponent(SchedulingToolbar)
    toolbar.vm.$emit('scaleChange', 'week')
    await flushPromises()
    const gantt = wrapper.findComponent({ name: 'GanttChart' })
    expect(gantt.props('scale')).toBe('week')
    expect(gantt.vm.command).toHaveBeenCalledWith({ kind: 'scaleTo', scale: 'week' })
    toolbar.vm.$emit('zoomIn')
    await flushPromises()
    expect(toolbar.props('scale')).toBe('day')
    expect(wrapper.findComponent(SchedulingLegend).props('scale')).toBe('day')
    toolbar.vm.$emit('update:search', ' wo-001 ')
    await flushPromises()
    expect(toolbar.props('matchCount')).toBe(1)
    expect(gantt.vm.command).toHaveBeenCalledWith({ kind: 'revealTask', taskId: 'assignment-001' })
    expect(gantt.vm.command).toHaveBeenCalledWith({ kind: 'selectTask', taskId: 'assignment-001' })
    expect(wrapper.findComponent(TaskDetailPanel).props('task')).toEqual(model.tasks[0])
    await switchTab(wrapper, '资源排产板')
    const resource = wrapper.findComponent({ name: 'ResourceSchedulerBoard' })
    expect(resource.props('scale')).toBe('day')
    expect(resource.vm.command).toHaveBeenCalledWith({
      kind: 'setSearchHighlight',
      taskIds: ['assignment-001'],
    })
    expect(resource.vm.command).toHaveBeenCalledWith({
      kind: 'revealTask',
      taskId: 'assignment-001',
    })
    wrapper.findComponent(SchedulingToolbar).vm.$emit('today')
    expect(resource.vm.command).toHaveBeenCalledWith({ kind: 'scrollToToday' })
    wrapper.findComponent(SchedulingToolbar).vm.$emit('fit')
    expect(resource.vm.command).toHaveBeenCalledWith({ kind: 'fitToScreen' })
    resource.vm.$emit('taskSelect', 'assignment-001')
    await flushPromises()
    expect(wrapper.find('[data-testid="scheduling-draft-task-detail"]').text()).toContain('WO-001')
    wrapper.unmount()
  })

  it('refreshes selected details and search results when edits, undo and pending changes replace the draft', async () => {
    const wrapper = mount(SchedulingDraftBoard, { props: { model }, global: { stubs: charts } })
    await flushPromises()
    wrapper.findComponent(SchedulingToolbar).vm.$emit('update:search', 'RES-1')
    await flushPromises()
    wrapper.findComponent(TaskDetailPanel).vm.$emit('toggle-lock', 'assignment-001', true)
    expect(wrapper.emitted('lock')).toEqual([['assignment-001', true]])
    const changed = {
      ...model,
      tasks: [
        { ...model.tasks[0]!, locked: true, resourceId: 'RES-2', endUtc: '2026-07-24T11:00:00Z' },
      ],
    }
    await wrapper.setProps({ model: changed })
    expect(wrapper.findComponent(TaskDetailPanel).props('task')).toEqual(changed.tasks[0])
    expect(wrapper.findComponent(SchedulingToolbar).props('matchCount')).toBe(0)
    expect(wrapper.findComponent(SchedulingLegend).text()).toContain('锁定')
    await wrapper.setProps({ model })
    expect(wrapper.findComponent(TaskDetailPanel).props('task')).toEqual(model.tasks[0])
    expect(wrapper.findComponent(SchedulingToolbar).props('matchCount')).toBe(1)
    expect(wrapper.findComponent(SchedulingLegend).text()).not.toContain('锁定')
    await wrapper.setProps({ model: { ...model, tasks: [] } })
    expect(wrapper.findComponent(TaskDetailPanel).exists()).toBe(false)
    expect(wrapper.findComponent(SchedulingToolbar).props('matchCount')).toBe(0)
    await wrapper.setProps({ model })
    expect(wrapper.findComponent(TaskDetailPanel).props('task')).toEqual(model.tasks[0])
    wrapper.unmount()
  })

  it('derives calendar and risk legend entries from the current draft and scale', async () => {
    const start = new Date(2026, 6, 24, 8).toISOString()
    const end = new Date(2026, 6, 24, 16).toISOString()
    const withCalendar = {
      ...model,
      calendars: [
        {
          calendarId: 'CAL-1',
          resourceIds: ['RES-1'],
          workCenterIds: [],
          shiftWindows: [{ shiftCode: 'DAY', startUtc: start, endUtc: end }],
        },
      ],
    }
    const wrapper = mount(SchedulingDraftBoard, {
      props: { model: withCalendar },
      global: { stubs: charts },
    })
    await flushPromises()
    wrapper.findComponent(SchedulingToolbar).vm.$emit('scaleChange', 'hour')
    await flushPromises()
    expect(wrapper.findComponent(SchedulingLegend).text()).toContain('班次边界')
    wrapper.findComponent(SchedulingToolbar).vm.$emit('scaleChange', 'day')
    await flushPromises()
    expect(wrapper.findComponent(SchedulingLegend).text()).not.toContain('班次边界')
    await wrapper.setProps({ model })
    expect(wrapper.findComponent(SchedulingLegend).text()).not.toContain('班次边界')
    expect(wrapper.findComponent(SchedulingLegend).text()).not.toContain('设备维护')
    wrapper.unmount()
  })

  it('keeps read-only lookup tools and shows a clear empty state without a plan', async () => {
    const wrapper = mount(SchedulingDraftBoard, {
      props: { model, readOnly: true },
      global: { stubs: charts },
    })
    await flushPromises()
    wrapper.findComponent({ name: 'GanttChart' }).vm.$emit('taskSelect', 'assignment-001')
    await flushPromises()
    expect(wrapper.findComponent(TaskDetailPanel).props('readOnly')).toBe(true)
    expect(wrapper.findComponent(TaskDetailPanel).findAll('button')).toHaveLength(0)
    expect(wrapper.findComponent(SchedulingToolbar).props('searchable')).toBe(true)
    expect(wrapper.findComponent({ name: 'GanttChart' }).props('readOnly')).toBe(true)
    await wrapper.setProps({ model: undefined })
    expect(wrapper.text()).toContain('生成首版方案后开始编辑')
    expect(wrapper.findComponent(SchedulingToolbar).exists()).toBe(false)
    expect(wrapper.findComponent(TaskDetailPanel).exists()).toBe(false)
    wrapper.unmount()
  })
})
