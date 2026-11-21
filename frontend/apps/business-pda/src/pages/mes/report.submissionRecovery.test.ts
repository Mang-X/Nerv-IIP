import {
  route,
  type ReportEnvelope,
  successfulReceipt,
  recordReport,
  confirmReport,
  contextGenerationRef,
  reportContextRef,
  selectWorkOrder,
  deferred,
  resetFixture,
} from './report.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import ReportPage from './report.vue'

describe('PDA MES production reporting page', () => {
  beforeEach(resetFixture)

  it('旧提交等待时新 pair 被占用阻断，迟到响应不能释放该占用', async () => {
    const firstRequest = deferred<ReportEnvelope>()
    recordReport.mockImplementationOnce(() => firstRequest.promise)

    route.query = {
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const firstGood = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    firstGood.value = '2'
    firstGood.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(recordReport).toHaveBeenCalledTimes(1)

    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    await flushPromises()
    const secondGood = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    secondGood.value = '3'
    secondGood.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(recordReport).toHaveBeenCalledTimes(1)
    expect(document.body.querySelector('[data-testid="occupied-report"]')).not.toBeNull()

    firstRequest.resolve(successfulReceipt)
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(false)
    expect(
      document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.disabled,
    ).toBe(true)

    document.body.querySelector<HTMLButtonElement>('[data-testid="return-to-preparation"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    await wrapper.get('[data-testid="retry-report"]').trigger('click')
    await flushPromises()
    expect(recordReport).toHaveBeenCalledTimes(2)
    expect(recordReport.mock.calls[1][0]).toEqual(recordReport.mock.calls[0][0])
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('WO-2026-0001 · 工序 10')
  })

  it('A pending → B → A 保留原意图，迟到成功不自动确认，显式重试仍同 key', async () => {
    const firstRequest = deferred<ReportEnvelope>()
    recordReport.mockImplementationOnce(() => firstRequest.promise)
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '2'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    const firstKey = recordReport.mock.calls[0][0].idempotencyKey

    route.query = { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-3' }
    await flushPromises()
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(1)
    expect(document.body.querySelector('[data-testid="submit-report"]')).toBeNull()
    firstRequest.resolve(successfulReceipt)
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(false)
    await wrapper.get('[data-testid="retry-report"]').trigger('click')
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('RPT-DEFAULT')
    expect(recordReport.mock.calls[0][0].idempotencyKey).toBe(firstKey)
  })

  it('A pending → B → A 的 reject 恢复同一 pair 错误，并以同 key retry', async () => {
    const firstRequest = deferred<ReportEnvelope>()
    recordReport.mockImplementationOnce(() => firstRequest.promise)
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '2'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    const firstKey = recordReport.mock.calls[0][0].idempotencyKey

    route.query = { workOrderId: 'WO-2026-0002', operationTaskId: 'OP-3' }
    await flushPromises()
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    await flushPromises()
    firstRequest.reject(new Error('A lost response'))
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    await wrapper.get('[data-testid="retry-report"]').trigger('click')
    await flushPromises()
    expect(recordReport).toHaveBeenCalledTimes(2)
    expect(recordReport.mock.calls[1][0].idempotencyKey).toBe(firstKey)
  })

  it('HTTP 成功但回执 envelope 失败时必须 fail closed', async () => {
    recordReport.mockResolvedValueOnce({
      success: false,
      message: '回执实体校验失败',
      data: null,
    })
    route.query = {
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()

    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '3'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('回执实体校验失败')
    expect(wrapper.text()).not.toContain('报工成功')
  })

  it('POST confirmed 但公开 GET 回读错实体时不得显示报工成功', async () => {
    confirmReport.mockRejectedValueOnce(
      new Error('报工已受理，但公开记录尚未回读到同一工单与工序，请重试核验。'),
    )
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(confirmReport).toHaveBeenCalledWith({
      reportNo: 'RPT-DEFAULT',
      productionReportId: '019f-report-default',
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
      context: reportContextRef.value,
    })
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('尚未回读到同一工单与工序')
    expect(wrapper.text()).not.toContain('报工成功')

    await wrapper.get('[data-testid="retry-report"]').trigger('click')
    await flushPromises()
    expect(recordReport).toHaveBeenCalledTimes(1)
    expect(confirmReport).toHaveBeenCalledTimes(2)
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
  })

  it('POST 后主体切换时旧回执不得覆盖新上下文或显示成功', async () => {
    const pendingReceipt = deferred<ReportEnvelope>()
    recordReport.mockReturnValueOnce(pendingReceipt.promise)
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    reportContextRef.value = { ...reportContextRef.value, principalId: 'principal-2' }
    await flushPromises()
    pendingReceipt.resolve(successfulReceipt)
    await flushPromises()

    expect(confirmReport).not.toHaveBeenCalled()
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(false)
    expect(wrapper.text()).not.toContain('报工成功')
    wrapper.unmount()
  })

  it.each([
    ['principal', { principalId: 'principal-2' }],
    ['organization', { organizationId: 'org-002' }],
    ['environment', { environmentId: 'env-prod' }],
    ['reporting scope', { scopeKind: 'work-center', scopeId: 'WC-B' }],
  ])(
    'GET pending 时 %s A→B→A 失效旧 attempt，但保留凭据供当前 context 重新核验',
    async (_dimension, changedContext) => {
      const pendingConfirmation = deferred<{
        reportNo: string
        productionReportId: string
        workOrderId: string
        operationTaskId: string
      }>()
      confirmReport.mockReturnValueOnce(pendingConfirmation.promise)
      route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
      const wrapper = mount(ReportPage, { attachTo: document.body })
      await flushPromises()
      const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
      input.value = '1'
      input.dispatchEvent(new Event('input'))
      await flushPromises()
      document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
      await flushPromises()
      const oldIdempotencyKey = recordReport.mock.calls[0][0].idempotencyKey

      const contextA = { ...reportContextRef.value }
      contextGenerationRef.value = 2
      reportContextRef.value = { ...contextA, ...changedContext, generation: 2 }
      await flushPromises()
      contextGenerationRef.value = 3
      reportContextRef.value = { ...contextA, generation: 3 }
      await flushPromises()

      pendingConfirmation.resolve({
        reportNo: 'RPT-DEFAULT',
        productionReportId: '019f-report-default',
        workOrderId: 'WO-2026-0001',
        operationTaskId: 'OP-1',
      })
      await flushPromises()
      expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(false)
      expect(wrapper.text()).not.toContain('报工成功')

      await wrapper.get('[data-testid="retry-report"]').trigger('click')
      await flushPromises()
      expect(recordReport).toHaveBeenCalledTimes(1)
      expect(recordReport.mock.calls[0][0].idempotencyKey).toBe(oldIdempotencyKey)
      expect(confirmReport).toHaveBeenCalledTimes(2)
      expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
      wrapper.unmount()
    },
  )

  it.each([
    ['data null', null],
    ['data empty', {}],
    ['productionReportId missing', { reportNo: 'RPT-ONLY' }],
    ['reportNo missing', { productionReportId: '019f-only-id' }],
    ['productionReportId blank', { productionReportId: '   ', reportNo: 'RPT-1' }],
    ['reportNo blank', { productionReportId: '019f-id-1', reportNo: '   ' }],
  ])('success true 但 %s 时必须 fail closed', async (_name, data) => {
    recordReport.mockResolvedValueOnce({ success: true, data })
    route.query = { workOrderId: 'WO-2026-0001', operationTaskId: 'OP-1' }
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await flushPromises()
    const input = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    input.value = '1'
    input.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('报工成功')
  })

  it('reuses the SAME idempotencyKey on retry; a new operation mints a different key', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    const taskRows = wrapper.findAll('[data-row]')
    await taskRows[0].trigger('click')
    await flushPromises()

    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '8'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()

    // 首次提交失败
    recordReport.mockRejectedValueOnce(new Error('lost response'))
    document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)

    // 不重新发起，直接点重试 → 复用同一 idempotencyKey
    wrapper.get('[data-testid="retry-report"]').trigger('click')
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(2)
    const firstKey = recordReport.mock.calls[0][0].idempotencyKey
    const retryKey = recordReport.mock.calls[1][0].idempotencyKey
    expect(firstKey).toBeTruthy()
    expect(retryKey).toBe(firstKey)

    // 成功后回到起点，发起新报工 → 新键
    wrapper.get('[data-testid="continue-report"]').trigger('click')
    await flushPromises()
    await selectWorkOrder(wrapper, 0)
    const newTaskRows = wrapper.findAll('[data-row]')
    await newTaskRows[0].trigger('click')
    await flushPromises()
    const good2 = document.body.querySelector<HTMLInputElement>('[data-testid="good-quantity"]')!
    good2.value = '3'
    good2.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(3)
    const newOpKey = recordReport.mock.calls[2][0].idempotencyKey
    expect(newOpKey).toBeTruthy()
    expect(newOpKey).not.toBe(firstKey)
    wrapper.unmount()
  })
})
