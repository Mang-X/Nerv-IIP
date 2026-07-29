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

    const cells = wrapper.findAll('tbody td')
    expect(cells).toHaveLength(6)
    expect((cells[2]!.find('input').element as HTMLInputElement).value).toBe('2026-07-24T08:00:00Z')
    expect(cells[5]!.text()).toContain('移回待排')
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
