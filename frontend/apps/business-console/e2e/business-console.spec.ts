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
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

test('business console smoke pages render', async ({ page }) => {
  await expectHeading(page, '/master-data/skus', '物料与产品')
  await expectHeading(page, '/master-data/partners', '业务伙伴')
  await expectHeading(page, '/inventory/availability', '库存可用量')
  await expectHeading(page, '/quality/ncrs', '不合格品处理')
  await expectHeading(page, '/mes', '生产驾驶舱')
  await expectHeading(page, '/mes/foundation', '生产准备检查')
  await expectHeading(page, '/mes/plans', '生产计划')
  await expectHeading(page, '/mes/work-orders', '工单与派工')
  await expectHeading(page, '/mes/work-orders/WO-001', '工单 WO-001')
  await expectHeading(page, '/mes/materials', '领料与齐套')
  await expectHeading(page, '/mes/dispatch', '派工看板')
  await expectHeading(page, '/mes/operation-tasks', '工序执行')
  await expectHeading(page, '/mes/production-reports', '报工记录')
  await expectHeading(page, '/mes/quality', '质量与不良')
  await expectHeading(page, '/mes/receipts', '完工入库')
  await expectHeading(page, '/mes/schedules', '规则排程')
  await expectHeading(page, '/mes/downtime', '设备与停机')
  await expectHeading(page, '/mes/handovers', '班次交接')
  await expectHeading(page, '/mes/traceability', '追溯查询')
  await expectHeading(page, '/mes/capacity', '产能影响')
})

test('生产计划：就绪计划行尾「转工单」可点并打开下达工单弹窗', async ({ page }) => {
  // 计划数据来自共享 mock（PLAN-READY 可转 / PLAN-BLOCKED 受阻）。
  await page.goto('/mes/plans', { waitUntil: 'domcontentloaded' })
  await expect(page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '生产计划' })).toBeVisible({ timeout: 15_000 })
  // 确认计划数据已渲染。
  await expect(page.getByText('PLAN-READY')).toBeVisible({ timeout: 15_000 })

  // 就绪计划：行尾「转工单」按钮可见可点。
  const convertBtn = page.getByRole('button', { name: '转工单' })
  await expect(convertBtn).toBeVisible()
  await expect(convertBtn).toBeEnabled()

  // 点开「下达工单」弹窗（抓"点不了"）。exact 避免匹配到「确认下达工单」按钮。
  await convertBtn.click()
  await expect(page.getByText('下达工单', { exact: true })).toBeVisible()
})

// 标题渲染为面包屑当前页 <span data-slot="breadcrumb-page">；侧栏激活链接也带 aria-current="page"，
// 故用 data-slot 精确定位。SPA 用 domcontentloaded 更稳（默认 load 可能因 HMR 长连接不触发）。
test('工单与派工：工单队列渲染、创建急单弹窗可开', async ({ page }) => {
  // 工单数据来自共享 mock（WO-001）。
  await page.goto('/mes/work-orders', { waitUntil: 'domcontentloaded' })
  await expect(page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '工单与派工' })).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('WO-001')).toBeVisible({ timeout: 15_000 })

  // 创建急单 → 弹窗打开（抓"点不了"）。
  await page.getByRole('button', { name: '创建急单' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByText('急单用于生产插单和临时补单')).toBeVisible()
})

test('领料与齐套：领料申请渲染收料进度与「查看出库」闭环链接', async ({ page }) => {
  // 领料申请数据来自共享 mock（MIR-001，已收 4 / 应领 10，关联 WMS-OUT-001）。
  await page.goto('/mes/materials', { waitUntil: 'domcontentloaded' })
  await expect(page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '领料与齐套' })).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('MIR-001')).toBeVisible({ timeout: 15_000 })
  // 收料进度可读（已收 4）。
  await expect(page.getByText(/已收\s*4/)).toBeVisible()
  // 领料闭环：出库单「查看出库」可点、跳 WMS（不显 GUID）。
  await expect(page.getByRole('link', { name: '查看出库' })).toHaveAttribute('href', /\/wms\/outbound/)
})

