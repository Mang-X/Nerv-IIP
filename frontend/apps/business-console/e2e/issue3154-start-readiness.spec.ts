import { expect, test, type Page, type Route } from '@playwright/test'

const STORAGE_KEY = 'nerv-iip.business-console.auth'
const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'shift.supervisor',
  email: 'supervisor@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: [
    'business.mes.work-orders.read',
    'business.mes.work-orders.manage',
    'business.mes.operation-tasks.read',
    'business.mes.operation-tasks.manage',
  ],
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

// 使用公开 readiness HTTP fixture 验证生产构建的真实菜单；不证明真实 MES provider。
for (const surface of ['工序页', '工单详情'] as const) {
  test(`#3154 ${surface}在阻断时禁用、放行后恢复开工`, async ({ page }, testInfo) => {
    let blocked = true
    let startRequests = 0
    await seedStoredSession(page)
    await page.route('**/api/console/v1/**', routeConsoleApi)
    await page.route('**/api/business-console/v1/**', async (route) => {
      const url = new URL(route.request().url())
      const { pathname } = url
      const task = {
        operationTaskId: 'OP-CNC-10',
        operationTaskNo: 'OP-CNC-10',
        workOrderId: 'WO-CNC-1021',
        workOrderNo: 'WO-CNC-1021',
        operationSequence: 10,
        operationCode: '精车',
        status: 'Queued',
        workCenterId: 'WC-CNC-01',
        workCenterName: '精加工一线',
        allowedActions: blocked ? [] : ['start'],
        blockReasons: blocked ? ['WORK_ORDER_NOT_RELEASED'] : [],
        evaluatedAtUtc: '2026-10-12T02:00:00Z',
      }
      const order = {
        workOrderId: 'WO-CNC-1021',
        workOrderNo: 'WO-CNC-1021',
        skuId: 'SKU-SHAFT-01',
        skuCode: 'SKU-SHAFT-01',
        skuName: '传动轴',
        quantity: 120,
        status: blocked ? 'created' : 'released',
        readinessStatus: blocked ? 'Blocked' : 'Ready',
        blockingReasons: task.blockReasons,
        operationTasks: [task],
      }
      if (pathname.endsWith('/me/work-context')) {
        const scope = { kind: 'work-center', id: 'WC-CNC-01', displayName: '精加工一线' }
        return fulfillJson(
          route,
          envelope({
            organizationId: 'org-001',
            environmentId: 'env-dev',
            applicablePermissionCode: url.searchParams.get('permissionCode'),
            resolvedAtUtc: task.evaluatedAtUtc,
            principal: { id: principal.principalId, principalType: 'User' },
            resolutionStatus: 'resolved',
            authorizedScopes: [scope],
            availableScopeKinds: ['work-center'],
            selectedScope: scope,
            issues: [],
          }),
        )
      }
      if (pathname.endsWith('/operation-tasks/OP-CNC-10/start')) {
        startRequests++
        return fulfillJson(route, envelope({ accepted: true }))
      }
      if (pathname.endsWith('/mes/operation-tasks'))
        return fulfillJson(route, envelope({ items: [task], total: 1 }))
      if (pathname.endsWith('/mes/work-orders'))
        return fulfillJson(route, envelope({ items: [order], total: 1 }))
      if (pathname.endsWith('/mes/work-orders/WO-CNC-1021'))
        return fulfillJson(route, envelope(order))
      if (pathname.endsWith('/material-readiness'))
        return fulfillJson(route, envelope({ readinessStatus: 'Ready', items: [] }))
      return fulfillJson(route, envelope({ items: [] }))
    })
    async function openStart() {
      await page.goto(surface === '工序页' ? '/mes/operation-tasks' : '/mes/work-orders')
      if (surface === '工单详情') {
        await page.getByRole('button', { name: '工单操作 WO-CNC-1021', exact: true }).click()
        await page.getByRole('menuitem', { name: '工单详情与工序' }).click()
      }
      await page
        .getByRole('button', {
          name: surface === '工序页' ? '工序任务操作 工序 10' : '工序操作 第 10 道',
          exact: true,
        })
        .click()
      return page.getByRole('menuitem', { name: '开工', exact: true })
    }
    const disabledStart = await openStart()
    await expect(disabledStart).toBeDisabled()
    await expect(disabledStart).toHaveAttribute('title', /工单未下达.*先在工单列表下达/)
    await page.screenshot({ path: testInfo.outputPath('blocked.png'), fullPage: true })
    expect(startRequests).toBe(0)
    blocked = false
    const enabledStart = await openStart()
    await expect(enabledStart).toBeEnabled()
    await page.screenshot({ path: testInfo.outputPath('ready.png'), fullPage: true })
    await enabledStart.click()
    await expect.poll(() => startRequests).toBe(1)
  })
}

async function seedStoredSession(page: Page) {
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
}

async function routeConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())
  if (pathname === '/api/console/v1/auth/refresh') return fulfillJson(route, envelope(session))
  if (pathname === '/api/console/v1/auth/me') return fulfillJson(route, envelope(principal))
  return route.fallback()
}

function envelope<T>(data: T) {
  return { success: true, data }
}

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}
