import { expect, test, type Page, type Route } from '@playwright/test'

const STORAGE_KEY = 'nerv-iip.business-console.auth'
const principal = {
  principalId: 'principal-equipment-engineer',
  principalType: 'User',
  loginName: 'equipment.engineer',
  email: 'equipment.engineer@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: ['business.iiot.telemetry.read'],
}
const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-oee-report',
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

test('设备工程师查看业务日趋势并切换工作中心横比', async ({ page }, testInfo) => {
  const aggregateRequests: URL[] = []
  const pageErrors: string[] = []
  page.on('pageerror', (error) => pageErrors.push(error.message))
  const routeApi = async (route: Route) => {
    const url = new URL(route.request().url())
    if (url.pathname === '/api/console/v1/auth/refresh') return fulfill(route, envelope(session))
    if (url.pathname === '/api/console/v1/auth/me') return fulfill(route, envelope(principal))
    if (url.pathname === '/api/business-console/v1/me/work-context') {
      const scope = { kind: 'organization', id: 'org-001', displayName: '苏州精密制造工厂' }
      return fulfill(
        route,
        envelope({
          organizationId: 'org-001',
          environmentId: 'env-dev',
          applicablePermissionCode: url.searchParams.get('permissionCode'),
          resolvedAtUtc: '2026-08-30T01:00:00.000Z',
          principal: { id: principal.principalId, principalType: principal.principalType },
          resolutionStatus: 'resolved',
          authorizedScopes: [scope],
          availableScopeKinds: ['organization'],
          selectedScope: scope,
          issues: [],
        }),
      )
    }
    if (url.pathname === '/api/business-console/v1/telemetry/oee/aggregates') {
      aggregateRequests.push(url)
      const dimension = url.searchParams.get('dimension') ?? 'day'
      return fulfill(
        route,
        envelope(dimension === 'workCenter' ? workCenterResponse(url) : businessDayResponse(url)),
      )
    }
    return fulfill(route, envelope({ items: [], total: 0 }))
  }
  await page.route('**/api/console/v1/**', routeApi)
  await page.route('**/api/business-console/v1/**', routeApi)

  await page.goto(
    '/equipment/telemetry/oee?deviceAssetId=DEV-CNC-01&windowStartUtc=2026-08-01T00:00:00.000Z&windowEndUtc=2026-09-01T00:00:00.000Z',
    { waitUntil: 'domcontentloaded' },
  )
  expect(pageErrors).toEqual([])

  await expect(page.getByText('OEE 与 A/P/Q 业务日趋势')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('完整窗口共 31 个业务日聚合桶。')).toBeVisible()
  await expect(page.getByText('2026-08-07 · SITE-SUZHOU')).toBeVisible()
  await expect(page.getByText('缺少或存在冲突的工序标准速率')).toBeVisible()
  await expect(page.getByText('1 个桶缺少率值，未画成 0%')).toBeVisible()
  await expect(page.getByPlaceholder('设备资产编号')).toHaveValue('DEV-CNC-01')
  await page.screenshot({
    path: testInfo.outputPath('issue-2819-oee-day-trend.png'),
    fullPage: true,
  })

  await page.getByRole('combobox', { name: '报表视角' }).click()
  await page.getByRole('option', { name: '工作中心横比' }).click()
  await page.keyboard.press('Escape')
  await expect(page.getByPlaceholder('设备资产编号')).toHaveValue('')
  await expect(page.getByText('WC-CNC-5AXIS')).toBeVisible()
  await expect(page.getByText('WC-ASSEMBLY-FINAL')).toBeVisible()
  await page.waitForTimeout(500)
  await page.screenshot({
    path: testInfo.outputPath('issue-2819-oee-work-center-comparison.png'),
    fullPage: true,
  })

  expect(aggregateRequests.some((url) => url.searchParams.get('dimension') === 'day')).toBe(true)
  expect(
    aggregateRequests.some(
      (url) =>
        url.searchParams.get('dimension') === 'day' &&
        url.searchParams.get('take') === '100' &&
        url.searchParams.get('deviceAssetId') === 'DEV-CNC-01',
    ),
  ).toBe(true)
  expect(aggregateRequests.some((url) => url.searchParams.get('dimension') === 'workCenter')).toBe(
    true,
  )
  expect(
    aggregateRequests
      .filter((url) => url.searchParams.get('dimension') === 'workCenter')
      .every((url) => !url.searchParams.has('deviceAssetId')),
  ).toBe(true)
  expect(
    aggregateRequests.every(
      (url) =>
        url.searchParams.get('organizationId') === 'org-001' &&
        url.searchParams.get('environmentId') === 'env-dev',
    ),
  ).toBe(true)
})

