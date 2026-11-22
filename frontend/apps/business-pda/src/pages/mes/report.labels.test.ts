import {
  route,
  successfulReceipt,
  recordReport,
  confirmReport,
  contextGenerationRef,
  reportContextRef,
  serialRequired,
  serialTemplateId,
  resetFixture,
} from './report.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import ReportPage from './report.vue'

describe('PDA MES production reporting page', () => {
  beforeEach(resetFixture)

  it('shows every allocated serial and transport status without claiming paper output', async () => {
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    recordReport.mockResolvedValue({
      success: true,
      data: {
        ...successfulReceipt.data,
        serialNumbers: ['SN-001', 'SN-002'],
        printBatchId: 'batch-1',
        printStatus: 'sent-to-printer',
      },
    })
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '2'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(wrapper.text()).toContain('SN-001')
    expect(wrapper.text()).toContain('SN-002')
    expect(wrapper.text()).toContain('已发送至打印机')
    expect(wrapper.text()).not.toContain('已打印')
  })

  it('keeps a successful report and retries pending label preparation with the same frozen template', async () => {
    serialRequired.value = true
    serialTemplateId.value = 'tpl-1'
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    recordReport.mockResolvedValueOnce({
      success: true,
      data: {
        ...successfulReceipt.data,
        serialNumbers: ['SN-001'],
        printBatchId: 'batch-1',
        printStatus: 'reserved',
        printingPreparationPending: true,
      },
    })
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(wrapper.text()).toContain('报工成功')
    expect(recordReport.mock.calls[0][0].labelTemplateId).toBe('tpl-1')
    serialTemplateId.value = 'tpl-2'
    await wrapper.get('[data-testid="retry-label-preparation"]').trigger('click')
    await flushPromises()
    expect(recordReport.mock.calls[1][0]).toEqual(recordReport.mock.calls[0][0])
  })

  it('keeps confirmed reporting successful while label preparation retry is pending or fails', async () => {
    serialRequired.value = true
    serialTemplateId.value = 'tpl-1'
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    recordReport.mockResolvedValueOnce({
      success: true,
      data: {
        ...successfulReceipt.data,
        serialNumbers: ['SN-001'],
        printBatchId: 'batch-1',
        printStatus: 'reserved',
        printingPreparationPending: true,
      },
    })
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    let rejectRetry!: (error: Error) => void
    recordReport.mockImplementationOnce(
      () =>
        new Promise((_, reject) => {
          rejectRetry = reject
        }),
    )
    await wrapper.get('[data-testid="retry-label-preparation"]').trigger('click')
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    rejectRetry(new Error('标签准备服务暂不可用，请稍后重试。'))
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('报工成功')
    expect(wrapper.text()).toContain('SN-001')
    expect(wrapper.text()).toContain('RPT-DEFAULT')
    expect(wrapper.text()).not.toContain('报工已受理，待核验')
    expect(wrapper.get('[data-testid="label-preparation-error"]').text()).toContain(
      '标签准备服务暂不可用',
    )
    expect(recordReport.mock.calls[1][0]).toEqual(recordReport.mock.calls[0][0])
    confirmReport.mockRejectedValueOnce(new Error('标签准备结果核对暂不可用，请重试。'))
    await wrapper.get('[data-testid="retry-label-preparation"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.get('[data-testid="label-preparation-error"]').text()).toContain(
      '标签准备结果核对暂不可用',
    )
    await wrapper.get('[data-testid="retry-label-preparation"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-testid="label-preparation-error"]').exists()).toBe(false)
    expect(recordReport.mock.calls[2][0]).toEqual(recordReport.mock.calls[0][0])
  })

  it.each(['principalId', 'organizationId', 'environmentId', 'scopeId'] as const)(
    'does not restore pending preparation for a different %s',
    async (field) => {
      route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
      recordReport.mockResolvedValueOnce({
        success: true,
        data: { ...successfulReceipt.data, printingPreparationPending: true },
      })
      const first = mount(ReportPage, { attachTo: document.body })
      await flushPromises()
      const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
      input.value = '1'
      input.dispatchEvent(new Event('input', { bubbles: true }))
      await flushPromises()
      document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
      await flushPromises()
      expect(first.find('[data-result][data-status="success"]').exists()).toBe(true)
      reportContextRef.value = { ...reportContextRef.value, [field]: 'other-context' }
      const other = mount(ReportPage)
      await flushPromises()
      expect(other.find('[data-result]').exists()).toBe(false)
      expect(other.find('[data-testid="retry-label-preparation"]').exists()).toBe(false)
      expect(recordReport).toHaveBeenCalledTimes(1)
    },
  )

  it('hides preparation in another context and restores it only after returning to its context', async () => {
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    recordReport.mockResolvedValueOnce({
      success: true,
      data: { ...successfulReceipt.data, printingPreparationPending: true },
    })
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    const originalContext = { ...reportContextRef.value }
    contextGenerationRef.value += 1
    reportContextRef.value = {
      ...reportContextRef.value,
      scopeId: 'other-scope',
      generation: contextGenerationRef.value,
    }
    await flushPromises()
    expect(wrapper.find('[data-result]').exists()).toBe(false)
    const reentered = mount(ReportPage)
    await flushPromises()
    expect(reentered.find('[data-result]').exists()).toBe(false)
    expect(recordReport).toHaveBeenCalledTimes(1)
    contextGenerationRef.value += 1
    reportContextRef.value = { ...originalContext, generation: contextGenerationRef.value }
    await flushPromises()
    expect(reentered.find('[data-result][data-status="success"]').exists()).toBe(true)
    await reentered.get('[data-testid="retry-label-preparation"]').trigger('click')
    await flushPromises()
    expect(recordReport.mock.calls[1][0]).toEqual(recordReport.mock.calls[0][0])
  })
})
