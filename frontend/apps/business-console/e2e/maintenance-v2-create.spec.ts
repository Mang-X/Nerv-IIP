import { expect, test } from '@playwright/test'
import path from 'node:path'
import { requireBrowserEvidenceOutputDir } from '../playwright.config'

test('PC 从权威目录选择停机原因并原样提交 v2', async ({ page }) => {
  const principal = {
    principalId: 'maintenance-engineer',
    principalType: 'User',
    loginName: '张工',
    organizationId: 'org-a',
    environmentId: 'env-a',
    permissionVersion: 1,
    permissionCodes: [
      'business.maintenance.work-orders.read',
      'business.maintenance.work-orders.manage',
    ],
  }
  const session = {
    principal,
    accessToken: 'browser-fixture-access',
    refreshToken: 'browser-fixture-refresh',
    sessionId: 'maintenance-browser',
    expiresAtUtc: '2099-01-01T00:00:00Z',
  }
  await page.addInitScript((stored) => {
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored))
  }, session)
  await page.route('**/api/**', async (route) => {
    const url = new URL(route.request().url())
    let data: unknown = { items: [], total: 0 }
    if (url.pathname.endsWith('/auth/refresh')) data = session
    else if (url.pathname.endsWith('/directories/downtime-reason')) {
      expect(url.searchParams.get('organizationId')).toBe('org-a')
      expect(url.searchParams.get('environmentId')).toBe('env-a')
      data = {
        items: [{ id: 'reason-1', code: 'Line-A.Spindle', displayName: '主轴检修' }],
        total: 1,
      }
    }
    await route.fulfill({ json: { success: true, data } })
  })
  await page.goto('/maintenance/work-orders?deviceAssetId=PRESS-01&sourceAlarmId=ALARM-2026-018')
  await page.locator('#mwo-unavailability-mode').click()
  await page.getByRole('option', { name: '登记设备不可用', exact: true }).click()
  await page.locator('#mwo-asset-unavailable-reason').click()
  await page.getByRole('option').filter({ hasText: '主轴检修' }).click()
  await expect(page.locator('#mwo-asset-unavailable-reason')).toContainText('主轴检修')
  await page.screenshot({
    path: path.join(requireBrowserEvidenceOutputDir(), 'maintenance-v2-create.png'),
    fullPage: true,
  })
  const posted = page.waitForRequest(
    (request) =>
      request.method() === 'POST' &&
      request.url().endsWith('/api/business-console/v2/maintenance/work-orders'),
  )
  await page.getByRole('button', { name: '创建维护工单', exact: true }).click()
  const request = await posted
  expect(request.postDataJSON()).toMatchObject({ assetUnavailableReasonCode: 'Line-A.Spindle' })
  expect(request.postDataJSON()).not.toHaveProperty('assetUnavailableReason')
})
