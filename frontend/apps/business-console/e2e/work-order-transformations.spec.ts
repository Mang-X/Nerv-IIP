import { expect, test, type Page, type Route } from '@playwright/test'

const STORAGE_KEY = 'nerv-iip.business-console.auth'

const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
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

const defaultWorkOrderRows = [
  {
    workOrderId: 'WO-MERGE-1',
    workOrderNo: 'WO-MERGE-1',
    skuId: 'SKU-001',
    productionVersionId: 'PV-001',
    quantity: 2,
    status: 'created',
    operationTasks: [],
  },
  {
    workOrderId: 'WO-MERGE-2',
    workOrderNo: 'WO-MERGE-2',
    skuId: 'SKU-001',
    productionVersionId: 'PV-001',
    quantity: 3,
    status: 'released',
    operationTasks: [],
  },
]

test.beforeEach(async ({ page }) => {
  await seedStoredSession(page)
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

test('工单详情拆分：Chromium HTTP lane 覆盖 accepted、回读重试、错误、409 与非法零写', async ({
  page,
}, testInfo) => {
  // 证据拓扑：真实 Chromium 页面 + Playwright route mock Gateway；捕获浏览器实际
  // 请求/响应。该 lane 不代表真实 Gateway/RBAC/PostgreSQL，三者均未运行。
  let splitScenario: 'accepted' | 'error' | 'conflict' = 'accepted'
  let readbackAttempts = 0
  const httpRequests: Array<{ method: string; pathname: string; body?: unknown }> = []
  const httpResponses: Array<{ method: string; pathname: string; status: number; body?: unknown }> =
    []

  page.on('request', (request) => {
    const url = new URL(request.url())
    if (
      url.pathname.endsWith('/work-orders/WO-001/split') ||
      url.pathname.includes('/work-order-transformations/')
    ) {
      let body: unknown
      try {
        body = request.postDataJSON()
      } catch {
        body = request.postData() ?? undefined
      }
      httpRequests.push({ method: request.method(), pathname: url.pathname, body })
    }
  })
  page.on('response', async (response) => {
    const url = new URL(response.url())
    if (
      url.pathname.endsWith('/work-orders/WO-001/split') ||
      url.pathname.includes('/work-order-transformations/')
    ) {
      let body: unknown
      try {
        body = await response.json()
      } catch {
        body = undefined
      }
      httpResponses.push({
        method: response.request().method(),
        pathname: url.pathname,
        status: response.status(),
        body,
      })
    }
  })

  await page.route('**/api/business-console/v1/mes/work-orders/WO-001/split**', async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname !== '/api/business-console/v1/mes/work-orders/WO-001/split') {
      return route.fallback()
    }
    if (splitScenario === 'error') {
      return fulfillJsonWithStatus(route, 500, { message: '拆分服务暂时不可用' })
    }
    if (splitScenario === 'conflict') {
      return fulfillJsonWithStatus(route, 409, { message: 'work-order transformation conflict' })
    }
    return fulfillJson(
      route,
      envelope({
        accepted: true,
        transformationId: 'TX-SPLIT',
        type: 'Split',
        sourceWorkOrderIds: ['WO-001'],
        targetWorkOrderIds: ['WO-CHILD-1', 'WO-CHILD-2'],
        isIdempotentReplay: false,
        operationReceipt: {
          operationId: 'OP-SPLIT',
          operationType: 'WorkOrderSplit',
          acceptedAtUtc: '2026-08-26T02:00:00.000Z',
        },
      }),
    )
  })
  await page.route(
    '**/api/business-console/v1/mes/work-order-transformations/TX-SPLIT**',
    async (route) => {
      const url = new URL(route.request().url())
      if (url.pathname !== '/api/business-console/v1/mes/work-order-transformations/TX-SPLIT') {
        return route.fallback()
      }
      readbackAttempts += 1
      if (readbackAttempts === 1) {
        return fulfillJsonWithStatus(route, 503, { message: '回读结果暂不可用' })
      }
      return fulfillJson(
        route,
        envelope({
          transformationId: 'TX-SPLIT',
          type: 'Split',
          idempotencyKey: 'split-work-order-WO-001-e2e',
          actor: 'principal-1',
          reason: '按客户批次拆分',
          occurredAtUtc: '2026-08-26T02:00:01.000Z',
          lines: [
            {
              sourceWorkOrderId: 'WO-001',
              targetWorkOrderId: 'WO-CHILD-1',
              quantity: 4,
              uomCode: 'PCS',
              sourceStatus: 'released',
              targetStatus: 'created',
              sourceVersion: 1,
              targetVersion: 1,
            },
          ],
        }),
      )
    },
  )

  await page.goto('/mes/work-orders/WO-001', { waitUntil: 'domcontentloaded' })
  const splitButton = page.getByTestId('open-split-work-order')
  await expect(splitButton).toBeVisible({ timeout: 15_000 })
  await expect(splitButton).toBeEnabled()
  await splitButton.click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()

  // 非法数量只产生前端校验，浏览器不应发出任何 split mutation。
  await dialog.getByLabel('子工单 1 标识').fill('WO-CHILD-1')
  await dialog.getByLabel('数量', { exact: true }).nth(0).fill('0')
  await dialog.getByLabel('子工单 2 标识').fill('WO-CHILD-2')
  await dialog.getByLabel('数量', { exact: true }).nth(1).fill('10')
  await dialog.getByLabel('拆分原因').fill('按客户批次拆分')
  await dialog.getByTestId('submit-work-order-transformation').click({ force: true })
  await expect(dialog.getByTestId('transformation-validation-errors')).toContainText(
    '第 1 个子工单数量必须大于 0。',
  )
  await expect
    .poll(
      () =>
        httpRequests.filter((item) => item.pathname.endsWith('/work-orders/WO-001/split')).length,
    )
    .toBe(0)

  // 正例返回 accepted，但首次 readback 为 503；页面必须保留 accepted 并提供重试。
  await dialog.getByLabel('数量', { exact: true }).nth(0).fill('4')
  await dialog.getByLabel('数量', { exact: true }).nth(1).fill('6')
  await dialog.getByTestId('submit-work-order-transformation').click({ force: true })
  await expect(dialog.getByTestId('transformation-status')).toHaveAttribute(
    'data-state',
    'accepted',
  )
  await expect(dialog.getByTestId('transformation-status')).toContainText('结果尚未完成回读')
  await expect(dialog.getByTestId('retry-transformation-readback')).toBeVisible()

  await dialog.getByTestId('retry-transformation-readback').click({ force: true })
  await expect(dialog.getByTestId('transformation-status')).toHaveAttribute('data-state', 'success')
  await expect(dialog.getByTestId('transformation-readback')).toContainText('WO-001 → WO-CHILD-1')
  await expect(
    httpRequests.find((item) => item.pathname.endsWith('/work-orders/WO-001/split')),
  ).toMatchObject({
    method: 'POST',
    body: {
      targets: [
        { workOrderId: 'WO-CHILD-1', quantity: 4 },
        { workOrderId: 'WO-CHILD-2', quantity: 6 },
      ],
      reason: '按客户批次拆分',
    },
  })

  // 普通 500 与 409 均从真实响应映射到相应页面状态。
  await dialog.getByRole('button', { name: '关闭' }).first().click({ force: true })
  await expect(dialog).toBeHidden()
  splitScenario = 'error'
  await splitButton.click()
  await dialog.getByLabel('子工单 1 标识').fill('WO-CHILD-1')
  await dialog.getByLabel('数量', { exact: true }).nth(0).fill('4')
  await dialog.getByLabel('子工单 2 标识').fill('WO-CHILD-2')
  await dialog.getByLabel('数量', { exact: true }).nth(1).fill('6')
  await dialog.getByLabel('拆分原因').fill('按客户批次拆分')
  await dialog.getByTestId('submit-work-order-transformation').click({ force: true })
  await expect(dialog.getByTestId('transformation-status')).toHaveAttribute('data-state', 'error')
  await expect(dialog.getByTestId('transformation-status')).toContainText('拆分服务暂时不可用')

  await dialog.getByRole('button', { name: '关闭' }).first().click({ force: true })
  await expect(dialog).toBeHidden()
  splitScenario = 'conflict'
  await splitButton.click()
  await dialog.getByLabel('子工单 1 标识').fill('WO-CHILD-1')
  await dialog.getByLabel('数量', { exact: true }).nth(0).fill('4')
  await dialog.getByLabel('子工单 2 标识').fill('WO-CHILD-2')
  await dialog.getByLabel('数量', { exact: true }).nth(1).fill('6')
  await dialog.getByLabel('拆分原因').fill('按客户批次拆分')
  await dialog.getByTestId('submit-work-order-transformation').click({ force: true })
  await expect(dialog.getByTestId('transformation-status')).toHaveAttribute(
    'data-state',
    'conflict',
  )
  await expect(dialog.getByTestId('transformation-status')).toContainText('409')

  await expect
    .poll(() =>
      httpResponses
        .filter(
          (item) =>
            item.pathname.endsWith('/work-orders/WO-001/split') ||
            item.pathname.includes('/work-order-transformations/TX-SPLIT'),
        )
        .map((item) => item.status),
    )
    .toEqual([200, 503, 200, 500, 409])
  await testInfo.attach('work-order-split-http.json', {
    body: JSON.stringify({ requests: httpRequests, responses: httpResponses }, null, 2),
    contentType: 'application/json',
  })
})

