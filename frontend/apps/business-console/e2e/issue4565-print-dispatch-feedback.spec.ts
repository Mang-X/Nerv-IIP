import { expect, test } from '@playwright/test'

const batchId = 'pb-260926-014'
const batchPath = '/api/business-console/v1/barcode/print-batches'
const printer = { printerId: 'packing-01', name: '包装一线标签机' }
const organizationId = 'org-001'
const environmentId = 'env-dev'
const principal = {
  principalId: 'label-operator',
  principalType: 'User',
  loginName: 'label-operator',
  organizationId,
  environmentId,
  permissionVersion: 1,
  permissionCodes: ['business.barcodes.print'],
}
const session = {
  principal,
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'label-session',
  expiresAtUtc: '2099-01-01T00:00:00Z',
}

const cases = [
  {
    name: '明确失败',
    status: 'failed',
    label: '打印失败',
    feedback: '发送打印失败',
    failureReason: '打印机连接失败。',
  },
  {
    name: '已发送',
    status: 'sent-to-printer',
    label: '已发送打印机',
    feedback: '打印批次已发送至打印机，请现场确认出纸。',
  },
  {
    name: '送达待核实',
    status: 'delivery-unknown',
    label: '送达待核实',
    feedback: '送达待核实，请检查打印机作业记录和现场出纸。',
    losesResponse: true,
  },
  {
    name: '回读失败',
    status: 'sent-to-printer',
    feedback: '暂无法确认派发结果，请刷新批次详情并现场核实后再处理。',
    failsReadback: true,
  },
]

for (const scenario of cases) {
  test(`打印批次派发反馈：${scenario.name}`, async ({ page }) => {
    let dispatched = false
    let readBack = false
    let detailUnavailable = scenario.failsReadback
    const batch = () => ({
      printBatchId: batchId,
      sourceDocumentType: 'work-order',
      sourceDocumentId: 'WO-260926-014',
      status: dispatched ? scenario.status : 'ready-to-print',
      requestedQuantity: 2,
      failureReason: dispatched ? scenario.failureReason : undefined,
      items: [],
    })

    await page.addInitScript((value) => {
      localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(value))
    }, session)
    await page.route(/\/api\/(?:business-console|console)\/v1\//, async (route) => {
      const request = route.request()
      const url = new URL(request.url())
      const path = url.pathname
      const json = (data: unknown) => route.fulfill({ json: { success: true, data } })

      if (path.endsWith('/auth/refresh')) return json(session)
      if (path.endsWith('/auth/me')) return json(principal)
      if (path.endsWith('/barcode/printers')) return json({ printers: [printer] })
      if (path === batchPath) return json({ printBatches: [batch()], total: 1 })
      if (path === `${batchPath}/${batchId}`) {
        if (dispatched) {
          expect(url.searchParams.get('organizationId')).toBe(organizationId)
          expect(url.searchParams.get('environmentId')).toBe(environmentId)
          readBack = true
          if (detailUnavailable) return route.abort('failed')
        }
        return json({ printBatch: batch() })
      }
      if (path === `${batchPath}/${batchId}/dispatch` && request.method() === 'POST') {
        expect(request.postDataJSON()).toEqual({
          printBatchId: batchId,
          printerId: printer.printerId,
        })
        expect(url.searchParams.get('organizationId')).toBe(organizationId)
        expect(url.searchParams.get('environmentId')).toBe(environmentId)
        dispatched = true
        if (scenario.losesResponse) return route.abort('failed')
        return json({ printBatchId: batchId })
      }
      return json({})
    })

    await page.goto(`/barcode/print-batches?printBatchId=${batchId}`)
    const detail = page
      .locator('section')
      .filter({ has: page.getByRole('heading', { name: '批次详情' }) })
    await expect(detail.getByText('状态：待打印', { exact: true })).toBeVisible({ timeout: 30_000 })
    await detail.getByRole('button', { name: '打印机', exact: true }).click()
    await page.getByRole('option', { name: printer.name }).click()
    await detail.getByRole('button', { name: '发送打印' }).click()

    await expect(
      page.locator('[data-sonner-toast]').filter({ hasText: scenario.feedback }),
    ).toBeVisible()
    expect(readBack).toBe(true)
    if (!scenario.failsReadback) {
      await expect(detail.getByText(`状态：${scenario.label}`, { exact: true })).toBeVisible()
    } else {
      await expect(detail.getByRole('button', { name: '发送打印' })).toBeDisabled()
      detailUnavailable = false
      await page.getByRole('button', { name: '刷新', exact: true }).click()
      await expect(detail.getByText('状态：已发送打印机', { exact: true })).toBeVisible()
    }
    await expect(detail.getByText('已打印', { exact: true })).toHaveCount(0)
    if (scenario.failureReason) await expect(detail.getByText(scenario.failureReason)).toBeVisible()
    if (scenario.status === 'delivery-unknown') {
      await expect(
        detail.getByText('请检查打印机作业记录和现场出纸。', { exact: false }),
      ).toBeVisible()
      await expect(detail.getByRole('button', { name: '发送打印' })).toHaveCount(0)
    }
  })
}
