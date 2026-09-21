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
  permissionCodes: ['business.mes.work-orders.read', 'business.mes.work-orders.manage'],
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

test.beforeEach(async ({ page }) => {
  await seedStoredSession(page)
  await page.route('**/api/console/v1/**', routeConsoleApi)
})

test('#3703 班组长可在工单详情填写原因并人工挂起', async ({ page }, testInfo) => {
  let workOrderStatus = 'released'
  const holdRequests: unknown[] = []

  await page.route('**/api/business-console/v1/**', async (route) => {
    const url = new URL(route.request().url())
    const { pathname } = url

    if (pathname === '/api/business-console/v1/me/work-context') {
      const scope = { kind: 'work-center', id: 'WC-ASSY-01', displayName: '总装一线' }
      return fulfillJson(
        route,
        envelope({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          applicablePermissionCode: url.searchParams.get('permissionCode'),
          resolvedAtUtc: '2026-09-22T01:00:00.000Z',
          principal: { id: principal.principalId, principalType: principal.principalType },
          resolutionStatus: 'resolved',
          authorizedScopes: [scope],
          availableScopeKinds: ['work-center'],
          selectedScope: scope,
          issues: [],
        }),
      )
    }

    if (pathname === '/api/business-console/v1/mes/work-orders/WO-3703/hold') {
      holdRequests.push(route.request().postDataJSON())
      workOrderStatus = 'hold'
      return fulfillJson(route, envelope({ accepted: true, downstreamDocumentId: 'WO-3703' }))
    }

    if (pathname === '/api/business-console/v1/mes/work-orders/WO-3703') {
      return fulfillJson(
        route,
        envelope({
          workOrderId: 'WO-3703',
          skuId: 'SKU-DAMPER-001',
          quantity: 120,
          status: workOrderStatus,
          readinessStatus: 'Ready',
          blockingReasons: [],
          operationTasks: [
            {
              operationTaskId: 'OP-3703-10',
              workOrderId: 'WO-3703',
              status: 'Ready',
              operationSequence: 10,
              workCenterId: 'WC-ASSY-01',
              qualityStatus: 'Ready',
            },
          ],
        }),
      )
    }

    if (pathname === '/api/business-console/v1/mes/work-orders/WO-3703/material-readiness') {
      return fulfillJson(
        route,
        envelope({
          workOrderId: 'WO-3703',
          readinessStatus: 'Ready',
          blockingReasons: [],
          items: [],
        }),
      )
    }

    return fulfillJson(route, envelope({ items: [] }))
  })

  await page.goto('/mes/work-orders/WO-3703', { waitUntil: 'domcontentloaded' })
  await page.getByTestId('open-hold-work-order').click()

  const dialog = page.getByRole('alertdialog')
  await expect(dialog).toBeVisible()
  await dialog.getByTestId('confirm-hold-work-order').click()
  await expect(dialog.getByTestId('hold-validation-summary')).toContainText(
    '请完整填写带 * 的必填项（已标红）。',
  )
  await expect(dialog.getByText('请输入挂起原因。')).toBeVisible()
  expect(holdRequests).toHaveLength(0)

  const screenshot = testInfo.outputPath('work-order-hold-validation.png')
  await page.screenshot({ path: screenshot, fullPage: true })
  await testInfo.attach('work-order-hold-validation', {
    path: screenshot,
    contentType: 'image/png',
  })

  await dialog.getByLabel('挂起原因').fill('总装设备异常，等待维修确认')
  await dialog.getByTestId('confirm-hold-work-order').click()

  await expect(dialog).toBeHidden()
  await expect(page.getByText('工单 WO-3703 已挂起。')).toBeVisible()
  await expect(page.getByText('已暂停', { exact: true })).toBeVisible()
  await expect.poll(() => holdRequests).toEqual([{ reason: '总装设备异常，等待维修确认' }])
})

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
