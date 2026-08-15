import { flushPromises, mount } from '@vue/test-utils'
import type { ScheduleModel } from '@nerv-iip/scheduling'
import { describe, expect, it } from 'vitest'
import SchedulingDraftBoard from './SchedulingDraftBoard.vue'

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
  it('keeps table cells aligned with their visible headers', async () => {
    const wrapper = mount(SchedulingDraftBoard, {
      props: { model },
      global: {
        stubs: {
          GanttChart: true,
          ResourceSchedulerBoard: true,
        },
      },
    })

    const tableTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()

    // 列：工单/工序 · 资源 · 开始 · 结束 · 物料 · 设备状态 · 锁定 · 待排
    const cells = wrapper.findAll('tbody td')
    expect(cells).toHaveLength(8)
    expect((cells[2]!.find('input').element as HTMLInputElement).value).toBe('2026-07-24T08:00:00Z')
    expect(cells[4]!.text()).toContain('齐套')
    expect(cells[5]!.text()).toContain('正常')
    expect(cells[7]!.text()).toContain('移回待排')
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
      global: { stubs: { GanttChart: true, ResourceSchedulerBoard: true } },
    })

    const banner = wrapper.find('[data-testid="scheduling-material-risks"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('需在开工前完成备料')
    expect(banner.text()).toContain('RM-OIL-01')

    const tableTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.findAll('tbody td')[4]!.text()).toContain('缺料待备')
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
      global: { stubs: { GanttChart: true, ResourceSchedulerBoard: true } },
    })

    const banner = wrapper.find('[data-testid="scheduling-equipment-risks"]')
    expect(banner.exists()).toBe(true)
    expect(banner.text()).toContain('开工前请人工确认设备可用')
    expect(banner.text()).toContain('DEV-CNC-01')

    const tableTab = wrapper.findAll('[role="tab"]').find((tab) => tab.text().includes('表格编辑'))!
    await tableTab.trigger('focus')
    await tableTab.trigger('mousedown')
    await flushPromises()

    expect(wrapper.findAll('tbody td')[5]!.text()).toContain('状态未知')
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
          GanttChart: true,
          ResourceSchedulerBoard: true,
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
          GanttChart: true,
          ResourceSchedulerBoard: true,
        },
      },
    })

    wrapper.findComponent({ name: 'GanttChart' }).vm.$emit('lockedDragAttempt', 'assignment-001')

    expect(wrapper.emitted('lockedAttempt')).toEqual([['assignment-001']])
  })
})
