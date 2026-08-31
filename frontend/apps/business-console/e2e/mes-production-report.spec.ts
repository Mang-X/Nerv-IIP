import { expect, test, type Route } from '@playwright/test'

const STORAGE_KEY = 'nerv-iip.business-console.auth'
const principal = {
  principalId: 'principal-production-manager',
  principalType: 'User',
  loginName: 'production.manager',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: [
    'business.mes.reporting.read',
    'business.mes.operations.read',
    'business.iiot.telemetry.read',
  ],
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-production-report',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
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
})

test('生产日报呈现跨源上下文、服务端第二页并导出当前筛选全量 CSV', async ({ page }, testInfo) => {
  const statisticsRequests: URL[] = []
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))

  const routeApi = async (route: Route) => {
    const url = new URL(route.request().url())
    if (url.pathname === '/api/console/v1/auth/refresh') return fulfill(route, envelope(session))
    if (url.pathname === '/api/console/v1/auth/me') return fulfill(route, envelope(principal))
    if (url.pathname === '/api/business-console/v1/mes/production-statistics') {
      statisticsRequests.push(url)
      const rows = productionRows()
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 20)
      return fulfill(
        route,
        envelope({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          dimension: url.searchParams.get('dimension') ?? 'day',
          windowStartUtc: url.searchParams.get('windowStartUtc'),
          windowEndUtc: url.searchParams.get('windowEndUtc'),
          items: rows.slice(skip, skip + take),
          totalCount: rows.length,
          skip,
          take,
        }),
      )
    }
    if (url.pathname === '/api/business-console/v1/mes/wip') {
      return fulfill(
        route,
        envelope({
          items: [
            {
              workOrderId: 'work-order-42',
              workOrderNo: 'WO-20260831-0042',
              operationTaskId: 'operation-20',
              operationTaskNo: 'WO-20260831-0042-OP-20',
              workCenterId: 'WC-CNC-01',
              workCenterCode: 'WC-CNC-01',
              status: 'inProgress',
              plannedQuantity: 80,
              goodQuantity: 52,
            },
          ],
          total: 4,
        }),
      )
    }
    if (url.pathname === '/api/business-console/v1/telemetry/oee/aggregates') {
      return fulfill(
        route,
        envelope({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          dimension: 'day',
          buckets: [
            {
              dimension: 'day',
              dimensionValue: '2026-08-31',
              businessDate: '2026-08-31',
              bucketStartUtc: '2026-08-31T00:00:00.000Z',
              bucketEndUtc: '2026-09-01T00:00:00.000Z',
              performanceRate: 0.873,
              isDegraded: false,
            },
          ],
          totalCount: 1,
          skip: 0,
          take: 5,
        }),
      )
    }
    return fulfill(route, envelope({ items: [], total: 0 }))
  }
  await page.route('**/api/console/v1/**', routeApi)
  await page.route('**/api/business-console/v1/**', routeApi)

  await page.goto('/mes/reports', { waitUntil: 'domcontentloaded' })

  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '生产日报' }),
  ).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('WO-20260831-0042-OP-20', { exact: true })).toBeVisible()
  await expect(page.getByText('87.3%', { exact: true })).toBeVisible()
  await expect(page.getByText('2026-08-01', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('22 个业务日聚合', { exact: true })).toBeVisible()

  await page.getByRole('button', { name: '下一页' }).click()
  await expect(page.getByText('2026-08-21', { exact: true }).first()).toBeVisible()
  await expect(page.getByText('2026-08-22', { exact: true }).first()).toBeVisible()

  const downloadPromise = page.waitForEvent('download')
  await page.getByRole('button', { name: '导出 CSV' }).click()
  const download = await downloadPromise
  expect(download.suggestedFilename()).toMatch(/^生产日报_业务日_.*\.csv$/)
  const csv = await download.createReadStream()
  const chunks: Buffer[] = []
  for await (const chunk of csv) chunks.push(Buffer.from(chunk))
  const contents = Buffer.concat(chunks).toString('utf8')
  expect(contents.startsWith('\ufeff聚合维度,维度值,业务日')).toBe(true)
  expect(contents).toContain('2026-08-01')
  expect(contents).toContain('2026-08-22')

  await page.screenshot({
    path: testInfo.outputPath('issue-2857-production-daily-page-2.png'),
    fullPage: true,
  })

  expect(pageErrors).toEqual([])
  expect(
    statisticsRequests.some(
      (url) => url.searchParams.get('skip') === '20' && url.searchParams.get('take') === '20',
    ),
  ).toBe(true)
  expect(
    statisticsRequests.some(
      (url) => url.searchParams.get('skip') === '0' && url.searchParams.get('take') === '500',
    ),
  ).toBe(true)
})

function productionRows() {
  return Array.from({ length: 22 }, (_, index) => {
    const businessDate = `2026-08-${String(index + 1).padStart(2, '0')}`
    return {
      dimension: 'day',
      dimensionValue: businessDate,
      businessDate,
      shiftCode: index % 2 === 0 ? 'SHIFT-DAY' : 'SHIFT-NIGHT',
      workCenterId: 'WC-CNC-01',
      skuId: 'SKU-HOUSING-01',
      goodQuantity: 92 + index,
      scrapQuantity: 3,
      reworkQuantity: 5,
      totalOutputQuantity: 100 + index,
      goodRate: (92 + index) / (100 + index),
      scrapRate: 3 / (100 + index),
      reworkRate: 5 / (100 + index),
      productionReportCount: 8,
      resolutionStatus: 'resolved',
      degradedReasons: [],
    }
  })
}

function envelope<T>(data: T) {
  return { success: true, data }
}

async function fulfill(route: Route, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}
