import {
  route,
  recordReport,
  materialLotsRef,
  selectWorkOrder,
  resetFixture,
} from './report.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import ReportPage from './report.vue'

describe('PDA MES production reporting page', () => {
  beforeEach(resetFixture)

  it('命令与可见 pair 精确一致，并只显示契约实际返回的回执字段', async () => {
    recordReport.mockResolvedValueOnce({
      success: true,
      data: {
        productionReportId: '019f-report-0003',
        reportNo: 'RPT-2026-0003',
      },
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
    goodInput.value = '4'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(1)
    expect(recordReport.mock.calls[0][0]).toMatchObject({
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
      goodQuantity: 4,
    })
    expect(wrapper.text()).toContain('WO-2026-0002 · 工序 10')
    expect(wrapper.text()).toContain('报工单号 RPT-2026-0003')
    expect(wrapper.text()).not.toContain('019f-report-0003')
  })

  it('records a report with the bound fields after entering quantity', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    // 选工序
    const taskRows = wrapper.findAll('[data-row]')
    await taskRows[0].trigger('click')
    await flushPromises()

    // 录入良品数（量录入区 teleport 到 body）
    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '8'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()

    const submitBtn = document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')!
    submitBtn.click()
    await flushPromises()

    expect(recordReport).toHaveBeenCalledTimes(1)
    const body = recordReport.mock.calls[0][0]
    expect(body.workOrderId).toBe('WO-2026-0001')
    expect(body.operationTaskId).toBe('OP-1')
    expect(body.goodQuantity).toBe(8)
    expect(body.scrapQuantity).toBe(0)
    expect(body).toHaveProperty('completesOperation')
    // org/env/timestamp 仍由 composable 注入，页面不传
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')
    expect(body).not.toHaveProperty('reportedAtUtc')
    // idempotencyKey 现由页面提供（稳定逐操作键）
    expect(body.idempotencyKey).toBeTruthy()

    // 成功后 Result 成功态
    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('报工成功')
    wrapper.unmount()
  })

  it('submits rework quantity and the selected received material lot consumption', async () => {
    materialLotsRef.value = [
      {
        requestId: 'MIR-PDA-001',
        workOrderId: 'WO-2026-0001',
        operationTaskId: 'OP-1',
        materialId: 'MAT-OIL',
        uomCode: 'L',
        materialLotId: 'LOT-OIL-001',
        requestedQuantity: 5,
        receivedQuantity: 5,
        consumedQuantity: 1,
        status: 'received',
      },
    ]
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    await wrapper.findAll('[data-row]')[0].trigger('click')
    await flushPromises()

    const reworkInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="rework-quantity"]',
    )!
    reworkInput.value = '2'
    reworkInput.dispatchEvent(new Event('input'))
    const scrapInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="scrap-quantity"]',
    )!
    scrapInput.value = '1'
    scrapInput.dispatchEvent(new Event('input'))
    await flushPromises()
    const scrapReason = document.body.querySelector<HTMLSelectElement>(
      '[data-testid="scrap-reason-code"]',
    )!
    scrapReason.value = 'SCRAP-SURFACE'
    scrapReason.dispatchEvent(new Event('change'))
    const materialCheckbox = document.body.querySelector<HTMLInputElement>(
      '[data-testid="material-lot-MIR-PDA-001"]',
    )!
    materialCheckbox.click()
    const materialQuantity = document.body.querySelector<HTMLInputElement>(
      '[data-testid="material-quantity-MIR-PDA-001"]',
    )!
    materialQuantity.value = '3'
    materialQuantity.dispatchEvent(new Event('input'))
    const goodInput = document.body.querySelector<HTMLInputElement>(
      '[data-testid="good-quantity"]',
    )!
    goodInput.value = '8'
    goodInput.dispatchEvent(new Event('input'))
    await flushPromises()
    document.body.querySelector<HTMLElement>('[data-testid="submit-report"]')!.click()
    await flushPromises()

    expect(recordReport.mock.calls[0][0]).toMatchObject({
      scrapReasonCode: 'SCRAP-SURFACE',
      reworkQuantity: 2,
      consumedMaterialLots: [
        {
          materialId: 'MAT-OIL',
          materialLotId: 'LOT-OIL-001',
          consumedQuantity: 3,
          materialIssueRequestNo: 'MIR-PDA-001',
        },
      ],
    })
    wrapper.unmount()
  })

  it('does not submit when no quantity was entered (good+scrap must be > 0)', async () => {
    const wrapper = mount(ReportPage, { attachTo: document.body })
    await selectWorkOrder(wrapper, 0)
    const taskRows = wrapper.findAll('[data-row]')
    await taskRows[0].trigger('click')
    await flushPromises()

    const submitBtn = document.body.querySelector<HTMLButtonElement>(
      '[data-testid="submit-report"]',
    )!
    submitBtn.click()
    await flushPromises()
    expect(recordReport).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
