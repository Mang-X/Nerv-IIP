import { expect, test } from '@playwright/test'

// Browser fixture proves the actual page consumes the public lot field. Inventory posting
// is independently exercised by Count_completion_adjusts_only_the_saved_lot.
test('仓储盘点展示目标批次并在实盘操作中保留上下文', async ({ page }, testInfo) => {
  const principal = {
    principalId: 'warehouse-operator',
    principalType: 'User',
    loginName: 'warehouse',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: ['business.wms.counts.read', 'business.inventory.counts.manage'],
  }
  const session = {
    accessToken: 'fixture-access',
    refreshToken: 'fixture-refresh',
    sessionId: 'fixture-session',
    expiresAtUtc: '2099-01-01T00:00:00Z',
    principal,
  }
  await page.addInitScript((stored) => {
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored))
  }, session)
  await page.route('**/api/console/v1/**', async (route) => {
    await route.fulfill({
      json: {
        success: true,
        data: route.request().url().includes('/auth/me') ? principal : session,
      },
    })
  })
  await page.route('**/api/business-console/v1/**', async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    let data: unknown = { items: [], total: 0, resources: [] }
    if (path.endsWith('/me/work-context')) {
      const scope = { kind: 'site', id: 'SITE-001', displayName: '宁波工厂' }
      data = {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        resolutionStatus: 'resolved',
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        authorizedScopes: [scope],
        availableScopeKinds: ['site'],
        selectedScope: scope,
        issues: [],
      }
    }
    if (path.endsWith('/wms/work-scopes/counts')) {
      data = {
        items: [{ scopeKind: 'site', scopeId: 'SITE-001', displayName: '宁波工厂' }],
        total: 1,
      }
    }
    if (path.endsWith('/wms/count-executions')) {
      data = {
        items: [
          {
            countExecutionId: 'count-01',
            countNo: 'CNT-20261023-000001',
            organizationId: 'org-001',
            environmentId: 'env-dev',
            skuCode: 'SEAL-020',
            uomCode: 'pcs',
            siteCode: 'SITE-001',
            locationCode: 'RACK-A-01',
            lotNo: 'LOT-20261023-01',
            expectedQuantity: 15,
            status: 'Open',
            version: 1,
          },
        ],
        total: 1,
      }
    }
    await route.fulfill({ json: { success: true, data } })
  })
  await page.goto('/wms/counts')
  await expect(page.getByRole('cell', { name: 'LOT-20261023-01', exact: true })).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('count-list.png'), fullPage: true })
  await page.getByRole('button', { name: '盘点操作 CNT-20261023-000001' }).click()
  await page.getByRole('menuitem', { name: '完成盘点' }).click()
  const dialog = page.getByRole('dialog')
  await expect(dialog.getByText('LOT-20261023-01', { exact: true })).toBeVisible()
  await expect(dialog.getByLabel('实盘数量')).toHaveValue('15')
  await page.screenshot({ path: testInfo.outputPath('count-complete.png'), fullPage: true })
})
