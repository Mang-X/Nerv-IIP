import { expect, test, type Route } from '@playwright/test'

/**
 * #2780 真机走查：首件未判合格时服务端拒绝批量报工，被拦下的操作员要能就地走到首件检验记录。
 *
 * 这条走查在**真浏览器**里驱动真实的行操作菜单（reka 弹层）与真实路由，只把两个读面用
 * `page.route` 造成有数据的样子（信封照抄真实响应），不需要后端栈、不落库、不发写请求。
 * jsdom 用例只能断言 `router.push` 的载荷，证不到「菜单真能展开」「落点真的切到首件记录页签」。
 *
 * 跑法：`pnpm exec playwright test e2e/issue2780 --project=desktop`
 * （playwright.config 的 webServer 会自行拉起 `vp dev`）。
 */
const STORAGE_KEY = 'nerv-iip.business-console.auth'

const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: ['business.mes.operations.read', 'business.quality.inspection-records.read'],
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

const operationTask = {
  operationTaskId: 'OPT-2026-0007',
  operationTaskNo: 'OPT-2026-0007',
  workOrderId: 'WO-2026-0142',
  workOrderNo: 'WO-2026-0142',
  status: 'inProgress',
  operationSequence: 20,
  workCenterId: 'WC-ASSY-01',
  workCenterCode: 'WC-ASSY-01',
  workCenterName: '总装一线',
  qualityStatus: 'released',
  assignedUserName: '张建国',
}

const firstArticleRecord = {
  id: 'a1d0c6e8-3f4b-4b6a-9c1d-2b7e5f0a1c33',
  code: 'IR-FA-2026-0031',
  skuCode: 'SKU-FG-1000',
  sourceDocumentId: 'WO-2026-0142:OPT-2026-0007',
  status: 'rejected',
  batchNo: 'LOT-2026-0142-01',
}

test.beforeEach(async ({ page }) => {
  await page.addInitScript(
    ({ key, storedSession }) => localStorage.setItem(key, JSON.stringify(storedSession)),
    {
      key: STORAGE_KEY,
      storedSession: {
        principal,
        refreshToken: session.refreshToken,
        sessionId: session.sessionId,
      },
    },
  )
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

test('#2780 工序执行行操作能就地打开首件检验记录页签', async ({ page }) => {
  await page.goto('/mes/operation-tasks', { waitUntil: 'domcontentloaded' })
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '工序执行' }),
  ).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText('OPT-2026-0007')).toBeVisible({ timeout: 30_000 })

  // 真实的行操作菜单：展不开就点不到，这正是 jsdom 证不到的那一段。
  await page.getByRole('button', { name: /工序任务操作/ }).click()
  const entry = page.getByRole('menuitem', { name: '首件检验记录' })
  await expect(entry).toBeVisible()
  await entry.click()

  await expect(page).toHaveURL(/\/quality\/inspections\?.*view=first-article-records/)

  // 先等落点稳定下来（渲染出记录），再断言抽屉不存在——否则 `toHaveCount(0)` 会在抽屉尚未挂载时
  // 提前为真，承担点就落到了后面的标题可见性上，与叙述不符。
  await expect(page.getByText('IR-FA-2026-0031')).toBeVisible({ timeout: 30_000 })
  // 落点不得自动弹开「创建检验记录」抽屉：本入口是去看结论的。
  // 首轮走查就栽在这里——入口带了 workOrderId，被 inspections.vue 的 query watch
  // 当成建单上下文，人一点进来就被丢进新建表单。
  await expect(page.getByRole('dialog', { name: '创建检验记录' })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: '首件检验记录' })).toBeVisible()
})

async function routeConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())
  if (pathname === '/api/console/v1/auth/refresh') return fulfillJson(route, envelope(session))
  if (pathname === '/api/console/v1/auth/me') return fulfillJson(route, envelope(principal))
  return route.fallback()
}

async function routeBusinessConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  const { pathname } = url

  if (pathname === '/api/business-console/v1/me/work-context') {
    const scope = { kind: 'organization', id: 'org-001', displayName: '一号工厂' }
    return fulfillJson(
      route,
      envelope({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        applicablePermissionCode: url.searchParams.get('permissionCode'),
        resolvedAtUtc: '2026-08-31T01:00:00.000Z',
        principal: { id: principal.principalId, principalType: principal.principalType },
        resolutionStatus: 'resolved',
        authorizedScopes: [scope],
        availableScopeKinds: ['organization'],
        selectedScope: scope,
        issues: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/operation-tasks') {
    return fulfillJson(route, envelope({ items: [operationTask], total: 1 }))
  }

  if (pathname === '/api/business-console/v1/quality/inspection-records') {
    return fulfillJson(route, envelope({ items: [firstArticleRecord], total: 1 }))
  }

  return fulfillJson(route, envelope({ items: [], total: 0 }))
}

function envelope<T>(data: T) {
  return { success: true, data }
}

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) })
}