test('工单列表合并：同 UOM 的真实 UI 流程覆盖 accepted、回读重试、错误和 409', async ({
  page,
}, testInfo) => {
  // 证据拓扑：真实 Chromium 列表页面 + Playwright route mock Gateway；SKU 主数据读面
  // 返回 baseUomCode=PCS，工单列表只返回 PR-C 已发布字段。该 lane 不代表真实
  // Gateway/RBAC/PostgreSQL，三者均未运行。
  let mergeScenario: 'accepted' | 'error' | 'conflict' = 'accepted'
  let readbackAttempts = 0
  const httpRequests: Array<{ method: string; pathname: string; body?: unknown }> = []
  const httpResponses: Array<{ method: string; pathname: string; status: number; body?: unknown }> =
    []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (
      url.pathname.endsWith('/work-orders/merge') ||
      url.pathname.includes('/work-order-transformations/TX-MERGE')
    ) {
      let body: unknown
      try {
        body = request.postDataJSON()
      } catch {
        body = request.postData() ?? undefined
      }
      httpRequests.push({ method: request.method(), pathname: url.pathname, body })
    }
  })
  page.on('response', async (response) => {
    const url = new URL(response.url())
    if (
      url.pathname.endsWith('/work-orders/merge') ||
      url.pathname.includes('/work-order-transformations/TX-MERGE')
    ) {
      let body: unknown
      try {
        body = await response.json()
      } catch {
        body = undefined
      }
      httpResponses.push({
        method: response.request().method(),
        pathname: url.pathname,
        status: response.status(),
        body,
      })
    }
  })

  await page.route('**/api/business-console/v1/mes/work-orders/merge**', async (route) => {
    const url = new URL(route.request().url())
    if (url.pathname !== '/api/business-console/v1/mes/work-orders/merge') {
      return route.fallback()
    }
    if (mergeScenario === 'error') {
      return fulfillJsonWithStatus(route, 500, { message: '合并服务暂时不可用' })
    }
    if (mergeScenario === 'conflict') {
      return fulfillJsonWithStatus(route, 409, { message: 'work-order transformation conflict' })
    }
    return fulfillJson(
      route,
      envelope({
        accepted: true,
        transformationId: 'TX-MERGE',
        type: 'Merge',
        sourceWorkOrderIds: ['WO-MERGE-1', 'WO-MERGE-2'],
        targetWorkOrderIds: ['WO-MERGE-TARGET'],
        isIdempotentReplay: false,
        operationReceipt: {
          operationId: 'OP-MERGE',
          operationType: 'WorkOrderMerge',
          acceptedAtUtc: '2026-08-26T02:01:00.000Z',
        },
      }),
    )
  })
  await page.route(
    '**/api/business-console/v1/mes/work-order-transformations/TX-MERGE**',
    async (route) => {
      const url = new URL(route.request().url())
      if (url.pathname !== '/api/business-console/v1/mes/work-order-transformations/TX-MERGE') {
        return route.fallback()
      }
      readbackAttempts += 1
      if (readbackAttempts === 1) {
        return fulfillJsonWithStatus(route, 503, { message: '合并结果暂不可用' })
      }
      return fulfillJson(
        route,
        envelope({
          transformationId: 'TX-MERGE',
          type: 'Merge',
          idempotencyKey: 'merge-work-orders-e2e',
          actor: 'principal-1',
          reason: '同 SKU 小单合并',
          occurredAtUtc: '2026-08-26T02:01:01.000Z',
          lines: [
            {
              sourceWorkOrderId: 'WO-MERGE-1',
              targetWorkOrderId: 'WO-MERGE-TARGET',
              quantity: 2,
              uomCode: 'PCS',
              sourceStatus: 'created',
              targetStatus: 'created',
              sourceVersion: 1,
              targetVersion: 1,
            },
          ],
        }),
      )
    },
  )

  await page.goto('/mes/work-orders', { waitUntil: 'domcontentloaded' })
  await expect(page.getByText('WO-MERGE-1')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('WO-MERGE-2')).toBeVisible({ timeout: 15_000 })
  await selectListRows(page)
  const mergeButton = page.getByTestId('open-merge-work-orders')
  await expect(mergeButton).toBeEnabled({ timeout: 15_000 })
  await mergeButton.click()

  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByText('2 PCS')).toBeVisible()
  await expect(dialog.getByText('3 PCS')).toBeVisible()
  await dialog.locator('#merge-target-work-order').fill('WO-MERGE-TARGET')
  await dialog.locator('#merge-reason').fill('同 SKU 小单合并')
  await dialog.getByTestId('submit-work-order-transformation').click({ force: true })
  await expect(dialog.getByTestId('transformation-status')).toHaveAttribute(
    'data-state',
    'accepted',
  )
  await expect(dialog.getByTestId('transformation-status')).toContainText('结果尚未完成回读')
  await expect(dialog.getByTestId('retry-transformation-readback')).toBeVisible()
  await expect(dialog.getByTestId('transformation-readback')).toHaveCount(0)
  await dialog.getByTestId('retry-transformation-readback').click({ force: true })
  await expect(dialog.getByTestId('transformation-status')).toHaveAttribute('data-state', 'success')
  await expect(dialog.getByTestId('transformation-readback')).toContainText(
    'WO-MERGE-1 → WO-MERGE-TARGET',
  )
  await expect(
    httpRequests.find((item) => item.pathname.endsWith('/work-orders/merge')),
  ).toMatchObject({
    method: 'POST',
    body: {
      sourceWorkOrderIds: ['WO-MERGE-1', 'WO-MERGE-2'],
      targetWorkOrderId: 'WO-MERGE-TARGET',
      reason: '同 SKU 小单合并',
    },
  })

  await dialog.getByRole('button', { name: '关闭' }).first().click({ force: true })
  await expect(dialog).toBeHidden()
  mergeScenario = 'error'
  await selectListRows(page)
  await mergeButton.click()
  await dialog.locator('#merge-target-work-order').fill('WO-MERGE-TARGET')
  await dialog.locator('#merge-reason').fill('同 SKU 小单合并')
  await dialog.getByTestId('submit-work-order-transformation').click({ force: true })
  await expect(dialog.getByTestId('transformation-status')).toHaveAttribute('data-state', 'error')
  await expect(dialog.getByTestId('transformation-status')).toContainText('合并服务暂时不可用')

  await dialog.getByRole('button', { name: '关闭' }).first().click({ force: true })
  await expect(dialog).toBeHidden()
  mergeScenario = 'conflict'
  await selectListRows(page)
  await mergeButton.click()
  await dialog.locator('#merge-target-work-order').fill('WO-MERGE-TARGET')
  await dialog.locator('#merge-reason').fill('同 SKU 小单合并')
  await dialog.getByTestId('submit-work-order-transformation').click({ force: true })
  await expect(dialog.getByTestId('transformation-status')).toHaveAttribute(
    'data-state',
    'conflict',
  )
  await expect(dialog.getByTestId('transformation-status')).toContainText('409')

  await expect
    .poll(() =>
      httpResponses
        .filter(
          (item) =>
            item.pathname.endsWith('/work-orders/merge') ||
            item.pathname.includes('/work-order-transformations/TX-MERGE'),
        )
        .map((item) => item.status),
    )
    .toEqual([200, 503, 200, 500, 409])
  await testInfo.attach('work-order-merge-http.json', {
    body: JSON.stringify({ requests: httpRequests, responses: httpResponses }, null, 2),
    contentType: 'application/json',
  })
})

