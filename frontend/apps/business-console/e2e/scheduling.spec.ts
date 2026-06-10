import { expect, test, type Page, type Route } from '@playwright/test'

// 排产工作台 E2E:默认引擎为 NativeEngine(未装 DHTMLX 试用包 → stub → 回落),
// 因此 CI 中确定性、免许可。真实 DHTMLX 渲染由 P4 浏览器确认验证。

const STORAGE_KEY = 'nerv-iip.business-console.auth'

const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

const PLAN = {
  planId: 'plan-1',
  status: 'generated',
  algorithmVersion: 'heuristic-1',
  generatedAtUtc: '2026-06-10T00:00:00.000Z',
  assignments: [
    { assignmentId: 'a1', orderId: 'WO-001', operationId: 'op-10', operationSequence: 10, resourceId: 'WC-001', workCenterId: 'WC-001', startUtc: '2026-06-10T08:00:00.000Z', endUtc: '2026-06-10T10:00:00.000Z', isLocked: false },
    { assignmentId: 'a2', orderId: 'WO-001', operationId: 'op-20', operationSequence: 20, resourceId: 'WC-002', workCenterId: 'WC-002', startUtc: '2026-06-10T10:00:00.000Z', endUtc: '2026-06-10T12:00:00.000Z', isLocked: true },
  ],
  resourceLoads: [
    { resourceId: 'WC-001', windowStartUtc: '2026-06-10T00:00:00.000Z', windowEndUtc: '2026-06-11T00:00:00.000Z', assignedMinutes: 120, availableMinutes: 480, utilization: 0.25 },
  ],
  conflicts: [
    { conflictId: 'c1', reasonCode: 'capacity', severity: 'warning', orderId: 'WO-001', operationId: 'op-20', resourceId: 'WC-002', message: '产能不足' },
  ],
  unscheduledOperations: [],
  changeSummary: [],
  ganttItems: [],
}

const PLAN_SUMMARY = {
  planId: 'plan-1', problemId: 'prob-1', status: 'generated', generatedAtUtc: '2026-06-10T00:00:00.000Z',
  assignmentCount: 2, conflictCount: 1, unscheduledOperationCount: 0,
}

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
    if (pathname.endsWith('/scheduling/plans')) return fulfill(route, { success: true, data: [PLAN_SUMMARY] })
    if (/\/scheduling\/plans\/[^/]+$/.test(pathname)) return fulfill(route, { success: true, data: PLAN })
    return fulfill(route, { success: true, data: {} })
  })
  await page.route('**/api/business-console/v1/**', (route: Route) => fulfill(route, { success: true, data: {} }))
})

test('scheduling workbench renders the gantt and conflicts', async ({ page }) => {
  await page.goto('/scheduling')
  await expect(page.getByRole('heading', { name: '排产工作台', level: 1 })).toBeVisible()
  // 工单甘特默认视图,NativeEngine 渲染。
  const order = page.locator('[data-view="order"][data-engine="native"]')
  await expect(order).toBeVisible()
  await expect(order.locator('[data-task-id]').first()).toBeVisible()
  // 冲突以业务语言出现在侧栏。
  await expect(page.getByText('产能不足')).toBeVisible()
})

test('scheduling workbench switches to the resource board', async ({ page }) => {
  await page.goto('/scheduling')
  await page.getByRole('tab', { name: '资源排产板' }).click()
  await expect(page.locator('[data-view="resource"]')).toBeVisible()
})

async function fulfill(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
}

// silence unused param lint for Page import in some configs
export type _Page = Page