function businessDayResponse(url: URL) {
  const allBuckets = Array.from({ length: 31 }, (_, index) => {
    const businessDate = `2026-08-${String(index + 1).padStart(2, '0')}`
    if (index === 6) {
      return {
        ...bucket('day', 'SITE-SUZHOU', businessDate, 0.799, null, 0.96, null),
        isDegraded: true,
        degradedReasons: ['theoreticalRateMissingOrAmbiguous'],
      }
    }
    return bucket('day', 'SITE-SUZHOU', businessDate, 0.781, 0.86, 0.93, 0.625)
  })
  const skip = Number(url.searchParams.get('skip') ?? 0)
  const take = Number(url.searchParams.get('take') ?? 20)
  return aggregateResponse(url, 'day', allBuckets.slice(skip, skip + take), allBuckets.length)
}

function workCenterResponse(url: URL) {
  return aggregateResponse(url, 'workCenter', [
    bucket('workCenter', 'WC-CNC-5AXIS', null, 0.846, 0.904, 0.975, 0.746),
    bucket('workCenter', 'WC-ASSEMBLY-FINAL', null, 0.91, 0.868, 0.989, 0.781),
    bucket('workCenter', 'WC-PAINT-LINE', null, 0.773, 0.831, 0.957, 0.615),
  ])
}

function aggregateResponse(
  url: URL,
  dimension: string,
  buckets: Array<Record<string, unknown>>,
  totalCount = buckets.length,
) {
  return {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    dimension,
    windowStartUtc: url.searchParams.get('windowStartUtc'),
    windowEndUtc: url.searchParams.get('windowEndUtc'),
    buckets,
    totalCount,
    skip: Number(url.searchParams.get('skip') ?? 0),
    take: Number(url.searchParams.get('take') ?? 20),
  }
}

function bucket(
  dimension: string,
  dimensionValue: string,
  businessDate: string | null,
  availabilityRate: number,
  performanceRate: number | null,
  qualityRate: number,
  oeeRate: number | null,
) {
  const day = businessDate ?? '2026-08-24'
  return {
    dimension,
    dimensionValue,
    siteCode: 'SITE-SUZHOU',
    workshopCode: dimension === 'workCenter' ? 'WORKSHOP-MACHINING' : null,
    lineCode: dimension === 'workCenter' ? 'LINE-CNC' : null,
    workCenterId: dimension === 'workCenter' ? dimensionValue : null,
    deviceAssetId: null,
    shiftCode: null,
    businessDate,
    bucketStartUtc: `${day}T00:00:00.000Z`,
    bucketEndUtc: `${day}T23:59:59.000Z`,
    deviceCount: dimension === 'workCenter' ? 3 : 9,
    stateSampleCount: 864,
    productionFactCount: 36,
    availabilityRate,
    performanceRate,
    qualityRate,
    oeeRate,
    goodQuantity: 1840,
    scrapQuantity: 24,
    reworkQuantity: 11,
    outputUomCode: 'PCS',
    expectedOutputQuantity: 2100,
    isDegraded: false,
    degradedReasons: [],
  }
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
