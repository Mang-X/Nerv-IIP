import { expect, test, type Browser, type Locator } from '@playwright/test'
import {
  CREATED_MAINTENANCE_WORK_ORDER_ID,
  STORAGE_KEY,
  expectNoHorizontalOverflow,
  principal,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
  session,
  workerProfile,
} from './fixtures'

const selfWorkOrderId = (index: number) =>
  `019f1000-0000-7000-8000-${String(index).padStart(12, '0')}`

async function expectMinimumTouchHeight(locator: Locator, minimum = 44) {
  await expect(locator).toBeVisible()
  const box = await locator.boundingBox()
  expect(
    box,
    `missing touch box for ${await locator.evaluate((element) => element.outerHTML)}`,
  ).not.toBeNull()
  expect(box!.height).toBeGreaterThanOrEqual(minimum)
}

async function createContextWithPrincipal(browser: Browser, overrides: Partial<typeof principal>) {
  const context = await browser.newContext({ viewport: { width: 375, height: 812 } })
  const page = await context.newPage()
  const scopedPrincipal = { ...principal, ...overrides }
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await page.route('**/api/console/v1/auth/refresh', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, data: { ...session, principal: scopedPrincipal } }),
    }),
  )
  await page.route('**/api/console/v1/auth/me', (route) =>
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ success: true, data: scopedPrincipal }),
    }),
  )
  await page.addInitScript(({ key, stored }) => localStorage.setItem(key, JSON.stringify(stored)), {
    key: STORAGE_KEY,
    stored: {
      principal: scopedPrincipal,
      refreshToken: session.refreshToken,
      sessionId: session.sessionId,
    },
  })
  return { context, page }
}

// 网关 Mock + 已登录主体（含 org/env + loginName，见 fixtures.principal）。
test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