test('工序执行：队列渲染、可报工行直显「报工」按钮且能进报工弹窗', async ({ page }) => {
  // 工序任务数据来自共享 mock（op-1：Ready + WO-001 → 可报工）。
  await page.goto('/mes/operation-tasks', { waitUntil: 'domcontentloaded' })
  await expect(page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '工序执行' })).toBeVisible({ timeout: 15_000 })

  // 可报工行：行尾直显「报工」按钮（不必翻下拉）。
  const reportBtn = page.getByRole('button', { name: '报工' }).first()
  await expect(reportBtn).toBeVisible({ timeout: 15_000 })

  // 点「报工」→ 跳工单页并自动打开报工弹窗（抓"点不了"+跨页带参）。
  await reportBtn.click()
  await expect(page.getByRole('dialog')).toBeVisible({ timeout: 15_000 })
})

test('报工记录：报工历史渲染产量、查看工单就地速览不跳页', async ({ page }) => {
  // 报工记录数据来自共享 mock（report-1：WO-001，良品 5）。
  await page.goto('/mes/production-reports', { waitUntil: 'domcontentloaded' })
  await expect(page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '报工记录' })).toBeVisible({ timeout: 15_000 })
  // 产量可读（良品 5）。
  await expect(page.getByText(/良品\s*5/)).toBeVisible({ timeout: 15_000 })
  // 查看工单 → 就地弹出「工单速览」，URL 不变（不跳页、不打断操作）。
  await page.getByRole('button', { name: '查看工单' }).first().click()
  await expect(page.getByRole('dialog').filter({ hasText: '工单速览' })).toBeVisible({ timeout: 15_000 })
  await expect(page).toHaveURL(/\/mes\/production-reports/)
})

test('完工入库：直接开为只读、回链工单；带工单上下文进来自动开登记弹窗', async ({ page }) => {
  // 直接打开：登记需从工单详情带上下文，按钮禁用并提示「从工单详情发起」。
  await page.goto('/mes/receipts', { waitUntil: 'domcontentloaded' })
  await expect(page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '完工入库' })).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('查看工单').first()).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('button', { name: '从工单详情发起' })).toBeDisabled()

  // 带工单上下文进来（模拟工单完工跳转）→ 登记弹窗自动打开（抓跨页带参+可登记）。
  await page.goto('/mes/receipts?workOrderId=WO-001&skuId=sku-1&quantity=5', { waitUntil: 'domcontentloaded' })
  await expect(page.getByRole('dialog')).toBeVisible({ timeout: 15_000 })
})

test('在制跟踪：在制进度渲染、查看工单就地速览不跳页', async ({ page }) => {
  // 在制数据来自共享 mock（WO-001：已产 5 / 计划 10）。
  await page.goto('/mes/wip', { waitUntil: 'domcontentloaded' })
  await expect(page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '在制跟踪' })).toBeVisible({ timeout: 15_000 })
  // 在制进度可读（已产 5）。
  await expect(page.getByText(/已产\s*5/)).toBeVisible({ timeout: 15_000 })
  // 查看工单 → 就地弹出「工单速览」，URL 不变（不跳页、不打断操作）。
  await page.getByRole('button', { name: '查看工单' }).first().click()
  await expect(page.getByRole('dialog').filter({ hasText: '工单速览' })).toBeVisible({ timeout: 15_000 })
  await expect(page).toHaveURL(/\/mes\/wip/)
})

