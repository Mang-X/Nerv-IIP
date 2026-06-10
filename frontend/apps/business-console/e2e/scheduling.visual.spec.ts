import { expect, test, type Route } from '@playwright/test'

// 排产工作台视觉回归基线:工单甘特 / 资源排产板 × 亮/暗。NativeEngine 确定性渲染
// (now 线取 horizon 中点,无真实时钟),适合稳定快照。仅在 desktop project 生成基线。

const STORAGE_KEY = 'nerv-iip.business-console.auth'
const principal = {
  principalId: 'principal-1', principalType: 'User', loginName: 'admin', email: 'admin@example.test',
  organizationId: 'org-001', environmentId: 'env-dev', permissionVersion: 1,
}
const session = { accessToken: 'a', refreshToken: 'r', sessionId: 's1', expiresAtUtc: '2099-01-01T00:00:00.000Z', principal }

const PLAN = {
  planId: 'plan-1', status: 'generated', algorithmVersion: 'heuristic-1', generatedAtUtc: '2026-06-10T00:00:00.000Z',
  assignments: [
    { assignmentId: 'a1', orderId: 'WO-001', operationId: 'op-10', operationSequence: 10, resourceId: 'WC-001', workCenterId: 'WC-001', startUtc: '2026-06-10T08:00:00.000Z', endUtc: '2026-06-10T10:00:00.000Z', isLocked: false },
    { assignmentId: 'a2', orderId: 'WO-001', operationId: 'op-20', operationSequence: 20, resourceId: 'WC-002', workCenterId: 'WC-002', startUtc: '2026-06-10T10:00:00.000Z', endUtc: '2026-06-10T14:00:00.000Z', isLocked: true },
    { assignmentId: 'a3', orderId: 'WO-002', operationId: 'op-10', operationSequence: 10, resourceId: 'WC-001', workCenterId: 'WC-001', startUtc: '2026-06-10T11:00:00.000Z', endUtc: '2026-06-10T15:00:00.000Z', isLocked: false },
  ],
  resourceLoads: [
    { resourceId: 'WC-001', windowStartUtc: '2026-06-10T00:00:00.000Z', windowEndUtc: '2026-06-11T00:00:00.000Z', assignedMinutes: 360, availableMinutes: 480, utilization: 0.75 },
    { resourceId: 'WC-002', windowStartUtc: '2026-06-10T00:00:00.000Z', windowEndUtc: '2026-06-11T00:00:00.000Z', assignedMinutes: 540, availableMinutes: 480, utilization: 1.12 },
  ],
  conflicts: [{ conflictId: 'c1', reasonCode: 'capacity', severity: 'warning', orderId: 'WO-001', operationId: 'op-20', resourceId: 'WC-002', message: '产能不足' }],
  unscheduledOperations: [], changeSummary: [], ganttItems: [],
}
const SUMMARY = { planId: 'plan-1', status: 'generated', generatedAtUtc: '2026-06-10T00:00:00.000Z', assignmentCount: 3, conflictCount: 1, unscheduledOperationCount: 0 }

test.describe('scheduling visual', () => {
  test.skip(({}, testInfo) => testInfo.project.name !== 'desktop', 'baselines only on desktop')

  test.beforeEach(async ({ page }) => {
    await page.addInitScript(
      ({ key, stored }) => localStorage.setItem(key, JSON.stringify(stored)),
      { key: STORAGE_KEY, stored: { principal, refreshToken: session.refreshToken, sessionId: session.sessionId } },
    )
    await page.route('**/api/console/v1/**', (route: Route) => {
      const { pathname } = new URL(route.request().url())
      if (pathname.endsWith('/auth/refresh')) return fulfill(route, { success: true, data: session })
      if (pathname.endsWith('/auth/me')) return fulfill(route, { success: true, data: principal })
      return route.fallback()
    })
    await page.route('**/api/business-console/v1/scheduling/plans**', (route: Route) => {
      const { pathname } = new URL(route.request().url())
      if (pathname.endsWith('/scheduling/plans')) return fulfill(route, { success: true, data: [SUMMARY] })
      if (/\/scheduling\/plans\/[^/]+$/.test(pathname)) return fulfill(route, { success: true, data: PLAN })
      return fulfill(route, { success: true, data: {} })
    })
    await page.route('**/api/business-console/v1/**', (route: Route) => fulfill(route, { success: true, data: {} }))
  })

  for (const mode of ['light', 'dark'] as const) {
    test(`order gantt — ${mode}`, async ({ page }) => {
      await openWorkbench(page, mode)
      await expect(page.locator('[data-view="order"]')).toHaveScreenshot(`order-${mode}.png`, { maxDiffPixelRatio: 0.02 })
    })

    test(`resource board — ${mode}`, async ({ page }) => {
      await openWorkbench(page, mode)
      await page.getByRole('tab', { name: '资源排产板' }).click()
      await expect(page.locator('[data-view="resource"]')).toHaveScreenshot(`resource-${mode}.png`, { maxDiffPixelRatio: 0.02 })
    })
  }
})

async function openWorkbench(page: import('@playwright/test').Page, mode: 'light' | 'dark') {
  await page.addInitScript((m) => localStorage.setItem('nerv-iip-color-mode', m), mode)
  await page.goto('/scheduling')
  await expect(page.getByRole('heading', { name: '排产工作台', level: 1 })).toBeVisible()
  await expect(page.locator('[data-view] [data-task-id]').first()).toBeVisible()
}

async function fulfill(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
}
