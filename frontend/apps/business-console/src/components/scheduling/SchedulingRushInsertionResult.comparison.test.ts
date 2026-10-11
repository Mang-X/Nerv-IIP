import { mount } from '@vue/test-utils'
import { expect, it } from 'vitest'
import { NvStatusBadge } from '@nerv-iip/ui'
import SchedulingRushInsertionResult from './SchedulingRushInsertionResult.vue'

// Regression: 急单候选的超交期结果必须保留延期预警，而非修订的移动分类。
it('急单同工序推迟且超期保留延期与 warning 徽标', () => {
  const before = {
    orderId: 'WO-001',
    operationId: 'OP-10',
    resourceId: 'WC-01',
    startUtc: '2026-09-30T00:00:00Z',
    endUtc: '2026-09-30T02:00:00Z',
  }
  const wrapper = mount(SchedulingRushInsertionResult, {
    props: {
      job: {
        status: 'completed',
        acceptedBaseline: { baseline: { assignments: [before] } },
        result: {
          candidate: {
            assignments: [
              { ...before, startUtc: '2026-10-01T00:00:00Z', endUtc: '2026-10-01T02:00:00Z' },
            ],
            changeSummary: [
              {
                orderId: before.orderId,
                operationId: before.operationId,
                changeType: 'delayed',
                message: '超过交期',
              },
            ],
          },
        },
      },
    },
  })
  const row = wrapper.get('[data-change-row]')
  expect(row.text()).toContain('已延后')
  expect(row.text()).toContain('超过交期')
  expect(row.text()).not.toContain('已移动')
  expect(row.findComponent(NvStatusBadge).props('tone')).toBe('warning')
})