async function expectHeading(page: Page, path: string, heading: string) {
  await page.goto(path, { waitUntil: 'domcontentloaded' })
  // 慢的 dev 环境 + 连续多页导航，放宽到 15s。
  await expect(page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: heading })).toBeVisible({ timeout: 15_000 })
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

  if (pathname === '/api/business-console/v1/master-data/skus') {
    return fulfillJson(
      route,
      envelope({
        resources: [
          {
            resourceType: 'sku',
            code: 'SKU-001',
            displayName: '前减振器总成',
            active: true,
            snapshotVersion: 'v1',
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/master-data/resources') {
    const resourceType = url.searchParams.get('resourceType') ?? 'site'
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

  if (pathname === '/api/business-console/v1/inventory/availability') {
    return fulfillJson(
      route,
      envelope({
        skuCode: 'SKU-001',
        uomCode: 'EA',
        onHandQuantity: 12,
        reservedQuantity: 2,
        availableQuantity: 10,
        lines: [
          {
            locationCode: 'A-01',
            qualityStatus: 'available',
            ownerType: 'owned',
            onHandQuantity: 12,
            availableQuantity: 10,
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/quality/ncrs') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            id: 'ncr-1',
            code: 'NCR-001',
            status: 'open',
            summary: 'Dimension out of tolerance',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/work-orders') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-001',
            skuId: 'SKU-001',
            quantity: 10,
            priority: 1,
            dueUtc: '2026-05-25T12:00:00.000Z',
            status: 'released',
            operationTasks: [
              {
                operationTaskId: 'op-1',
                status: 'ready',
                operationSequence: 10,
                workCenterId: 'WC-001',
              },
            ],
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/overview') {
    return fulfillJson(
      route,
      envelope({
        counts: [
          { key: 'WorkOrders', count: 1, status: 'Released' },
          { key: 'OperationTasks', count: 1, status: 'Ready' },
        ],
        blockers: [],
        pendingWork: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/foundation-readiness') {
    return fulfillJson(
      route,
      envelope({
        status: 'Ready',
        areas: [{ areaCode: 'master-data', status: 'Ready', issues: [] }],
        blockingIssues: [],
        warningIssues: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/production-plans') {
    return fulfillJson(
      route,
      envelope({
        items: [
          { productionPlanId: 'PLAN-READY', sourceSystem: 'sales', skuId: 'sku-1', plannedQuantity: 10, uomCode: 'EA', readinessStatus: 'Ready', plannedStartUtc: '2026-06-01T08:00:00.000Z', blockingReasons: [] },
          { productionPlanId: 'PLAN-BLOCKED', sourceSystem: 'forecast', skuId: 'sku-2', plannedQuantity: 5, readinessStatus: 'Blocked', blockingReasons: ['material_shortage'] },
        ],
        total: 2,
      }),
    )
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
      envelope({
        workOrderId: 'WO-001',
        readinessStatus: 'Ready',
        blockingReasons: [],
        items: [],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/operation-tasks') {
    return fulfillJson(
      route,
      envelope({
        items: [
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

  if (pathname === '/api/business-console/v1/mes/material-issue-requests') {
    return fulfillJson(
      route,
      envelope({
        items: [
          { requestId: 'MIR-001', workOrderId: 'WO-001', materialId: 'mat-1', requestedQuantity: 10, receivedQuantity: 4, status: 'PartiallyReceived', wmsRequestId: 'WMS-OUT-001', requestedAtUtc: '2026-06-01T08:00:00.000Z' },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/dispatch-tasks') {
    return fulfillJson(route, envelope({ items: [] }))
  }

  if (pathname === '/api/business-console/v1/mes/related-quality-items') {
    return fulfillJson(route, envelope({ items: [] }))
  }

  if (pathname === '/api/business-console/v1/mes/downtime-events') {
    return fulfillJson(route, envelope({ items: [] }))
  }

  if (pathname === '/api/business-console/v1/mes/shift-handovers') {
    return fulfillJson(route, envelope({ items: [] }))
  }

  if (pathname.startsWith('/api/business-console/v1/mes/traceability/')) {
    return fulfillJson(route, envelope({ nodes: [], edges: [] }))
  }

  if (pathname === '/api/business-console/v1/mes/wip') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            workOrderId: 'WO-001',
            operationTaskId: 'op-1',
            workCenterId: 'WC-001',
            status: 'Ready',
            plannedQuantity: 10,
            goodQuantity: 5,
            scrapQuantity: 0,
            blockingReasons: [],
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/production-reports') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            productionReportId: 'report-1',
            workOrderId: 'WO-001',
            operationTaskId: 'op-1',
            goodQuantity: 5,
            scrapQuantity: 0,
            reportedAtUtc: '2026-05-25T13:00:00.000Z',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/finished-goods-receipt-requests') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            receiptRequestId: 'receipt-1',
            workOrderId: 'WO-001',
            skuId: 'SKU-001',
            quantity: 5,
            receiptStatus: 'Pending',
            requestedAtUtc: '2026-05-25T14:00:00.000Z',
          },
        ],
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/capacity-impacts') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            impactId: 'impact-1',
            workCenterId: 'WC-001',
            deviceAssetId: 'DEV-001',
            status: 'Active',
            effectiveFromUtc: '2026-05-25T15:00:00.000Z',
            reasonCode: 'MAINTENANCE',
          },
        ],
      }),
    )
  }

  return fulfillJson(route, envelope({}))
}

function envelope<T>(data: T) {
  return {
    success: true,
    data,
  }
}

async function fulfillJson(route: Route, body: unknown) {
  await route.fulfill({
    status: 200,
    contentType: 'application/json',
    body: JSON.stringify(body),
  })
}
