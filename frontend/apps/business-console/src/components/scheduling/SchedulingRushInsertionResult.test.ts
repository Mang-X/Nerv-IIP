import { mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import SchedulingRushInsertionResult from './SchedulingRushInsertionResult.vue'
vi.mock('@nerv-iip/scheduling', () => ({
  toModel: () => ({ changes: [], tasks: [] }),
  ChangeSummaryPanel: { template: '<div aria-label="逐工序对比" />' },
}))
vi.mock('./SchedulingPreviewResult.vue', () => ({
  default: { template: '<div aria-label="候选排程" />' },
}))
it('展示权威承诺、延迟与破交期，并展开后端传播原因；完成只提供选定保存动作', () => {
  const wrapper = mount(SchedulingRushInsertionResult, {
    props: {
      job: {
        jobId: 'job-1',
        status: 'completed',
        input: { planId: 'plan-1', workOrderId: 'WO-12' },
        result: {
          baselinePlanId: 'plan-1',
          candidatePlanId: 'insertion-1',
          promiseUtc: '2026-10-09T10:00:00Z',
          candidate: { planId: 'insertion-1' },
          orders: [
            {
              orderId: 'WO-11',
              status: 'delayed',
              baselineCompletionUtc: '2026-10-09T10:00:00Z',
              candidateCompletionUtc: '2026-10-10T10:00:00Z',
              delayDays: 1,
              baselineLate: false,
              candidateLate: true,
              newlyLate: true,
            },
          ],
          operations: [
            { orderId: 'WO-11', operationId: '钻孔', reasonCodes: ['ResourceCapacity'], paths: [] },
          ],
        },
      },
    },
  })
  expect(wrapper.text()).toContain('可承诺交期')
  const date = (iso: string) => {
    const d = new Date(iso)
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
  }
  expect(wrapper.find('.rounded-md.border.p-3 .text-lg').text()).toBe(date('2026-10-09T10:00:00Z'))
  const cells = wrapper
    .findAll('tbody tr')
    .find((row) => row.text().includes('WO-11'))!
    .findAll('td')
  expect(cells[2]!.text()).toBe(date('2026-10-09T10:00:00Z'))
  expect(cells[3]!.text()).toBe(date('2026-10-10T10:00:00Z'))
  expect(wrapper.text()).toContain('WO-11')
  expect(wrapper.text()).toContain('新增破交期')
  expect(wrapper.text()).toContain('1 天')
  expect(wrapper.find('details').text()).toContain('产能竞争')
  expect(wrapper.findAll('button').map((b) => b.text())).toContain('选定候选并保存修订')
  expect(wrapper.findAll('button').map((b) => b.text())).not.toContain('确认发布')
})
it('不可承诺如实展示未知 ETA 与未排工单，不显示虚构日期', () => {
  const wrapper = mount(SchedulingRushInsertionResult, {
    props: {
      job: {
        status: 'completed',
        result: {
          promiseUtc: null,
          failures: ['unknownMaterialEta'],
          orders: [{ orderId: 'WO-12', status: 'unscheduled', delayDays: null }],
        },
      },
    },
  })
  expect(wrapper.text()).toContain('不可承诺')
  expect(wrapper.text()).toContain('物料到齐时间未知')
  expect(wrapper.text()).toContain('未排完整')
})

it('任务查询失败后显示失败状态并允许手动重试，不继续显示计算中', async () => {
  const wrapper = mount(SchedulingRushInsertionResult, {
    props: {
      job: { jobId: 'job-running', status: 'running' },
      failed: true,
      message: '候选计算失败：服务不可用；急单已保存。',
    },
  })
  expect(wrapper.text()).toContain('计算失败')
  expect(wrapper.text()).not.toContain('计算中')
  await wrapper
    .findAll('button')
    .find((b) => b.text().includes('手动重试'))!
    .trigger('click')
  expect(wrapper.emitted('retry')).toHaveLength(1)
})