test('维修工单：服务端 Self 筛选与分页 → 强 ID 详情重新校验', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  const listRequests: URL[] = []
  const detailRequests: URL[] = []
  const deviceDetailRequests: URL[] = []
  let rejectFirstListResponse = true
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname.startsWith('/api/business-console/v1/master-data/resources/device-asset/')) {
      deviceDetailRequests.push(url)
    }
  })
  const workOrders = Array.from({ length: 25 }, (_, index) => ({
    workOrderId: selfWorkOrderId(index + 1),
    sourceReferenceId: `MWO-2026-${String(index + 1).padStart(4, '0')}`,
    deviceAssetId: 'DEV-CNC-01',
    priority: 'high',
    status: 'accepted',
    openedAtUtc: '2026-08-02T01:00:00.000Z',
    assignedTechnicianUserId: principal.principalId,
    assignedTeamId: 'team-a',
    version: 7,
    allowedActions: ['start', 'cancel'],
    blockReasons: ['manage-permission-required'],
    lifecycle: [
      {
        action: 'accept',
        fromStatus: 'open',
        toStatus: 'accepted',
        actorPrincipalId: principal.principalId,
        technicianUserId: principal.principalId,
        teamId: 'team-a',
        reason: '现场接单',
        resultingVersion: 7,
        occurredAtUtc: '2026-08-02T01:02:03.000Z',
      },
    ],
  }))

  await page.route('**/api/business-console/v1/maintenance/work-orders**', async (route) => {
    const url = new URL(route.request().url())
    const base = '/api/business-console/v1/maintenance/work-orders'
    if (route.request().method() !== 'GET') return route.fallback()
    if (url.pathname === base) {
      listRequests.push(url)
      if (rejectFirstListResponse) {
        rejectFirstListResponse = false
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ success: false, message: '列表暂不可用', data: null }),
        })
      }
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 20)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            items: workOrders.slice(skip, skip + take),
            total: workOrders.length,
            skip,
            take,
          },
        }),
      })
    }
    if (url.pathname === `${base}/${selfWorkOrderId(1)}`) {
      detailRequests.push(url)
      return route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ success: true, data: workOrders[0] }),
      })
    }
    return route.fallback()
  })

  await page.goto('/equipment/work-orders')
  await expect(page.getByTestId('maintenance-self-work-orders-error')).toContainText(
    '维修工单读取失败，请重试',
  )
  await expect(page.getByTestId('maintenance-work-order-row')).toHaveCount(0)
  await expect(page.getByText('当前维修人员暂无符合筛选条件的维修工单')).toHaveCount(0)
  await page.getByTestId('retry-list').click()
  await expect(page.getByTestId('task-list-meta')).toContainText('已加载 20 / 共 25')
  expect(listRequests[0].searchParams.get('scopeKind')).toBe('self')
  expect(listRequests[0].searchParams.get('scopeId')).toBe(principal.principalId)
  expect(listRequests[0].searchParams.get('skip')).toBe('0')
  expect(listRequests[0].searchParams.get('take')).toBe('20')
  await expect(page.getByTestId('maintenance-work-order-row').first()).toContainText('已接单')
  await expect(page.getByTestId('maintenance-work-order-row').first()).not.toContainText(
    'DEV-CNC-01',
  )
  await expect(page.getByTestId('maintenance-work-order-row').first()).toContainText(
    workerProfile.displayName,
  )
  await expect(page.getByTestId('maintenance-work-order-row').first()).not.toContainText(
    principal.principalId,
  )

  await page.getByRole('searchbox', { name: '维修工单关键字' }).fill('主轴')
  await page.getByRole('button', { name: '全部状态' }).click()
  await page.getByRole('button', { name: '已接单', exact: true }).click()
  await page.getByTestId('maintenance-device-filter').click()
  await page.getByTestId('device-option-019f0000-0000-7000-8000-000000000001').click()
  await expectMinimumTouchHeight(page.getByTestId('maintenance-device-clear'))
  await expect
    .poll(() => {
      const last = listRequests.at(-1)
      return last
        ? {
            scopeKind: last.searchParams.get('scopeKind'),
            scopeId: last.searchParams.get('scopeId'),
            status: last.searchParams.get('status'),
            deviceAssetIds: last.searchParams.get('deviceAssetIds'),
            keyword: last.searchParams.get('keyword'),
          }
        : undefined
    })
    .toEqual({
      scopeKind: 'self',
      scopeId: principal.principalId,
      status: 'accepted',
      deviceAssetIds: '019f0000-0000-7000-8000-000000000001,DEV-CNC-01',
      keyword: '主轴',
    })

  const scroller = page.locator('[data-slot="pull-refresh"] .nv-m-pr-scroll')
  await scroller.evaluate((element) => element.scrollTo({ top: element.scrollHeight }))
  await expect
    .poll(() => listRequests.some((url) => url.searchParams.get('skip') === '20'))
    .toBe(true)
  await expect(page.getByTestId('task-list-meta')).toContainText('已加载 25 / 共 25')

  await page.getByTestId('maintenance-work-order-row').first().focus()
  await page.getByTestId('maintenance-work-order-row').first().press('Space')
  await expect(page).toHaveURL(`/equipment/work-orders/${selfWorkOrderId(1)}`)
  await expectMinimumTouchHeight(page.getByRole('button', { name: '返回维修工单列表' }))
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('一号数控机床')
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText(
    'WS-1 · LINE-A · ST-9',
  )
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('版本 7')
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText(
    `维修人员 ${workerProfile.displayName}`,
  )
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('班组 甲班')
  await expect(page.getByTestId('maintenance-work-order-detail')).not.toContainText(
    principal.principalId,
  )
  await expect(page.getByTestId('maintenance-work-order-detail')).not.toContainText('team-a')
  await expect(page.getByTestId('maintenance-work-order-detail')).not.toContainText(
    selfWorkOrderId(1),
  )
  expect(detailRequests).toHaveLength(1)
  expect(detailRequests[0].searchParams.get('scopeKind')).toBe('self')
  expect(detailRequests[0].searchParams.get('scopeId')).toBe(principal.principalId)
  expect(deviceDetailRequests).toHaveLength(1)
  expect(deviceDetailRequests[0].pathname).toBe(
    '/api/business-console/v1/master-data/resources/device-asset/DEV-CNC-01',
  )
  expect(deviceDetailRequests[0].searchParams.get('organizationId')).toBe('org-001')
  expect(deviceDetailRequests[0].searchParams.get('environmentId')).toBe('env-dev')
  await expect(page.getByRole('button', { name: '开工', exact: true })).toHaveCount(0)
})

