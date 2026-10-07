import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SchedulingPlanOrders from './SchedulingPlanOrders.vue'
import { toModel } from '@nerv-iip/scheduling'

// DomainInvariant: #3628 销售单层级、真实工序状态与页内定位。
describe('计划销售单侧栏', () => {
  const model = toModel({
    assignments: [
      {
        assignmentId: 'a1',
        orderId: 'WO-03008',
        operationId: 'OP-10',
        operationSequence: 10,
        startUtc: '2026-08-01T00:00:00Z',
        endUtc: '2026-08-01T03:00:00Z',
      },
      {
        assignmentId: 'a2',
        orderId: 'WO-03008',
        operationId: 'OP-20',
        operationSequence: 20,
        startUtc: '2026-08-01T03:00:00Z',
        endUtc: '2026-08-01T06:00:00Z',
      },
      {
        assignmentId: 'a3',
        orderId: 'WO-03009',
        operationId: 'OP-10',
        operationSequence: 10,
        startUtc: '2026-08-01T06:00:00Z',
        endUtc: '2026-08-01T09:00:00Z',
      },
    ],
  })
  it('按销售单分组，显示工单数量进度和真实工序状态，选择行发出定位并高亮', async () => {
    const wrapper = mount(SchedulingPlanOrders, {
      props: {
        model,
        selectedTaskId: '',
        workOrders: [
          {
            workOrderId: 'WO-03008',
            status: 'started',
            quantity: 100,
            completedQuantity: 30,
            commercialSourceFacts: {
              status: 'available',
              salesOrders: [{ salesOrderNo: 'SO-08001' }, { salesOrderNo: 'SO-08002' }],
            },
            operationTasks: [
              { operationTaskNo: 'OP-10', status: 'Completed' },
              { operationTaskNo: 'OP-20', status: 'InProgress' },
            ],
          },
          { workOrderId: 'WO-03009', commercialSourceFacts: { status: 'forbidden' } },
        ],
      },
    })
    expect(wrapper.text()).toContain('SO-08001')
    expect(wrapper.text()).toContain('SO-08002')
    expect(wrapper.text()).toContain('30 / 100')
    expect(wrapper.text()).toContain('已完成')
    expect(wrapper.text()).toContain('执行中')
    expect(wrapper.text()).toContain('无权读取销售订单')
    await wrapper.get('button[data-operation="a2"]').trigger('click')
    expect(wrapper.emitted('select')).toEqual([['a2']])
    await wrapper.setProps({ selectedTaskId: 'a2' })
    expect(wrapper.get('button[data-operation="a2"]').attributes('aria-pressed')).toBe('true')
    expect(wrapper.get('button[data-operation="a1"]').attributes('aria-pressed')).toBe('false')
  })
})
