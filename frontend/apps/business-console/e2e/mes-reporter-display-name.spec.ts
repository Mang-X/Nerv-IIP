import { expect, test } from '@playwright/test'

// 真实页面 + Gateway 契约 fixture；IAM/Gateway 的权限和姓名查询范围由公开 HTTP seam 测试证明。
test('报工只读用户查看 IAM 操作人姓名及独立详情', async ({ page }, testInfo) => {
  const principal = {
    principalId: 'production-reader',
    principalType: 'User',
    loginName: 'reader',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: ['business.mes.reporting.read'],
  }
  const session = {
    principal,
    accessToken: 'read-access',
    refreshToken: 'read-refresh',
    sessionId: 'report-reader',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
  }
  const report = {
    productionReportId: 'pr-20261013-0042',
    reportNo: 'PRPT-20261013-0042',
    workOrderId: 'wo-20261013-0031',
    workOrderNo: 'WO-20261013-0031',
    operationTaskId: 'op-20',
    operationTaskNo: 'OPT-20261013-0020',
    goodQuantity: 120,
    scrapQuantity: 2,
    reworkQuantity: 1,
    reportedAtUtc: '2026-10-13T01:00:00Z',
    reportedBy: 'user-admin',
    reportedByDisplayName: '王建国',
  }
  const detailRequests: string[] = []
  await page.addInitScript((stored) => {
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored))
  }, session)
  await page.route(/\/api\/(?:business-console|console)\/v1\//, async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const json = (data: unknown) => route.fulfill({ json: { success: true, data } })
    if (path.endsWith('/auth/refresh')) return json(session)
    if (path.endsWith('/auth/me')) return json(principal)
    if (path.endsWith(`/mes/production-reports/${report.reportNo}`)) {
      detailRequests.push(url.search)
      return json({ report, consumedMaterialLots: [], laborAllocations: [] })
    }
    if (path.endsWith('/mes/production-reports')) return json({ items: [report], total: 1 })
    if (path.endsWith('/master-data/resources')) return json({ resources: [], total: 0 })
    if (path.endsWith('/mes/work-orders')) return json({ items: [], total: 0 })
    if (path.endsWith('/mes/telemetry-production-report-candidates'))
      return json({ items: [], total: 0 })
    return json({ items: [], total: 0 })
  })
  await page.goto('/mes/production-reports')
  const row = page.getByRole('row').filter({ hasText: report.reportNo })
  await expect(row).toContainText('王建国')
  await expect(page.getByRole('button', { name: '冲销', exact: true })).toHaveCount(0)
  await page.screenshot({ path: testInfo.outputPath('report-list.png'), fullPage: true })
  await page.getByRole('button', { name: report.reportNo, exact: true }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog).toContainText('王建国')
  await expect(dialog).not.toContainText('user-admin')
  expect(detailRequests).toHaveLength(1)
  expect(detailRequests[0]).toContain('organizationId=org-001')
  expect(detailRequests[0]).toContain('environmentId=env-dev')
  await page.screenshot({ path: testInfo.outputPath('report-detail.png'), fullPage: true })
})