test('维修工单：HTTP 200 错位 skip/take 响应失败关闭且不渲染空态', async ({ page }) => {
  await page.route('**/api/business-console/v1/maintenance/work-orders**', async (route) => {
    const url = new URL(route.request().url())
    if (
      route.request().method() !== 'GET' ||
      url.pathname !== '/api/business-console/v1/maintenance/work-orders'
    ) {
      return route.fallback()
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        data: {
          items: [],
          total: 0,
          skip: 20,
          take: 20,
        },
      }),
    })
  })

  await page.goto('/equipment/work-orders')

  await expect(page.getByTestId('maintenance-self-work-orders-error')).toContainText(
    '维修工单读取失败，请重试',
  )
  await expect(page.getByTestId('maintenance-work-order-row')).toHaveCount(0)
  await expect(page.getByText('当前维修人员暂无符合筛选条件的维修工单')).toHaveCount(0)
})

test('维修工单：畸形设备资料失败关闭，重试公开 ID 后不泄露机器来源引用', async ({ page }) => {
  const workOrderId = '019f1000-0000-7000-8000-000000000099'
  const deviceAssetId = '019f0000-0000-7000-8000-000000000001'
  const deviceDetailRequests: URL[] = []
  let rejectMalformedDevice = true
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname.includes('/master-data/resources/device-asset/')) {
      deviceDetailRequests.push(url)
    }
  })
  await page.route(
    `**/api/business-console/v1/maintenance/work-orders/${workOrderId}**`,
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            workOrderId,
            sourceReferenceId: '019f2000-0000-7000-8000-000000000099',
            deviceAssetId,
            priority: 'high',
            status: 'accepted',
            openedAtUtc: '2026-08-02T01:00:00.000Z',
            version: 3,
            allowedActions: [],
            blockReasons: [],
            lifecycle: [],
            assignedTechnicianUserId: principal.principalId,
            assignedTeamId: null,
          },
        }),
      })
    },
  )
  await page.route(
    `**/api/business-console/v1/master-data/resources/device-asset/${deviceAssetId}**`,
    async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            resourceType: 'device-asset',
            deviceAssetId,
            code: 'DEV-CNC-01',
            displayName: '一号数控机床',
            organizationId: principal.organizationId,
            environmentId: principal.environmentId,
            workshopCode: rejectMalformedDevice ? { code: 'WS-INVALID' } : 'WS-01',
          },
        }),
      })
    },
  )

  await page.goto(`/equipment/work-orders/${workOrderId}`)

  await expect(page.getByTestId('maintenance-work-order-detail-error')).toContainText(
    '工单详情读取失败，请重试',
  )
  await expect(page.getByTestId('maintenance-work-order-detail')).toHaveCount(0)
  rejectMalformedDevice = false
  await page.getByTestId('maintenance-work-order-detail-error').getByRole('button').click()

  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('一号数控机床')
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('DEV-CNC-01')
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('维修工单')
  await expect(page.getByTestId('maintenance-work-order-detail')).not.toContainText(
    '019f2000-0000-7000-8000-000000000099',
  )
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText(
    `维修人员 ${workerProfile.displayName}`,
  )
  expect(deviceDetailRequests).toHaveLength(2)
  expect(
    deviceDetailRequests.every(
      (url) =>
        url.pathname ===
        `/api/business-console/v1/master-data/resources/device-asset/${deviceAssetId}`,
    ),
  ).toBe(true)
})

