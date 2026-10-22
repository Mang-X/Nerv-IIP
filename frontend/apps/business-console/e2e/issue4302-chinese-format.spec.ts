import { expect, test } from '@playwright/test'

// 本地路由 fixture 证明真实页面在 en-US 浏览器中的展示；不证明真实 provider 或父组合验收。
const principal = {
  principalId: 'planner-1',
  principalType: 'User',
  loginName: 'planner',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: [
    'business.planning.demands.read',
    'business.planning.mps.read',
    'business.planning.mrp.read',
    'business.planning.suggestions.manage',
    'business.maintenance.work-orders.read',
  ],
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

test.use({ locale: 'en-US', timezoneId: 'Asia/Shanghai', viewport: { width: 1440, height: 900 } })

test.beforeEach(async ({ page }) => {
  await page.addInitScript((storedSession) => {
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(storedSession))
  }, session)
  await page.route('**/api/console/v1/**', async (route) => {
    const pathname = new URL(route.request().url()).pathname
    await route.fulfill({
      json: { success: true, data: pathname.endsWith('/refresh') ? session : principal },
    })
  })
  await page.route('**/api/business-console/v1/**', async (route) => {
    const url = new URL(route.request().url())
    let data: unknown = { items: [], total: 0 }
    if (url.pathname.endsWith('/me/work-context')) {
      data = { organizationId: 'org-001', environmentId: 'env-dev' }
    } else if (url.pathname.endsWith('/master-data/resources')) {
      data = {
        resources:
          url.searchParams.get('resourceType') === 'unit-of-measure'
            ? [{ code: 'pcs', displayName: '件' }]
            : [],
        total: url.searchParams.get('resourceType') === 'unit-of-measure' ? 1 : 0,
      }
    } else if (url.pathname.endsWith('/planning/mps')) {
      data = {
        items: [
          {
            mpsId: 'mps-1',
            skuCode: 'FG-SHOCK',
            uomCode: 'pcs',
            siteCode: 'SITE-01',
            bucketDate: '2026-10-11',
            quantity: 120,
            status: 'Draft',
          },
          {
            mpsId: 'mps-2',
            skuCode: 'FG-SPRING',
            uomCode: 'missing-unit',
            siteCode: 'SITE-01',
            bucketDate: '2026-10-11',
            quantity: 35.5,
            status: 'Draft',
          },
        ],
        total: 2,
      }
    } else if (url.pathname.endsWith('/maintenance/work-orders')) {
      data = {
        items: [
          {
            workOrderId: 'work-order-1',
            workOrderNo: 'MWO-20261011-000001',
            deviceAssetId: 'DEV-PRESS-01',
            priority: 'high',
            status: 'open',
            openedAtUtc: '2026-10-11T00:15:00Z',
          },
        ],
        total: 1,
      }
    }
    await route.fulfill({ json: { success: true, data } })
  })
})

test('en-US 浏览器的 MPS 数量显示中文单位，缺名称保留数量与中性占位', async ({
  page,
}, testInfo) => {
  await page.goto('/planning')
  await page.getByRole('tab', { name: /MPS 主计划/ }).click()
  expect(await page.evaluate(() => navigator.language)).toBe('en-US')
  const namedRow = page.getByRole('row').filter({ hasText: 'FG-SHOCK' })
  await expect(namedRow.getByRole('cell', { name: '120 件', exact: true })).toBeVisible()
  const missingRow = page.getByRole('row').filter({ hasText: 'FG-SPRING' })
  await expect(missingRow.getByRole('cell', { name: '35.5 —', exact: true })).toBeVisible()
  await expect(namedRow).not.toContainText('pcs')
  await expect(missingRow).not.toContainText('missing-unit')
  await page.screenshot({ path: testInfo.outputPath('mps-en-US.png'), fullPage: true })
})

test('en-US 浏览器的维修开单时间按本地时区固定中文日期口径', async ({ page }, testInfo) => {
  await page.goto('/maintenance/work-orders')
  expect(await page.evaluate(() => navigator.language)).toBe('en-US')
  // 00:15 UTC 在 Asia/Shanghai 是同日 08:15，时刻事实保持不变。
  expect(await page.evaluate(() => new Date('2026-10-11T00:15:00Z').toLocaleString())).toBe(
    '10/11/2026, 8:15:00 AM',
  )
  const row = page.getByRole('row').filter({ hasText: 'MWO-20261011-000001' })
  await expect(row.getByRole('cell', { name: '2026-10-11 08:15', exact: true })).toBeVisible()
  await page.screenshot({ path: testInfo.outputPath('maintenance-en-US.png'), fullPage: true })
})
