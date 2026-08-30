import { expect, test, type Route } from '@playwright/test'

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

test('设备工程师分站点查看业务日趋势并核对同名班次', async ({ page }, testInfo) => {
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
        envelope(dimension === 'shift' ? shiftResponse(url) : businessDayResponse(url)),
      )
    }
    return fulfill(route, envelope({ items: [], total: 0 }))
  }
  await page.route('**/api/console/v1/**', routeApi)
  await page.route('**/api/business-console/v1/**', routeApi)

  await page.goto(
    '/equipment/telemetry/oee?deviceAssetId=DEV-CNC-01&windowStartUtc=2026-03-01T00:00:00.000Z&windowEndUtc=2026-04-01T00:00:00.000Z',
    { waitUntil: 'domcontentloaded' },
  )
  expect(pageErrors).toEqual([])

  await expect(page.getByText('OEE 与 A/P/Q 业务日趋势')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText(/完整窗口共 65 个业务日聚合桶，按\s*2 个站点分别呈现/)).toBeVisible()
  await expect(page.getByRole('heading', { name: '站点 SITE-SUZHOU', exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: '站点 SITE-DETROIT', exact: true })).toBeVisible()
  await expect(page.getByText('34 个桶，33 个完整率值点，1 个缺失点保留在核查表。')).toBeVisible()
  await expect(page.getByText('31 个桶，31 个完整率值点。', { exact: true })).toBeVisible()
  await expect(page.getByText('1 个桶缺少率值，未画成 0%')).toBeVisible()
  await expect(page.getByText('横轴使用业务日“月/日”短标签。')).toBeVisible()
  await expect(page.getByText('SITE-SUZHOU · OEE', { exact: true })).toHaveCount(3)
  await expect(page.getByText('SITE-DETROIT · OEE', { exact: true })).toHaveCount(1)
  await expect(page.getByText('3/1', { exact: true })).toHaveCount(3)
  await expect(page.getByText('3/31', { exact: true })).toHaveCount(2)
  await expect(page.getByText('数据不完整', { exact: true })).toHaveCount(0)
  await expect(page.getByPlaceholder('设备资产编号')).toHaveValue('DEV-CNC-01')

  const suzhouPanel = page.locator('[data-oee-site="SITE-SUZHOU"]')
  await expect(suzhouPanel.locator('[data-oee-segment]')).toHaveCount(2)
  await expect(
    suzhouPanel.getByText('首桶 UTC：2026-02-28 15:00:00 UTC – 2026-03-01 15:00:00 UTC', {
      exact: true,
    }),
  ).toBeVisible()
  await expect(
    suzhouPanel.getByText('首桶 UTC：2026-02-28 16:00:00 UTC – 2026-03-01 16:00:00 UTC', {
      exact: true,
    }),
  ).toBeVisible()

  const detroitPanel = page.locator('[data-oee-site="SITE-DETROIT"]')
  await detroitPanel.getByText('查看逐桶 UTC 窗口').click()
  await expect(
    detroitPanel.getByText('2026-03-08：2026-03-08 05:00:00 UTC – 2026-03-09 04:00:00 UTC', {
      exact: true,
    }),
  ).toBeVisible()
  await detroitPanel.getByText('查看逐桶 UTC 窗口').click()
  await expect(suzhouPanel.locator('[data-oee-run]')).toHaveCount(3)

  const suzhouChart = page.locator('[data-oee-site="SITE-SUZHOU"]').getByRole('figure').first()
  for (const xRatio of [0.15, 0.35, 0.55, 0.75]) {
    const chartBox = await suzhouChart.boundingBox()
    expect(chartBox).not.toBeNull()
    await suzhouChart.hover({
      position: { x: chartBox!.width * xRatio, y: chartBox!.height * 0.5 },
    })
    if (await page.locator('.nv-vis-card').count()) break
  }
  await expect(
    page.locator('.nv-vis-card').filter({ hasText: 'SITE-SUZHOU · OEE' }).last(),
  ).toBeVisible()
  await page.screenshot({
    path: testInfo.outputPath('issue-2819-oee-day-trend.png'),
    fullPage: true,
  })

  await page.getByRole('button', { name: '第 3 页', exact: true }).click()
  await expect(page.getByText('缺少或存在冲突的工序标准速率')).toBeVisible()
  await expect(page.getByText('2026-03-26', { exact: true }).first()).toBeVisible()
  await expect(page.getByRole('heading', { name: '站点 SITE-SUZHOU', exact: true })).toBeVisible()

  await page.getByRole('combobox', { name: '报表视角' }).click()
  await page.getByRole('option', { name: '班次横比' }).click()
  await page.keyboard.press('Escape')
  await expect(page.getByPlaceholder('设备资产编号')).toHaveValue('')
  await expect(page.getByText('SHIFT-DAY', { exact: true })).toHaveCount(2)
  await expect(
    page.getByText('站点 SITE-SUZHOU › 车间 WORKSHOP-MACHINING › 产线 LINE-CNC', {
      exact: true,
    }),
  ).toBeVisible()
  await expect(
    page.getByText('站点 SITE-DETROIT › 车间 WORKSHOP-ASSEMBLY › 产线 LINE-FINAL', {
      exact: true,
    }),
  ).toBeVisible()
  await page.waitForTimeout(500)
  await page.screenshot({
    path: testInfo.outputPath('issue-2819-oee-shift-comparison.png'),
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
  expect(aggregateRequests.some((url) => url.searchParams.get('dimension') === 'shift')).toBe(true)
  expect(
    aggregateRequests
      .filter((url) => url.searchParams.get('dimension') === 'shift')
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
  const primaryBuckets = Array.from({ length: 31 }, (_, index) => {
    const businessDate = `2026-03-${String(index + 1).padStart(2, '0')}`
    const previousDate = isoDate(new Date(Date.UTC(2026, 2, index)))
    const nextDate = isoDate(new Date(Date.UTC(2026, 2, index + 2)))
    const suzhou = bucket('day', 'SITE-SUZHOU', businessDate, 0.781, 0.86, 0.93, 0.625, {
      siteCode: 'SITE-SUZHOU',
      bucketStartUtc: `${previousDate}T16:00:00.000Z`,
      bucketEndUtc: `${businessDate}T16:00:00.000Z`,
    })
    const detroit = bucket('day', 'SITE-DETROIT', businessDate, 0.88, 0.91, 0.975, 0.781, {
      siteCode: 'SITE-DETROIT',
      bucketStartUtc: `${businessDate}T${index < 8 ? '05' : '04'}:00:00.000Z`,
      bucketEndUtc: `${nextDate}T${index < 7 ? '05' : '04'}:00:00.000Z`,
    })
    return index === 25
      ? [
          {
            ...suzhou,
            performanceRate: null,
            oeeRate: null,
            isDegraded: true,
            degradedReasons: ['theoreticalRateMissingOrAmbiguous'],
          },
          detroit,
        ]
      : [suzhou, detroit]
  }).flat()
  const tokyoHistory = Array.from({ length: 3 }, (_, index) => {
    const businessDate = `2026-03-0${index + 1}`
    return bucket('day', 'SITE-SUZHOU', businessDate, 0.79, 0.88, 0.94, 0.654, {
      siteCode: 'SITE-SUZHOU',
      bucketStartUtc: `${isoDate(new Date(Date.UTC(2026, 2, index)))}T15:00:00.000Z`,
      bucketEndUtc: `${businessDate}T15:00:00.000Z`,
    })
  })
  const allBuckets = [...primaryBuckets, ...tokyoHistory]
  const skip = Number(url.searchParams.get('skip') ?? 0)
  const take = Number(url.searchParams.get('take') ?? 20)
  return aggregateResponse(url, 'day', allBuckets.slice(skip, skip + take), allBuckets.length)
}

function shiftResponse(url: URL) {
  return aggregateResponse(url, 'shift', [
    bucket('shift', 'SHIFT-DAY', '2026-03-24', 0.846, 0.904, 0.975, 0.746, {
      siteCode: 'SITE-SUZHOU',
      workshopCode: 'WORKSHOP-MACHINING',
      lineCode: 'LINE-CNC',
      shiftCode: 'SHIFT-DAY',
      bucketStartUtc: '2026-03-24T00:00:00.000Z',
      bucketEndUtc: '2026-03-24T12:00:00.000Z',
    }),
    bucket('shift', 'SHIFT-DAY', '2026-03-24', 0.91, 0.868, 0.989, 0.781, {
      siteCode: 'SITE-DETROIT',
      workshopCode: 'WORKSHOP-ASSEMBLY',
      lineCode: 'LINE-FINAL',
      shiftCode: 'SHIFT-DAY',
      bucketStartUtc: '2026-03-24T12:00:00.000Z',
      bucketEndUtc: '2026-03-25T00:00:00.000Z',
    }),
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
  overrides: Record<string, unknown> = {},
) {
  const day = businessDate ?? '2026-03-24'
  return {
    dimension,
    dimensionValue,
    siteCode: 'SITE-SUZHOU',
    workshopCode: null,
    lineCode: null,
    workCenterId: null,
    deviceAssetId: null,
    shiftCode: null,
    businessDate,
    bucketStartUtc: `${day}T00:00:00.000Z`,
    bucketEndUtc: `${isoDate(new Date(`${day}T00:00:00.000Z`).getTime() + 86_400_000)}T00:00:00.000Z`,
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
    ...overrides,
  }
}

function isoDate(value: Date | number) {
  return new Date(value).toISOString().slice(0, 10)
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