test('维修工单：缺少设备位置读取权限时不发请求且不声称个人队列', async ({ browser }) => {
  const { context, page } = await createContextWithPrincipal(browser, {
    permissionCodes: principal.permissionCodes.filter(
      (code) => code !== 'business.masterdata.resources.read',
    ),
  })
  try {
    const requests: string[] = []
    page.on('request', (request) => {
      const pathname = new URL(request.url()).pathname
      if (
        pathname === '/api/business-console/v1/maintenance/work-orders' ||
        pathname === '/api/business-console/v1/master-data/device-assets'
      ) {
        requests.push(pathname)
      }
    })

    await page.goto('/equipment/work-orders')

    await expect(page.getByText('当前账号暂无法查看维修工单')).toBeVisible()
    await expect(page.getByTestId('list-empty-explanation')).toContainText(
      '当前账号暂无法查看，请重新登录或联系管理员',
    )
    await expect(page.getByText('我的维修工单')).toHaveCount(0)
    expect(requests).toEqual([])
  } finally {
    await context.close()
  }
})

test('维修工单：缺少主体 ID 时列表与详情均不发业务请求', async ({ browser }) => {
  const { context, page } = await createContextWithPrincipal(browser, { principalId: '' })
  try {
    const requests: string[] = []
    page.on('request', (request) => {
      const pathname = new URL(request.url()).pathname
      if (
        pathname === '/api/business-console/v1/maintenance/work-orders' ||
        pathname.startsWith('/api/business-console/v1/maintenance/work-orders/') ||
        pathname === '/api/business-console/v1/master-data/device-assets'
      ) {
        requests.push(pathname)
      }
    })

    await page.goto('/equipment/work-orders')

    await expect(page.getByText('当前账号暂无法查看维修工单')).toBeVisible()
    await expect(page.getByTestId('list-empty-explanation')).toContainText(
      '当前账号暂无法查看，请重新登录或联系管理员',
    )
    await expect(page.getByText('我的工单')).toHaveCount(0)
    expect(requests).toEqual([])

    await page.goto('/equipment/work-orders/019f0000-0000-7000-8000-000000000203')

    await expect(page.getByRole('heading', { name: '工单不可查看' })).toBeVisible()
    await expect(page.getByText('当前账号暂无法查看，请重新登录或联系管理员。')).toBeVisible()
    expect(requests).toEqual([])
  } finally {
    await context.close()
  }
})

test('维修工单：终态矛盾事实失败关闭，修正后强 ID 回读且仅可查看', async ({ page }) => {
  const detailRequests: URL[] = []
  let rejectContradictoryDetail = true
  await page.route(
    '**/api/business-console/v1/maintenance/work-orders/019f0000-0000-7000-8000-000000000202**',
    async (route) => {
      const url = new URL(route.request().url())
      detailRequests.push(url)
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            workOrderId: '019f0000-0000-7000-8000-000000000202',
            sourceReferenceId: 'MWO-2026-CLOSED',
            deviceAssetId: 'DEV-CNC-01',
            priority: 'medium',
            status: 'closed',
            openedAtUtc: '2026-08-02T01:00:00.000Z',
            version: 12,
            allowedActions: rejectContradictoryDetail ? ['start'] : [],
            blockReasons: ['terminal-status'],
            lifecycle: [],
            assignedTechnicianUserId: principal.principalId,
            assignedTeamId: 'team-a',
          },
        }),
      })
    },
  )

  await page.goto('/equipment/work-orders/019f0000-0000-7000-8000-000000000202?sourceAlarmId=ALM-9')

  await expect(page.getByTestId('maintenance-work-order-detail-error')).toContainText(
    '工单详情读取失败，请重试',
  )
  await expect(page.getByTestId('maintenance-read-only-state')).toHaveCount(0)
  rejectContradictoryDetail = false
  await page.getByTestId('maintenance-work-order-detail-error').getByRole('button').click()
  await expect(page.getByTestId('maintenance-read-only-state')).toContainText('终态只读')
  await expect(page.getByTestId('maintenance-read-only-state')).toContainText(
    '工单已进入终态，仅可查看',
  )
  expect(detailRequests).toHaveLength(2)
  expect(detailRequests.every((url) => url.searchParams.get('scopeKind') === 'self')).toBe(true)
  expect(
    detailRequests.every((url) => url.searchParams.get('scopeId') === principal.principalId),
  ).toBe(true)
  await expect(page.getByRole('button', { name: '开工', exact: true })).toHaveCount(0)
})

