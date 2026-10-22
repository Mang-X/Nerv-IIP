import { flushPromises, mount } from '@vue/test-utils'
import { expect, it, vi } from 'vitest'
import Result from './ProductionReportSerialResult.vue'
const getBatch = vi.hoisted(() => vi.fn())
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  getBusinessConsoleBarcodePrintBatch: getBatch,
}))
it('refreshes only the matching report batch and ignores a late response after context changes', async () => {
  const receipt = {
    reportNo: 'RPT-1',
    productionReportId: 'report-1',
    printBatchId: 'batch-1',
    serialNumbers: ['SN-1'],
    printStatus: 'ready-to-print',
  }
  const context = {
    principalId: 'user-1',
    organizationId: 'org-1',
    environmentId: 'env-1',
    scopeKind: 'organization',
    scopeId: 'org-1',
    generation: 1,
  }
  getBatch.mockResolvedValueOnce({
    data: {
      success: true,
      data: {
        printBatch: {
          printBatchId: 'batch-1',
          productionReportId: 'report-1',
          productionReportNo: 'RPT-1',
          status: 'failed',
        },
      },
    },
  })
  const wrapper = mount(Result, { props: { receipt, context } })
  await wrapper.get('[data-testid="refresh-print-status"]').trigger('click')
  await flushPromises()
  expect(wrapper.text()).toContain('打印发送失败')
  let finish!: (value: unknown) => void
  getBatch.mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finish = resolve
      }),
  )
  await wrapper.get('[data-testid="refresh-print-status"]').trigger('click')
  await wrapper.setProps({ context: { ...context, generation: 2 } })
  finish({
    data: {
      success: true,
      data: {
        printBatch: {
          printBatchId: 'batch-1',
          productionReportId: 'report-1',
          productionReportNo: 'RPT-1',
          status: 'sent-to-printer',
        },
      },
    },
  })
  await flushPromises()
  expect(wrapper.text()).not.toContain('已发送至打印机')
})