test('工单列表合并：缺少 UOM 时 fail-closed 且不发送写请求', async ({ page }, testInfo) => {
  // 证据拓扑：真实 Chromium 列表页面 + Playwright route mock Gateway；列表响应遵循
  // PR-C 当前工单契约，SKU-MISSING 不在已发布 SKU 主数据读面中，因此 UOM 未知。
  // 该 lane 不代表真实 Gateway/RBAC/PostgreSQL，三者均未运行。
  const mergeRequests: Array<{ pathname: string; body?: unknown }> = []
  page.on('request', (request) => {
    const url = new URL(request.url())
    if (url.pathname === '/api/business-console/v1/mes/work-orders/merge') {
      mergeRequests.push({ pathname: url.pathname, body: request.postDataJSON() })
    }
  })
  await page.route('**/api/business-console/v1/mes/work-orders**', async (route) => {
    const url = new URL(route.request().url())
    if (
      route.request().method() === 'GET' &&
      url.pathname === '/api/business-console/v1/mes/work-orders'
    ) {
      return fulfillJson(
        route,
        envelope({
          items: [
            {
              workOrderId: 'WO-MISSING-UOM-1',
              workOrderNo: 'WO-MISSING-UOM-1',
              skuId: 'SKU-MISSING',
              productionVersionId: 'PV-001',
              quantity: 2,
              status: 'created',
              operationTasks: [],
            },
            {
              workOrderId: 'WO-MISSING-UOM-2',
              workOrderNo: 'WO-MISSING-UOM-2',
              skuId: 'SKU-MISSING',
              productionVersionId: 'PV-001',
              quantity: 3,
              status: 'released',
              operationTasks: [],
            },
          ],
          total: 2,
        }),
      )
    }
    return route.fallback()
  })
  await page.route('**/api/business-console/v1/mes/work-orders/merge**', async (route) => {
    mergeRequests.push({
      pathname: new URL(route.request().url()).pathname,
      body: route.request().postDataJSON(),
    })
    return fulfillJsonWithStatus(route, 500, { message: '不应发送合并请求' })
  })

  await page.goto('/mes/work-orders', { waitUntil: 'domcontentloaded' })
  await expect(page.getByText('WO-MISSING-UOM-1')).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('WO-MISSING-UOM-2')).toBeVisible({ timeout: 15_000 })
  await selectListRows(page)

  const mergeButton = page.getByTestId('open-merge-work-orders')
  await expect(mergeButton).toBeDisabled()
  await expect(page.getByTestId('merge-unit-unavailable')).toContainText(
    '未返回单位信息，无法确认数量单位；请刷新列表后重试。',
  )
  await expect.poll(() => mergeRequests.length).toBe(0)
  await testInfo.attach('work-order-merge-missing-uom.json', {
    body: JSON.stringify({
      requests: mergeRequests,
      selected: ['WO-MISSING-UOM-1', 'WO-MISSING-UOM-2'],
    }),
    contentType: 'application/json',
  })
})

