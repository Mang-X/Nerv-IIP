import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { BusinessConsoleSchedulePlan } from '@nerv-iip/api-client'
import ScheduleRevisionReview from './ScheduleRevisionReview.vue'

// DomainInvariant / Regression: #3625 要求逐工序的资源、时间和原因，以及工单筛选。
const basePlan: BusinessConsoleSchedulePlan = {
  assignments: [
    {
      orderId: 'WO-001',
      operationId: '精车',
      resourceId: 'CNC-01',
      startUtc: '2026-09-30T00:00:00Z',
      endUtc: '2026-09-30T02:00:00Z',
    },
    {
      orderId: 'WO-002',
      operationId: '精车',
      resourceId: 'CNC-03',
      startUtc: '2026-09-30T02:00:00Z',
      endUtc: '2026-09-30T04:00:00Z',
    },
  ],
}
const candidate: BusinessConsoleSchedulePlan = {
  assignments: [
    {
      ...basePlan.assignments![0],
      resourceId: 'CNC-02',
      startUtc: '2026-09-30T01:00:00Z',
      endUtc: '2026-09-30T03:00:00Z',
    },
    {
      orderId: 'WO-003',
      operationId: '装配',
      resourceId: 'ASM-01',
      startUtc: '2026-09-30T04:00:00Z',
      endUtc: '2026-09-30T06:00:00Z',
    },
  ],
  changeSummary: [
    {
      orderId: 'WO-001',
      operationId: '精车',
      changeType: 'moved',
      message: '原设备停机，改派备用设备',
    },
    { orderId: 'WO-002', operationId: '精车', changeType: 'blocked', message: '无可用资源' },
    { orderId: 'WO-003', operationId: '装配', changeType: 'added', message: '新增急单' },
  ],
}

describe('修订逐工序对比', () => {
  it('按工单和工序关联两版 assignment，展示类型、前后资源/时间和原因', () => {
    const wrapper = mount(ScheduleRevisionReview, { props: { revision: { candidate }, basePlan } })
    const rows = wrapper.findAll('[data-change-row]')
    expect(rows).toHaveLength(3)
    expect(rows[0].text()).toContain('精车')
    expect(rows[0].text()).toContain('移动')
    expect(rows[0].text()).toContain('CNC-01 → CNC-02')
    expect(rows[0].text()).toContain('原设备停机，改派备用设备')
    const times = rows[0].findAll('time').map((time) => time.attributes('datetime'))
    expect(times).toEqual([
      '2026-09-30T00:00:00Z',
      '2026-09-30T02:00:00Z',
      '2026-09-30T01:00:00Z',
      '2026-09-30T03:00:00Z',
    ])
    expect(rows[1].text()).toContain('CNC-03 → 未排')
    expect(rows[2].text()).toContain('未排 → ASM-01')
  })

  it('筛选工单只显示该工单工序，清空恢复全部', async () => {
    const wrapper = mount(ScheduleRevisionReview, { props: { revision: { candidate }, basePlan } })
    const input = wrapper.get('input[aria-label="筛选工单"]')
    await input.setValue('WO-002')
    expect(wrapper.findAll('[data-change-row]')).toHaveLength(1)
    expect(wrapper.get('[data-change-row]').text()).toContain('无可用资源')
    await input.setValue('WO-009')
    expect(wrapper.text()).toContain('没有匹配的工单变更')
    await input.setValue('')
    expect(wrapper.findAll('[data-change-row]')).toHaveLength(3)
  })
})
