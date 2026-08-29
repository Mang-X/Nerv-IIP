import { expect, type Page, type Request } from '@playwright/test'

import type { WmsListPath } from './issue1912-wms-walkthrough-facts'

const AUTH_STORAGE_KEY = 'nerv-iip.business-console.auth'

const mountedPagePrincipal = {
  principalId: 'nerv-1571-mounted-worker',
  principalType: 'User',
  loginName: 'wms-worker-fixture',
  email: 'wms-worker-fixture@example.test',
  organizationId: 'org-live',
  environmentId: 'env-live',
  permissionVersion: 1,
  permissionCodes: ['business.wms.receipts.read', 'business.wms.shipments.read'],
}

const mountedPageSession = {
  accessToken: 'fixture-access-token',
  refreshToken: 'fixture-refresh-token',
  sessionId: 'fixture-session',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal: mountedPagePrincipal,
}

export type WmsProductionFixtureKind = 'inbound' | 'outbound'

export type WmsTargetRequestRecord = Readonly<{
  request: Request
  marked: boolean
}>

export type WmsTargetResponseDecision = Readonly<{
  status?: number
  body?: unknown
  delayMs?: number
}>

type WmsProductionFixtureOptions = Readonly<{
  kind: WmsProductionFixtureKind
  targetPath: WmsListPath
  onTargetRequest?: (
    request: Request,
    marked: boolean,
    markedRequestIndex: number,
  ) => WmsTargetResponseDecision | void
}>

const targetScopePath: Record<WmsProductionFixtureKind, string> = {
  inbound: '/api/business-console/v1/wms/work-scopes/receipts',
  outbound: '/api/business-console/v1/wms/work-scopes/shipments',
}

const targetRoute: Record<WmsProductionFixtureKind, string> = {
  inbound: '/wms/inbound',
  outbound: '/wms/outbound',
}

/**
 * Mounts the production WMS route and replaces only its HTTP dependencies. The page, NvDataTable,
 * and NvPagination are loaded from the application bundle; this fixture never supplies a
 * hand-written pagination DOM. Target requests remain observable so a caller can bind assertions
 * to the marked action request rather than to a response selected by path or arrival order.
 */
export async function mountWmsProductionFixture(
  page: Page,
  options: WmsProductionFixtureOptions,
): Promise<Readonly<{ targetRequests: WmsTargetRequestRecord[] }>> {
  const targetRequests: WmsTargetRequestRecord[] = []
  let markedRequestIndex = 0

  await page.addInitScript(
    ({ key, storedSession }) => {
      localStorage.setItem(key, JSON.stringify(storedSession))
    },
    { key: AUTH_STORAGE_KEY, storedSession: mountedPageSession },
  )

  await page.route('**/api/console/v1/**', async (route) => {
    const path = new URL(route.request().url()).pathname
    if (path.endsWith('/auth/me')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ success: true, data: mountedPagePrincipal }),
      })
      return
    }
    if (path.endsWith('/auth/refresh')) {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ success: true, data: mountedPageSession }),
      })
      return
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, data: {} }),
    })
  })

  await page.route('**/api/business-console/v1/**', async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname
    if (path === options.targetPath) {
      const marked = Boolean(request.headers()['x-nerv-walkthrough-action'])
      const currentMarkedRequestIndex = marked ? markedRequestIndex++ : -1
      targetRequests.push({ request, marked })
      const decision = options.onTargetRequest?.(request, marked, currentMarkedRequestIndex) ?? {}
      if (decision.delayMs !== undefined) {
        await new Promise((resolve) => setTimeout(resolve, decision.delayMs))
      }
      await route.fulfill({
        status: decision.status ?? 200,
        contentType: 'application/json',
        body: JSON.stringify(decision.body ?? { success: true, data: { items: [], total: 21 } }),
      })
      return
    }

    let data: unknown = { items: [], total: 0 }
    if (path === targetScopePath[options.kind]) {
      data = {
        actorPrincipalId: mountedPagePrincipal.principalId,
        items: [
          {
            scopeKind: 'work-pool',
            scopeId: options.kind === 'inbound' ? 'pool-receiving-001' : 'pool-shipping-001',
            displayName: options.kind === 'inbound' ? '收货作业池' : '发货作业池',
          },
        ],
        total: 1,
      }
    } else if (path === '/api/business-console/v1/master-data/resources') {
      data = {
        resources: [{ resourceType: 'site', code: 'SITE-001', displayName: '一号工厂' }],
        total: 1,
      }
    }

    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, data }),
    })
  })

  await page.goto(targetRoute[options.kind], {
    waitUntil: 'domcontentloaded',
    timeout: 120_000,
  })
  await expect(page.getByLabel('每页条数', { exact: true })).toBeVisible({ timeout: 120_000 })
  await expect(page.getByLabel('作业范围', { exact: true })).toContainText(
    options.kind === 'inbound' ? '收货作业池' : '发货作业池',
    { timeout: 120_000 },
  )
  return { targetRequests }
}
