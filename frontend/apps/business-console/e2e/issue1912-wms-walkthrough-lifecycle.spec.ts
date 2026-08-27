import { expect, test } from '@playwright/test'

import { NERV_1571_WMS_OUTBOUND_QUERY_FACTS } from './issue1912-wms-walkthrough-authority'
import {
  withWmsInitialListResponseGuard as withProductionWmsInitialListResponseGuard,
  withWmsInitialListResponseGuardForTest as withWmsInitialListResponseGuard,
} from './issue1912-wms-walkthrough-facts'

const inboundPath = '/api/business-console/v1/wms/inbound-orders'
const outboundPath = '/api/business-console/v1/wms/outbound-orders'
const authStorageKey = 'nerv-iip.business-console.auth'

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

test.describe('NERV-1571 / #1912 WMS lifecycle facts', () => {
  test.beforeEach(() => {
    test.skip(test.info().project.name !== 'desktop', 'WMS lifecycle probe 仅在 desktop project 运行')
  })

  test('页面挂载期间的完整 WMS auxiliary 读面不冒充目标列表', async ({ page }) => {
    const lifecycleTimeoutMs = 5_000
    const inboundAuxiliaryPaths = [
      outboundPath,
      '/api/business-console/v1/wms/work-scopes/receipts',
      '/api/business-console/v1/wms/operational-candidates/receipts',
      '/api/business-console/v1/wms/putaway-tasks',
      '/api/business-console/v1/wms/picking-tasks',
      '/api/business-console/v1/wms/count-executions',
      '/api/business-console/v1/wms/receiving-quality-gates',
      '/api/business-console/v1/wms/supplier-return-requests',
    ]
    const outboundAuxiliaryPaths = [
      '/api/business-console/v1/wms/work-scopes/shipments',
      '/api/business-console/v1/wms/operational-candidates/shipments',
      '/api/business-console/v1/wms/putaway-tasks',
      '/api/business-console/v1/wms/picking-tasks',
      '/api/business-console/v1/wms/count-executions',
    ]
    await page.route('**/api/business-console/v1/wms/**', async (route) => {
      await route.fulfill({ status: 200, body: JSON.stringify({ success: true, data: {} }) })
    })
    await page.setContent('<base href="http://walkthrough.fixture/">')

    const runStartupRequests = async (targetPath: string, auxiliaryPaths: string[]) => {
      const target = page.evaluate(
        async ({ path }) => {
          await new Promise((resolve) => setTimeout(resolve, 80))
          await fetch(`http://walkthrough.fixture${path}?phase=target`)
        },
        { path: targetPath },
      )
      await Promise.all(
        auxiliaryPaths.map((path) =>
          page.evaluate(async (requestPath) => {
            await fetch(`http://walkthrough.fixture${requestPath}?phase=auxiliary`)
          }, path),
        ),
      )
      await target
    }

    await expect(
      withWmsInitialListResponseGuard(
        page,
        inboundPath,
        () => runStartupRequests(inboundPath, inboundAuxiliaryPaths),
        lifecycleTimeoutMs,
      ),
    ).resolves.toMatchObject({ firstList: expect.anything() })

    const secondPage = await page.context().newPage()
    try {
      await secondPage.route('**/api/business-console/v1/wms/**', async (route) => {
        await route.fulfill({ status: 200, body: JSON.stringify({ success: true, data: {} }) })
      })
      await secondPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          secondPage,
          outboundPath,
          () =>
            Promise.all([
              secondPage.evaluate(
                async ({ path }) => {
                  await new Promise((resolve) => setTimeout(resolve, 80))
                  await fetch(`http://walkthrough.fixture${path}?phase=target`)
                },
                { path: outboundPath },
              ),
              ...outboundAuxiliaryPaths.map((path) =>
                secondPage.evaluate(async (requestPath) => {
                  await fetch(`http://walkthrough.fixture${requestPath}?phase=auxiliary`)
                }, path),
              ),
            ]),
          lifecycleTimeoutMs,
        ),
      ).resolves.toMatchObject({ firstList: expect.anything() })
    } finally {
      await secondPage.close()
    }
  })

  test('真实 inbound 页面挂载的 startup race 只发出该页面 registry 允许的 WMS 读面', async ({
    page,
  }) => {
    const requests: Array<{ method: string; path: string }> = []
    await page.addInitScript(
      ({ key, storedSession }) => {
        localStorage.setItem(key, JSON.stringify(storedSession))
      },
      { key: authStorageKey, storedSession: mountedPageSession },
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
      const url = new URL(route.request().url())
      requests.push({ method: route.request().method(), path: url.pathname })

      if (url.pathname === inboundPath) {
        // Keep the target response pending while all scope-bound catalog queries race on mount.
        await new Promise((resolve) => setTimeout(resolve, 120))
      }

      let data: unknown = { items: [], total: 0 }
      if (url.pathname === '/api/business-console/v1/wms/work-scopes/receipts') {
        data = {
          actorPrincipalId: mountedPagePrincipal.principalId,
          items: [
            {
              scopeKind: 'work-pool',
              scopeId: 'pool-receiving-001',
              displayName: '收货作业池',
            },
          ],
          total: 1,
        }
      } else if (url.pathname === '/api/business-console/v1/wms/operational-candidates/receipts') {
        data = {
          scopeKind: 'work-pool',
          scopeId: 'pool-receiving-001',
          locations: [],
          lots: [],
          asOfUtc: '2026-08-28T00:00:00.000Z',
          freshnessUtc: '2026-08-28T00:00:00.000Z',
          truncated: false,
        }
      } else if (url.pathname === '/api/business-console/v1/master-data/resources') {
        data = {
          resources: [{ resourceType: 'site', code: 'SITE-001', displayName: '一号工厂' }],
          total: 1,
        }
      } else if (url.pathname === '/api/business-console/v1/master-data/skus') {
        data = { resources: [], total: 0 }
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ success: true, data }),
      })
    })
    const guarded = await withProductionWmsInitialListResponseGuard(
      page,
      inboundPath,
      () => page.goto('/wms/inbound', { waitUntil: 'domcontentloaded' }),
      10_000,
      '/wms/inbound',
    )

    expect(guarded.firstList.status()).toBe(200)
    expect(guarded.firstList.request().method()).toBe('GET')
    expect(requests).toEqual(
      expect.arrayContaining([
        { method: 'GET', path: inboundPath },
        { method: 'GET', path: outboundPath },
        { method: 'GET', path: '/api/business-console/v1/wms/work-scopes/receipts' },
        { method: 'GET', path: '/api/business-console/v1/wms/operational-candidates/receipts' },
        { method: 'GET', path: '/api/business-console/v1/wms/putaway-tasks' },
        { method: 'GET', path: '/api/business-console/v1/wms/picking-tasks' },
        { method: 'GET', path: '/api/business-console/v1/wms/count-executions' },
        { method: 'GET', path: '/api/business-console/v1/wms/receiving-quality-gates' },
        { method: 'GET', path: '/api/business-console/v1/wms/supplier-return-requests' },
      ]),
    )
    expect(requests.filter((request) => request.path.startsWith('/api/business-console/v1/wms/')))
      .toEqual(
        expect.not.arrayContaining([
          { method: 'GET', path: '/api/business-console/v1/wms/inbound-orders/extra' },
          { method: 'GET', path: '/api/business-console/v1/wms/unknown' },
        ]),
      )
  })

  test('首个 WMS 列表响应为 503 或错误路径时，不接受后续 200 自洽通过', async ({ page }) => {
    const keyword = NERV_1571_WMS_OUTBOUND_QUERY_FACTS.keyword
    const lifecycleTimeoutMs = 5_000
    await page.route('**/api/business-console/v1/wms/*', async (route) => {
      const phase = new URL(route.request().url()).searchParams.get('phase')
      const attempt = new URL(route.request().url()).searchParams.get('attempt')
      await route.fulfill({
        status: phase === '503-first' && attempt === '1' ? 503 : 200,
        body: JSON.stringify({ items: [] }),
      })
    })
    await page.setContent('<base href="http://walkthrough.fixture/">')

    await expect(
      withWmsInitialListResponseGuard(
        page,
        inboundPath,
        async () =>
          page.evaluate(
            async ({ path, keyword: requestKeyword }) => {
              await fetch(`${path}?phase=503-first&attempt=1&keyword=${requestKeyword}`)
              await fetch(`${path}?phase=503-first&attempt=2&keyword=${requestKeyword}`)
            },
            { path: `http://walkthrough.fixture${inboundPath}`, keyword },
          ),
        lifecycleTimeoutMs,
      ),
    ).rejects.toThrow('HTTP 503')

    const secondPage = await page.context().newPage()
    try {
      await secondPage.route('**/api/business-console/v1/wms/*', async (route) => {
        await route.fulfill({ status: 200, body: JSON.stringify({}) })
      })
      await secondPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          secondPage,
          outboundPath,
          async () =>
            secondPage.evaluate(
              async ({ wrongPath, targetPath, requestKeyword }) => {
                await fetch(`${wrongPath}&keyword=${requestKeyword}`)
                await fetch(`${targetPath}&keyword=${requestKeyword}`)
              },
              {
                wrongPath: `http://walkthrough.fixture${inboundPath}?phase=wrong-first`,
                targetPath: `http://walkthrough.fixture${outboundPath}?phase=wrong-first`,
                requestKeyword: keyword,
              },
            ),
          lifecycleTimeoutMs,
        ),
      ).rejects.toThrow('unexpected WMS list-like request path')
    } finally {
      await secondPage.close()
    }

    const thirdPage = await page.context().newPage()
    try {
      let unknownAttempt = 0
      await thirdPage.route('**/api/business-console/v1/wms/*', async (route) => {
        unknownAttempt += 1
        await route.fulfill({
          status: unknownAttempt === 1 ? 503 : 200,
          body: JSON.stringify({ items: [] }),
        })
      })
      await thirdPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          thirdPage,
          inboundPath,
          async () =>
            thirdPage.evaluate(
              async ({ unknownPath, targetPath, requestKeyword }) => {
                await fetch(`${unknownPath}?phase=unknown-first&keyword=${requestKeyword}`)
                await fetch(`${targetPath}?phase=unknown-first&keyword=${requestKeyword}`)
              },
              {
                unknownPath: 'http://walkthrough.fixture/api/business-console/v1/wms/unknown',
                targetPath: `http://walkthrough.fixture${inboundPath}`,
                requestKeyword: keyword,
              },
            ),
          lifecycleTimeoutMs,
        ),
      ).rejects.toThrow('unexpected WMS list-like request path')
    } finally {
      await thirdPage.close()
    }

    const fourthPage = await page.context().newPage()
    try {
      let attempt = 0
      await fourthPage.route(`**${inboundPath}*`, async (route) => {
        attempt += 1
        if (attempt === 1) {
          await route.abort('failed')
          return
        }
        await route.fulfill({ status: 200, body: JSON.stringify({ items: [] }) })
      })
      await fourthPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          fourthPage,
          inboundPath,
          async () =>
            fourthPage.evaluate(
              async ({ path, requestKeyword }) => {
                try {
                  await fetch(`${path}?phase=network-failure&attempt=1&keyword=${requestKeyword}`)
                } catch {
                  // The route abort is the failure under test; the later 200 must not replace it.
                }
                await fetch(`${path}?phase=network-failure&attempt=2&keyword=${requestKeyword}`)
              },
              {
                path: `http://walkthrough.fixture${inboundPath}`,
                requestKeyword: keyword,
              },
            ),
          lifecycleTimeoutMs,
        ),
      ).rejects.toThrow('request failed')
    } finally {
      await fourthPage.close()
    }

    const fifthPage = await page.context().newPage()
    try {
      await fifthPage.route('**/api/business-console/v1/wms/**', async (route) => {
        await route.fulfill({ status: 200, body: JSON.stringify({ items: [] }) })
      })
      await fifthPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          fifthPage,
          inboundPath,
          async () =>
            fifthPage.evaluate(
              async ({ nestedPath, targetPath, requestKeyword }) => {
                await fetch(`${nestedPath}?phase=nested-first&keyword=${requestKeyword}`)
                await fetch(`${targetPath}?phase=nested-first&keyword=${requestKeyword}`)
              },
              {
                nestedPath: `http://walkthrough.fixture${inboundPath}/extra`,
                targetPath: `http://walkthrough.fixture${inboundPath}`,
                requestKeyword: keyword,
              },
            ),
          lifecycleTimeoutMs,
        ),
      ).rejects.toThrow('unexpected WMS list-like request path')
    } finally {
      await fifthPage.close()
    }

    const sixthPage = await page.context().newPage()
    try {
      await sixthPage.route(`**${inboundPath}*`, async (route) => {
        await route.fulfill({ status: 200, body: JSON.stringify({ items: [] }) })
      })
      await sixthPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          sixthPage,
          inboundPath,
          async () =>
            sixthPage.evaluate(
              async ({ path, requestKeyword }) => {
                await fetch(`${path}?phase=method-first&keyword=${requestKeyword}`, {
                  method: 'POST',
                  body: '{}',
                })
                await fetch(`${path}?phase=method-first&keyword=${requestKeyword}`)
              },
              {
                path: `http://walkthrough.fixture${inboundPath}`,
                requestKeyword: keyword,
              },
            ),
          lifecycleTimeoutMs,
        ),
      ).rejects.toThrow('unexpected WMS initial list request method POST')
    } finally {
      await sixthPage.close()
    }

    const seventhPage = await page.context().newPage()
    try {
      let firstRequestSeen = false
      await seventhPage.route('**/api/business-console/v1/wms/**', async (route) => {
        const path = new URL(route.request().url()).pathname
        if (path === inboundPath && !firstRequestSeen) {
          firstRequestSeen = true
          await new Promise((resolve) => setTimeout(resolve, 150))
        }
        await route.fulfill({ status: 200, body: JSON.stringify({ items: [] }) })
      })
      await seventhPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          seventhPage,
          inboundPath,
          async () =>
            seventhPage.evaluate(
              async ({ unknownPath, targetPath, requestKeyword }) => {
                const initial = fetch(
                  `${targetPath}?phase=unknown-after-first&keyword=${requestKeyword}`,
                )
                await new Promise((resolve) => setTimeout(resolve, 20))
                await fetch(`${unknownPath}?phase=unknown-after-first&keyword=${requestKeyword}`)
                await initial
              },
              {
                unknownPath: 'http://walkthrough.fixture/api/business-console/v1/wms/unknown',
                targetPath: `http://walkthrough.fixture${inboundPath}`,
                requestKeyword: keyword,
              },
            ),
          lifecycleTimeoutMs,
        ),
      ).rejects.toThrow('unexpected WMS list-like request path')
    } finally {
      await seventhPage.close()
    }

    const eighthPage = await page.context().newPage()
    try {
      let firstRequestSeen = false
      await eighthPage.route(`**${inboundPath}*`, async (route) => {
        if (!firstRequestSeen) {
          firstRequestSeen = true
          await new Promise((resolve) => setTimeout(resolve, 150))
        }
        await route.fulfill({ status: 200, body: JSON.stringify({ items: [] }) })
      })
      await eighthPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          eighthPage,
          inboundPath,
          async () =>
            eighthPage.evaluate(
              async ({ path, requestKeyword }) => {
                const initial = fetch(`${path}?phase=method-after-first&keyword=${requestKeyword}`)
                await new Promise((resolve) => setTimeout(resolve, 20))
                await fetch(`${path}?phase=method-after-first&keyword=${requestKeyword}`, {
                  method: 'POST',
                  body: '{}',
                })
                await initial
              },
              {
                path: `http://walkthrough.fixture${inboundPath}`,
                requestKeyword: keyword,
              },
            ),
          lifecycleTimeoutMs,
        ),
      ).rejects.toThrow('unexpected WMS initial list request method POST')
    } finally {
      await eighthPage.close()
    }

    const ninthPage = await page.context().newPage()
    try {
      let requestCount = 0
      await ninthPage.route(`**${inboundPath}*`, async (route) => {
        requestCount += 1
        if (requestCount === 1) {
          await new Promise((resolve) => setTimeout(resolve, 150))
        }
        await route.fulfill({
          status: requestCount === 2 ? 503 : 200,
          body: JSON.stringify({ items: [] }),
        })
      })
      await ninthPage.setContent('<base href="http://walkthrough.fixture/">')
      await expect(
        withWmsInitialListResponseGuard(
          ninthPage,
          inboundPath,
          async () =>
            ninthPage.evaluate(
              async ({ path, requestKeyword }) => {
                const initial = fetch(`${path}?phase=status-after-first&keyword=${requestKeyword}`)
                await new Promise((resolve) => setTimeout(resolve, 20))
                await fetch(`${path}?phase=status-after-first&keyword=${requestKeyword}`)
                await initial
              },
              {
                path: `http://walkthrough.fixture${inboundPath}`,
                requestKeyword: keyword,
              },
            ),
          lifecycleTimeoutMs,
        ),
      ).rejects.toThrow('HTTP 503')
    } finally {
      await ninthPage.close()
    }
  })
})
