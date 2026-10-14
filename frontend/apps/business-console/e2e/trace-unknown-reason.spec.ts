import { expect, test } from '@playwright/test'

// Regression：#4434 / #4416 的公开 Unknown 根响应；只证明 fixture 页面行为。
test('无匹配批次或序列说明无详情原因且不提供虚假链接（fixture）', async ({ page }, testInfo) => {
  const batchOrSerial = 'SER-A4416-1'
  const principal = {
    principalId: 'trace-reader',
    principalType: 'User',
    loginName: 'reader',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: ['business.mes.traceability.read', 'business.mes.reporting.read'],
  }
  const session = {
    principal,
    accessToken: 'fixture-access',
    refreshToken: 'fixture-refresh',
    sessionId: 'trace-reader',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
  }
  await page.addInitScript(
    (stored) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored)),
    session,
  )
  await page.route(/\/api\/(?:business-console|console)\/v1\//, async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const json = (data: unknown) => route.fulfill({ json: { success: true, data } })
    if (path.endsWith('/auth/refresh')) return json(session)
    if (path.endsWith('/auth/me')) return json(principal)
    if (
      path.endsWith('/me/work-context') &&
      !principal.permissionCodes.includes(url.searchParams.get('permissionCode') ?? '')
    ) {
      return json({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolutionStatus: 'no-authorized-scope',
        authorizedScopes: [],
      })
    }
    if (path.endsWith('/me/work-context'))
      return json({
        organizationId: principal.organizationId,
        environmentId: principal.environmentId,
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolutionStatus: 'resolved',
        authorizedScopes: [{ kind: 'workCenter', id: 'WC-TUBE-01', displayName: '制管一线' }],
        selectedScope: { kind: 'workCenter', id: 'WC-TUBE-01', displayName: '制管一线' },
      })
    if (path.includes('/mes/traceability/')) {
      expect(decodeURIComponent(path.split('/').at(-1)!)).toBe(batchOrSerial)
      return json({
        nodes: [
          {
            nodeId: batchOrSerial,
            nodeType: 'BatchOrSerial',
            displayName: batchOrSerial,
            status: 'Unknown',
          },
        ],
        edges: [],
      })
    }
    return json({ items: [], total: 0, resources: [] })
  })
  await page.goto(`/mes/traceability?mode=batch&batchOrSerial=${batchOrSerial}`)
  const row = page.getByRole('row').filter({ has: page.getByText(batchOrSerial, { exact: true }) })
  await expect(row).toContainText('未找到匹配的批次/序列事实，暂无可查看详情')
  await expect(row.getByRole('link')).toHaveCount(0)
  await expect(page.getByLabel('批次或物料批', { exact: true })).toHaveValue(batchOrSerial)
  await page.screenshot({ path: testInfo.outputPath('unknown-root-reason.png'), fullPage: true })
})
