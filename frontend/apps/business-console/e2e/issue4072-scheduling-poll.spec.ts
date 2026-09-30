import { expect, test } from '@playwright/test'

// #4072：生产构建 + HTTP fixture，证明下一轮轮询更新页面，不证明 MES 服务端失效链。
const principal = {
  principalId: 'planner-1',
  principalType: 'User',
  loginName: 'planner',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: ['business.scheduling.plans.read', 'business.scheduling.plans.release'],
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  principal,
}

test('后台失效在下一轮轮询更新提示和发布按钮，退出后停止', async ({ page }) => {
  let invalidated = false
  let historyRequests = 0
  await page.clock.install()
  await page.addInitScript((storedSession) => {
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(storedSession))
  }, session)
  await page.route('**/api/console/v1/**', async (route) => {
    const pathname = new URL(route.request().url()).pathname
    await route.fulfill({
      json: { success: true, data: pathname.endsWith('/me') ? principal : session },
    })
  })
  await page.route('**/api/business-console/v1/**', async (route) => {
    const pathname = new URL(route.request().url()).pathname
    if (pathname.endsWith('/scheduling/plans/history')) {
      historyRequests++
      return route.fulfill({
        json: {
          success: true,
          data: {
            total: 1,
            items: [
              {
                planId: 'APS-260930-001',
                status: 'generated',
                assignmentCount: 12,
                isInvalidated: invalidated,
                latestInvalidationReasonCode: invalidated ? 'equipmentUnavailable' : undefined,
              },
            ],
          },
        },
      })
    }
    return route.fulfill({ json: { success: true, data: { items: [], total: 0 } } })
  })
  await page.goto('/scheduling')
  await page.getByRole('tab', { name: '表格' }).click()
  const row = page.getByRole('row').filter({ hasText: 'APS-260930-001' })
  const release = row.getByRole('button', { name: '发布', exact: true })
  await expect(release).toBeEnabled()
  invalidated = true
  await page.clock.fastForward(5000)
  await expect(row).toContainText('已失效')
  await expect(row).toContainText('设备不可用')
  await expect(release).toBeDisabled()
  await page.screenshot({
    path: test.info().outputPath('scheduling-invalidated.png'),
    fullPage: true,
  })
  await page.goto('/')
  const requestsAfterLeaving = historyRequests
  await page.clock.fastForward(10_000)
  expect(historyRequests).toBe(requestsAfterLeaving)
})