test('报修：375×812 路由/扫码/设备搜索 → ActionSheet → 键盘态单次提交', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await page.setViewportSize({ width: 375, height: 812 })
  const postBodies: unknown[] = []
  page.on('request', (request) => {
    const { pathname } = new URL(request.url())
    if (
      request.method() === 'POST' &&
      pathname === '/api/business-console/v1/maintenance/work-orders'
    ) {
      postBodies.push(request.postDataJSON())
    }
  })

  const expect48 = async (locator: Locator) => {
    const subpixelTolerance = 0.001
    const box = await locator.boundingBox()
    expect(
      box,
      `missing box for ${await locator.evaluate((element) => element.outerHTML)}`,
    ).not.toBeNull()
    expect(box!.height).toBeGreaterThanOrEqual(48 - subpixelTolerance)
    expect(box!.width).toBeGreaterThanOrEqual(48 - subpixelTolerance)
  }

  await page.goto('/equipment/repair?deviceAssetId=DEV-ROUTE&sourceAlarmId=ALM-9')
  await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()
  await expect(page.getByTestId('device-trigger')).toContainText('DEV-ROUTE')
  await expect(page.getByTestId('device-trigger')).toContainText('报警上下文 · ALM-9')
  await expect(page.getByTestId('device-input')).toHaveCount(0)
  await expect(page.locator('select')).toHaveCount(0)
  await expectNoHorizontalOverflow(page)

  // 首屏当前可见交互均为 ≥48px 命中盒；扫码以组件容器作为完整命中区域。
  const scan = page.locator('input[placeholder="扫描设备码"]')
  await expect48(scan.locator('..'))
  await expect48(page.getByTestId('device-trigger'))
  await expect48(page.getByTestId('priority-trigger'))
  await expect48(page.getByTestId('reason-input'))
  await expect48(page.getByTestId('submit'))

  // ActionSheet 三项及取消均为 48px；取消保持已选值。
  await page.getByTestId('priority-trigger').click()
  const prioritySheet = page.locator('[data-slot="mobile-sheet-content"]')
  await expect(prioritySheet).toBeVisible()
  for (const label of ['高', '中', '低', '取消']) {
    await expect48(prioritySheet.getByRole('button', { name: label, exact: true }))
  }
  await prioritySheet.getByRole('button', { name: '高', exact: true }).click()
  await expect(prioritySheet).toBeHidden()
  await page.getByTestId('priority-trigger').click()
  await prioritySheet.getByRole('button', { name: '取消', exact: true }).click()
  await expect(page.getByTestId('priority-trigger')).toContainText('高')

  // 报警路由设备可被扫码替换；已有优先级与自由文本描述保持不变。
  await page.getByTestId('reason-input').fill('主轴异响，无法运转')
  await scan.click()
  await scan.pressSequentially('DEV-SCAN')
  await scan.press('Enter')
  await expect(page.getByTestId('device-trigger')).toContainText('DEV-SCAN')
  await expect(page.getByTestId('priority-trigger')).toContainText('高')
  await expect(page.getByTestId('reason-input')).toHaveValue('主轴异响，无法运转')

  // 再用现有 facade 的服务端 keyword 选择设备编码；请求保持 principal scope + 有界分页。
  await page.getByTestId('device-trigger').click()
  const deviceSheet = page.locator('[data-slot="mobile-sheet-content"]')
  const searchInput = deviceSheet.locator('input[type="search"]')
  await searchInput.fill('数控')
  await expect48(searchInput)
  await expect48(deviceSheet.getByRole('button', { name: '清除' }))
  await expect48(deviceSheet.getByRole('button', { name: '取消', exact: true }))
  const keywordRequest = page.waitForRequest((request) => {
    const url = new URL(request.url())
    return (
      url.pathname === '/api/business-console/v1/master-data/device-assets' &&
      url.searchParams.get('keyword') === '数控'
    )
  })
  await searchInput.press('Enter')
  const requestUrl = new URL((await keywordRequest).url())
  expect(requestUrl.searchParams.get('organizationId')).toBe('org-001')
  expect(requestUrl.searchParams.get('environmentId')).toBe('env-dev')
  expect(requestUrl.searchParams.get('includeDisabled')).toBe('false')
  expect(requestUrl.searchParams.get('skip')).toBe('0')
  expect(requestUrl.searchParams.get('take')).toBe('20')
  const deviceOption = deviceSheet.getByRole('button', { name: /一号数控机床/ })
  await expect(deviceOption).toContainText('DEV-CNC-01')
  await expect(deviceOption).toContainText('WS-1 · LINE-A · ST-9')
  await expect48(deviceOption)
  await deviceOption.click()
  await expect(page.getByTestId('device-trigger')).toContainText('一号数控机床')
  await expect(page.getByTestId('device-trigger')).toContainText('DEV-CNC-01')

  // 仅属 mock Chromium 证据：缩短 viewport 模拟软键盘占位，不能代表 Android/iOS 真 IME。
  await page.setViewportSize({ width: 375, height: 520 })
  const reason = page.getByTestId('reason-input')
  await reason.focus()
  await reason.fill('主轴异响，无法运转')
  const submit = page.getByTestId('submit')
  await submit.scrollIntoViewIfNeeded()
  const submitBox = await submit.boundingBox()
  expect(submitBox).not.toBeNull()
  expect(submitBox!.y + submitBox!.height).toBeLessThanOrEqual(520)
  await submit.click()

  // 单击只产生一次 create；Maintenance 使用 facade 返回的设备编码，报警 ID 保持 route-only。
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('报修已提交')).toBeVisible()
  await expect(page.getByTestId('view-created-work-order')).toHaveCount(0)
  expect(postBodies).toEqual([
    {
      deviceAssetId: 'DEV-CNC-01',
      priority: 'high',
      assetUnavailableReason: '主轴异响，无法运转',
      sourceAlarmId: 'ALM-9',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      openedBy: 'operator01',
      idempotencyKey: expect.any(String),
    },
  ])
})

