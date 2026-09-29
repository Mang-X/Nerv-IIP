import { expect, test } from '@playwright/test'

// #4050：真实浏览器 + HTTP fixture，仅证明 Console 消费既有 history 契约。
// 服务端排序与 total 的正确性由 Scheduling/Gateway 测试承担。
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

test('历史方案超过 100 条仍可翻页，筛选回到第一页并展示真实窗口', async ({ page }) => {
  const requests: URL[] = []
  await page.addInitScript((storedSession) => {
    localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(storedSession))
  }, session)
  await page.route('**/api/console/v1/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    await route.fulfill({
      json: { success: true, data: path.endsWith('/me') ? principal : session },
    })
  })
  await page.route('**/api/business-console/v1/**', async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname.endsWith('/scheduling/plans/history')) {
      requests.push(url)
      const pageIndex = Number(url.searchParams.get('pageIndex'))
      const pageSize = Number(url.searchParams.get('pageSize'))
      const items = Array.from(
        { length: Math.min(pageSize, 137 - pageIndex * pageSize) },
        (_, i) => ({
          planId: `APS-202609-${String(pageIndex * pageSize + i + 1).padStart(3, '0')}`,
          status: 'released',
          generatedAtUtc: '2026-09-28T01:00:00Z',
          releasedAtUtc: '2026-09-29T01:00:00Z',
          assignmentCount: 12,
          conflictCount: 0,
          unscheduledOperationCount: 0,
          isInvalidated: false,
          horizonStartUtc: '2026-09-29T00:00:00Z',
          horizonEndUtc: '2026-10-06T00:00:00Z',
        }),
      )
      return route.fulfill({ json: { success: true, data: { items, total: 137 } } })
    }
    return route.fulfill({ json: { success: true, data: { items: [], total: 0 } } })
  })
  await page.goto('/scheduling')
  await page.getByRole('tab', { name: '表格' }).click()
  await expect(page.getByText('137 个方案')).toBeVisible()
  await expect(page.getByRole('cell', { name: 'APS-202609-001', exact: true })).toBeVisible()
  await expect(page.getByRole('cell', { name: /2026-09-29.*至.*2026-10-06/ }).first()).toBeVisible()
  await expect(page.getByText('发布时间从新到旧，未发布方案排在后面')).toBeVisible()
  await page.getByRole('button', { name: '最后一页', exact: true }).click()
  await expect(page.getByRole('cell', { name: 'APS-202609-131', exact: true })).toBeVisible()
  expect(requests.at(-1)?.searchParams.get('pageIndex')).toBe('13')
  await page.getByRole('combobox', { name: '按方案状态筛选' }).click()
  await page.getByRole('option', { name: '已发布', exact: true }).click()
  await expect.poll(() => requests.at(-1)?.searchParams.get('status')).toBe('released')
  expect(requests.at(-1)?.searchParams.get('pageIndex')).toBe('0')
  await page.getByRole('button', { name: '下一页', exact: true }).click()
  await expect.poll(() => requests.at(-1)?.searchParams.get('pageIndex')).toBe('1')
  await page.getByLabel('发布日（UTC）').fill('2026-09-29')
  await expect.poll(() => requests.at(-1)?.searchParams.get('releasedOn')).toBe('2026-09-29')
  expect(requests.at(-1)?.searchParams.get('pageIndex')).toBe('0')
  await page.getByRole('button', { name: '下一页', exact: true }).click()
  await expect.poll(() => requests.at(-1)?.searchParams.get('pageIndex')).toBe('1')
  await page.getByRole('combobox', { name: '按方案失效筛选' }).click()
  await page.getByRole('option', { name: '未失效', exact: true }).click()
  await expect.poll(() => requests.at(-1)?.searchParams.get('isInvalidated')).toBe('false')
  expect(requests.at(-1)?.searchParams.get('pageIndex')).toBe('0')
  await expect(page.getByRole('cell', { name: 'APS-202609-001', exact: true })).toBeVisible()
  const widths = await page.evaluate(() => ({
    viewport: document.documentElement.clientWidth,
    content: document.documentElement.scrollWidth,
  }))
  expect(widths.content).toBeLessThanOrEqual(widths.viewport)
  await page.screenshot({ path: test.info().outputPath('history-filtered.png'), fullPage: true })
})
