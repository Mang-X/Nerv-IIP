import { expect, test } from '@playwright/test'

// 真实 Vue 页面与浏览器，API 使用 #2889/#2893 契约形状模拟；不证明真实打印机出纸。
test('序列号报工校验、未知结果刷新恢复、准备重试与只读状态追溯', async ({ page }, testInfo) => {
  const principal = {
    principalId: 'serial-operator',
    principalType: 'User',
    loginName: 'operator',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: [
      'business.mes.operations.read',
      'business.mes.reporting.write',
      'business.mes.reporting.read',
      'business.mes.work-orders.read',
      'business.mes.traceability.read',
      'business.barcode.read',
      'business.master-data.read',
    ],
  }
  const session = {
    principal,
    accessToken: 'mock-access',
    refreshToken: 'mock-refresh',
    sessionId: 'mock-session',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
  }
  const task = {
    operationTaskId: 'OPT-2026-0007',
    operationTaskNo: 'OPT-2026-0007',
    workOrderId: 'WO-2026-0142',
    workOrderNo: 'WO-2026-0142',
    status: 'inProgress',
    operationSequence: 20,
    workCenterId: 'WC-ASSY-01',
    workCenterName: '总装一线',
    qualityStatus: 'released',
    plannedQuantity: 200,
    goodQuantity: 0,
  }
  const writes: Record<string, unknown>[] = []
  const forbidden: string[] = []
  const traceReads: string[] = []
  let statusReads = 0
  await page.addInitScript((value) => {
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(value))
  }, session)
  await page.route(/\/api\/(?:business-console|console)\/v1\//, async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const json = (data: unknown) => route.fulfill({ json: { success: true, data } })
    if (/\/(dispatch|reprint|void)(\/|$)/.test(path)) forbidden.push(path)
    if (path.endsWith('/auth/refresh')) return json(session)
    if (path.endsWith('/auth/me')) return json(principal)
    if (path.endsWith('/me/work-context')) {
      const scope = { kind: 'organization', id: 'org-001', displayName: '一号工厂' }
      return json({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolvedAtUtc: '2026-09-14T01:00:00.000Z',
        principal: { id: principal.principalId, principalType: principal.principalType },
        resolutionStatus: 'resolved',
        authorizedScopes: [scope],
        availableScopeKinds: ['organization'],
        selectedScope: scope,
        issues: [],
      })
    }
    if (path.endsWith('/mes/operation-tasks') || path.endsWith('/mes/wip'))
      return json({ items: [task], total: 1 })
    if (path.endsWith('/mes/work-orders/WO-2026-0142'))
      return json({ skuId: 'SKU-HOUSING', plannedQuantity: 200 })
    if (path.endsWith('/master-data/resources/sku/SKU-HOUSING'))
      return json({ active: true, serialTrackingPolicy: 'on-production' })
    if (path.endsWith('/barcode/templates'))
      return json({
        templates: [
          {
            templateId: 'tpl-housing',
            templateCode: 'HOUSING-LABEL',
            templateName: '壳体单件标签',
            status: 'active',
          },
        ],
        total: 1,
      })
    if (path.endsWith('/mes/production-reports') && route.request().method() === 'POST') {
      const body = route.request().postDataJSON()
      writes.push(body)
      if (writes.length === 1) return route.abort('failed')
      return route.fulfill({
        json: {
          success: true,
          data: {
            productionReportId: 'report-serial',
            reportNo: 'PRPT-2026-0042',
            serialNumbers: ['HOUSING-00001', 'HOUSING-00002'],
            printBatchId: 'batch-housing',
            printStatus: writes.length === 2 ? 'reserved' : 'sent-to-printer',
            printingPreparationPending: writes.length === 2,
            operationReceipt: {
              operationType: 'mes.production-report.record',
              authority: 'mes',
              resourceType: 'production-report',
              resourceId: 'report-serial',
              idempotencyKey: body.idempotencyKey,
              outcome: 'accepted',
              stateConfirmed: false,
              readbackRequired: true,
              readbackMethod: 'GET',
              readbackPath:
                '/api/business-console/v1/mes/production-reports/PRPT-2026-0042?organizationId=org-001&environmentId=env-dev',
            },
          },
        },
      })
    }
    if (path.endsWith('/mes/production-reports/PRPT-2026-0042'))
      return json({ report: { productionReportId: 'report-serial', reportNo: 'PRPT-2026-0042' } })
    if (path.endsWith('/barcode/print-batches/batch-housing')) {
      statusReads++
      return json({ printBatch: { printBatchId: 'batch-housing', status: 'failed' } })
    }
    if (path.includes('/mes/traceability/batches/')) traceReads.push(path)
    return json({ items: [], total: 0 })
  })
  await page.goto('/mes/operation-tasks')
  await page.getByRole('button', { name: '报工', exact: true }).click()
  const dialog = page.getByRole('dialog', { name: '报工', exact: true })
  const good = dialog.getByRole('spinbutton', { name: '合格数量 *', exact: true })
  await good.fill('1.5')
  await dialog.getByRole('button', { name: '提交报工', exact: true }).click()
  await expect(
    dialog.getByRole('alert').filter({ hasText: '合格数量必须为非负整数' }),
  ).toBeVisible()
  expect(writes).toHaveLength(0)
  await good.fill('2')
  await expect(dialog.getByText('待分配 2 个序列号')).toBeVisible()
  await dialog.getByLabel('标签模板').selectOption('tpl-housing')
  await dialog.getByRole('checkbox', { name: '本工序已完成' }).uncheck()
  await dialog.getByRole('button', { name: '提交报工', exact: true }).click()
  await expect(good).toBeDisabled()
  await page.reload()
  await page.getByRole('button', { name: '报工', exact: true }).click()
  await expect(good).toHaveValue('2')
  await expect(good).toBeDisabled()
  await dialog.getByRole('button', { name: '提交报工', exact: true }).click()
  await expect(dialog.getByText('打印准备待完成', { exact: true })).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('preparation-pending.png'), fullPage: true })
  await dialog.getByRole('button', { name: '继续完成打印准备' }).click()
  await expect(dialog.getByText('已发送至打印机', { exact: true })).toBeVisible()
  await expect(dialog.getByText('PRPT-2026-0042', { exact: true })).toBeVisible()
  await expect(dialog.getByRole('link')).toHaveCount(2)
  expect(writes).toHaveLength(3)
  expect(writes[1]).toEqual(writes[0])
  expect(writes[2]).toEqual(writes[0])
  expect(writes[0]).not.toHaveProperty('serialNumbers')
  expect(writes[0]).not.toHaveProperty('serialTrackingPolicy')
  await page.screenshot({ path: testInfo.outputPath('serial-report-receipt.png'), fullPage: true })
  await dialog.getByRole('button', { name: '刷新打印进度' }).click()
  await expect(dialog.getByText('标签发送失败', { exact: true })).toBeVisible()
  expect(statusReads).toBe(1)
  expect(writes).toHaveLength(3)
  await dialog.getByRole('link', { name: 'HOUSING-00002', exact: true }).click()
  await expect(page).toHaveURL(/\/mes\/traceability\?mode=batch&serialNo=HOUSING-00002/)
  await expect.poll(() => traceReads.some((path) => path.endsWith('/HOUSING-00002'))).toBe(true)
  expect(forbidden).toEqual([])
})