test('点检：选保养计划 → 选「通过」→ 提交 → 成功 Result', async ({ page }) => {
  await page.goto('/equipment/inspect')
  await expect(page.getByRole('heading', { name: '点检', exact: true })).toBeVisible()

  // 选择保养计划（PM-001 ← PLAN-1）
  await page.getByText('PM-001').click()
  // 选结果「通过」（pass → 通过）
  await page.getByTestId('result-pass').click()

  await page.getByTestId('submit').click()

  // 成功离场态（POST inspections → { inspectionId: 'INS-new' }）
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('点检已记录')).toBeVisible()
})

// MAN-458 #812：数字键盘录入（含 ± 负号）+ 超差即时警示 + 提交前汇总确认。真实 Chromium /
// Pixel 5 视口验证 jsdom 测不到的：Teleport 键盘浮层、计算样式红警示、触点尺寸、ScanBar 抢焦。
test('点检：数字键盘录入（含负号）+ 超差警示 + 提交确认', async ({ page }) => {
  // 键盘/弹窗过渡置 none：组件 @media(prefers-reduced-motion) 走 transition:none，
  // Teleport + Transition 的离场即时移除，消除 headless 下 transitionend 滞留（测行为非动画）。
  await page.emulateMedia({ reducedMotion: 'reduce' })

  await page.goto('/equipment/inspect')
  await page.getByText('PM-001').click()
  await page.getByTestId('result-pass').click()

  // 特性 + 单位：**真实 tap + fill**。文本获焦时页面停用 ScanBar 回焦（focusin），故正常录入
  // 不再被 ScanBar 抢走（#812 戴手套可完成录入的核心验收，不再靠原生 setter 假绿）。
  const characteristic = page.getByTestId('measurement-characteristic')
  await characteristic.click()
  await characteristic.fill('轴承温度')
  await expect(characteristic).toHaveValue('轴承温度')
  const uom = page.getByTestId('measurement-uom')
  await uom.click()
  await uom.fill('C')
  await expect(uom).toHaveValue('C')

  // 数字键盘录入（只读 Cell 触发，防系统键盘）：下限 0 / 上限 70 / 测量值 -80（± 负号 →
  // 低于下限超差）。键盘是底部 sheet + fixed inset-0 背板：字段间须「完成」收起再点下一格。
  const keyboard = page.locator('[data-slot="number-keyboard"]')
  const enterViaKeyboard = async (cell: string, digits: string) => {
    await page.getByTestId(cell).click()
    await expect(keyboard).toBeVisible()
    for (const d of digits) {
      await keyboard.getByRole('button', { name: d, exact: true }).click()
    }
  }
  const closeKeyboard = async () => {
    await keyboard.getByRole('button', { name: '完成' }).last().click()
    await expect(keyboard).toBeHidden()
  }

  await enterViaKeyboard('measurement-lower', '0')
  // 戴手套触点 ≥44px：数字键（键盘开着时量）+ 提交动作「完成」键（删除头部小按钮后仅剩
  // 底部大键，此前 E2E 只量数字键与 Cell、漏了提交动作，本处补上）。
  const digitBox = await keyboard.getByRole('button', { name: '8', exact: true }).boundingBox()
  expect(digitBox!.height).toBeGreaterThanOrEqual(44)
  expect(digitBox!.width).toBeGreaterThanOrEqual(44)
  const doneButtons = await keyboard.getByRole('button', { name: '完成' }).all()
  expect(doneButtons).toHaveLength(1)
  const doneBox = await doneButtons[0].boundingBox()
  expect(doneBox!.height).toBeGreaterThanOrEqual(44)
  expect(doneBox!.width).toBeGreaterThanOrEqual(44)
  await closeKeyboard()

  await enterViaKeyboard('measurement-upper', '70')
  await closeKeyboard()

  // 测量值：± → 8 → 0 = -80（负号回归覆盖；-80 < 下限 0 → 超差）。
  await page.getByTestId('measurement-value').click()
  await expect(keyboard).toBeVisible()
  await keyboard.getByRole('button', { name: '正负号' }).click()
  await keyboard.getByRole('button', { name: '8', exact: true }).click()
  await keyboard.getByRole('button', { name: '0', exact: true }).click()
  await closeKeyboard()
  await expect(page.getByTestId('measurement-value-text')).toHaveText('-80')

  // 测量值 Cell 触点 ≥44px。
  const cellBox = await page.getByTestId('measurement-value').boundingBox()
  expect(cellBox!.height).toBeGreaterThanOrEqual(44)

  // 超差即时警示：红标 + 数值变红 + 规格公差呈现；移动视口无横向溢出。
  await expect(page.getByTestId('out-of-tolerance')).toBeVisible()
  await expect(page.getByTestId('measurement-value-text')).toHaveClass(/text-destructive/)
  await expect(page.getByTestId('spec-range')).toHaveText('0 ~ 70 C')
  await expectNoHorizontalOverflow(page)

  // 提交 → 超差汇总确认「1 项测量值超差」→ 仍要提交 → 成功离场。
  await page.getByTestId('submit').click()
  const dialog = page.locator('[data-slot="mobile-dialog-content"]')
  await expect(dialog).toContainText('1 项测量值超差')
  await dialog.getByRole('button', { name: '仍要提交' }).click()
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('点检已记录')).toBeVisible()
})