async function selectListRows(page: Page) {
  const rowCheckboxes = page.getByRole('checkbox', { name: '选择行' })
  await expect(rowCheckboxes).toHaveCount(2)
  for (const index of [0, 1]) {
    const checkbox = rowCheckboxes.nth(index)
    if (!(await checkbox.isChecked())) await checkbox.click()
  }
}

async function seedStoredSession(page: Page) {
  await page.addInitScript(
    ({ key, storedSession }) => {
      localStorage.setItem(key, JSON.stringify(storedSession))
    },
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
  const url = new URL(route.request().url())
  if (url.pathname === '/api/console/v1/auth/refresh') {
    return fulfillJson(route, envelope(session))
  }
  if (url.pathname === '/api/console/v1/auth/me') {
    return fulfillJson(route, envelope(principal))
  }
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
        resolvedAtUtc: '2026-08-26T01:00:00.000Z',
        principal: { id: principal.principalId, principalType: principal.principalType },
        resolutionStatus: 'resolved',
        authorizedScopes: [scope],
        availableScopeKinds: ['organization'],
        selectedScope: scope,
        issues: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/master-data/skus') {
    return fulfillJson(
      route,
      envelope({
        resources: [
          {
            resourceType: 'sku',
            code: 'SKU-001',
            displayName: '前减振器总成',
            baseUomCode: 'PCS',
            active: true,
            snapshotVersion: 'v1',
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/master-data/resources') {
    const resourceType = url.searchParams.get('resourceType') ?? 'work-center'
    return fulfillJson(
      route,
      envelope({
        resources: [
          {
            resourceType,
            code: `${resourceType.toUpperCase()}-001`,
            displayName: `${resourceType} 主数据`,
            active: true,
            snapshotVersion: 'v1',
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders') {
    return fulfillJson(route, envelope({ items: defaultWorkOrderRows, total: 2 }))
  }

  if (pathname === '/api/business-console/v1/mes/work-orders/WO-001') {
    return fulfillJson(
      route,
      envelope({
        workOrderId: 'WO-001',
        skuId: 'SKU-001',
        quantity: 10,
        status: 'released',
        readinessStatus: 'Ready',
        blockingReasons: [],
        operationTasks: [
          {
            operationTaskId: 'op-1',
            workOrderId: 'WO-001',
            status: 'Ready',
            operationSequence: 10,
            workCenterId: 'WC-001',
            qualityStatus: 'Ready',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders/WO-001/material-readiness') {
    return fulfillJson(
      route,
      envelope({ workOrderId: 'WO-001', readinessStatus: 'Ready', blockingReasons: [], items: [] }),
    )
  }

  return fulfillJson(route, envelope({ items: [] }))
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

async function fulfillJsonWithStatus(route: Route, status: number, body: unknown) {
  await route.fulfill({
    status,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}