test('报警 → 报修 → 已确认强 ID 详情：真实入口保留上下文并按 Self 重校验', async ({ page }) => {
  const detailRequests: URL[] = []
  let createdWorkOrderAssigned = false
  await page.route(
    `**/api/business-console/v1/maintenance/work-orders/${CREATED_MAINTENANCE_WORK_ORDER_ID}**`,
    async (route) => {
      const url = new URL(route.request().url())
      detailRequests.push(url)
      if (!createdWorkOrderAssigned) {
        await route.fulfill({
          status: 403,
          contentType: 'application/json',
          body: JSON.stringify({
            success: false,
            message: '新建工单尚未指派给当前维修人员',
            data: null,
          }),
        })
        return
      }
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          data: {
            workOrderId: CREATED_MAINTENANCE_WORK_ORDER_ID,
            sourceReferenceId: 'MWO-2026-CREATED',
            deviceAssetId: 'DEV-A',
            priority: 'high',
            status: 'open',
            openedAtUtc: '2026-08-02T02:00:00.000Z',
            version: 1,
            allowedActions: ['accept'],
            blockReasons: [],
            lifecycle: [],
            assignedTechnicianUserId: principal.principalId,
            assignedTeamId: null,
            sourceAlarmId: 'ALM-1',
          },
        }),
      })
    },
  )
  await page.goto('/equipment/alarms')
  await expect(page.getByRole('heading', { name: '查看报警' })).toBeVisible()

  // 报警行渲染：设备 + 报警码 + 级别中文（严重，而非工程语言 'critical'）
  await expect(page.getByText('DEV-A · 报警码 E-101')).toBeVisible()
  await expect(page.getByText('严重')).toBeVisible()
  await expect(page.getByText('critical')).toHaveCount(0)

  // 去报修承载在行详情抽屉内（MAN-456 从行内移入详情）：先开详情再点。
  await page.getByTestId('detail-ALM-1').click()
  await page.getByTestId('repair-ALM-1').click()
  await expect(page).toHaveURL(/\/equipment\/repair\?/)
  const url = new URL(page.url())
  expect(url.pathname).toBe('/equipment/repair')
  expect(url.searchParams.get('deviceAssetId')).toBe('DEV-A')
  expect(url.searchParams.get('sourceAlarmId')).toBe('ALM-1')

  // 穿透后报修页设备已预填
  await expect(page.getByTestId('device-trigger')).toContainText('DEV-A')
  await expect(page.getByTestId('device-trigger')).toContainText('报警上下文 · ALM-1')

  await page.getByTestId('priority-trigger').click()
  await page.getByRole('button', { name: '高', exact: true }).click()
  await page.getByTestId('submit').click()
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByTestId('created-work-order-assignment-state')).toContainText(
    '工单指派状态暂不可核实',
  )
  await expect(page.getByTestId('view-created-work-order')).toHaveCount(0)

  // 创建回执真实保持未指派；此处模拟外部派工，再由用户显式回读 Self 详情。
  createdWorkOrderAssigned = true
  await page.getByTestId('recheck-created-work-order-assignment').click()
  await expect(page.getByTestId('created-work-order-assignment-state')).toContainText(
    '已确认工单指派给当前维修人员',
  )
  await expect(page.getByTestId('view-created-work-order')).toBeVisible()
  await page.getByTestId('view-created-work-order').click()

  await expect(page).toHaveURL(
    new RegExp(`/equipment/work-orders/${CREATED_MAINTENANCE_WORK_ORDER_ID}\\?`),
  )
  const detailUrl = new URL(page.url())
  expect(detailUrl.searchParams.get('source')).toBeNull()
  expect(detailUrl.searchParams.get('sourceAlarmId')).toBe('ALM-1')
  await expect(page.getByTestId('maintenance-source-context')).toHaveText('来源：报警报修创建结果')
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText('装配线冲压机')
  await expect(page.getByTestId('maintenance-work-order-detail')).toContainText(
    'WS-1 · LINE-A · ST-1',
  )
  expect(detailRequests.length).toBeGreaterThanOrEqual(2)
  for (const request of detailRequests) {
    expect(request.searchParams.get('scopeKind')).toBe('self')
    expect(request.searchParams.get('scopeId')).toBe(principal.principalId)
  }
})

test('首页 → 报修：点应用墙「报修」跳 /equipment/repair', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('home-name')).toBeVisible()

  await page.getByRole('button', { name: '报修' }).click()
  await expect(page).toHaveURL('/equipment/repair')
  await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()
})
