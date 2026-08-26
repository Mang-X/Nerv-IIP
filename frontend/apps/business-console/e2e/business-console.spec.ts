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
  // 只授予被 e2e 覆盖、且页面 meta 声明了 requiredPermissions 的能力码；
  // 未声明的页面本来就放行，这里只做加法，不会收紧任何已有用例。
  // 路由守卫（src/router/guards/auth.ts）逐页比对 meta.requiredPermissions，
  // 缺一个码整页跳 /forbidden，故本 spec 访问到的页面须逐条列全。
  permissionCodes: [
    'business.erp.sales.read', // /erp/sales(/*)
    'business.erp.finance.read', // /erp/finance(/*)
    'business.masterdata.products.read', // /master-data/skus
    'business.masterdata.resources.read', // /master-data/partners
    'business.inventory.ledger.read', // /inventory/availability
    'business.quality.ncr.read', // /quality/ncrs
    'business.mes.overview.read', // /mes
    'business.mes.foundation.read', // /mes/foundation
    'business.mes.plans.read', // /mes/plans
    'business.mes.work-orders.read', // /mes/work-orders(/:id)
    'business.mes.materials.read', // /mes/materials
    'business.mes.dispatch.read', // /mes/dispatch
    'business.mes.operations.read', // /mes/operation-tasks, /mes/wip
    'business.mes.reporting.read', // /mes/production-reports
    'business.mes.quality.read', // /mes/quality
    'business.mes.receipts.read', // /mes/receipts
    // 完工入库用例断言的是 manage 用户才渲染的登记入口（receipts.vue 里 canManageReceipts
    // 控制「从工单详情发起」按钮与带 query 自动开弹窗），故额外给操作级 manage 码。
    'business.mes.receipts.manage',
    'business.mes.schedules.read', // /mes/schedules
    'business.mes.downtime.read', // /mes/downtime
    'business.mes.handovers.read', // /mes/handovers
    'business.mes.traceability.read', // /mes/traceability
    'business.mes.capacity.read', // /mes/capacity
  ],
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

// 回归：`pages/erp/sales.vue` 与 `pages/erp/sales/` 同名并存，曾让 sales.vue 变成父路由，
// 子页没有出口 → /erp/sales/orders 等三个 URL 全部渲染「销售机会」。finance 同型。
test('经营管理：销售/财务子页各自渲染，侧栏只高亮当前项', async ({ page }) => {
  const sections: Array<[path: string, heading: string, navItem: string]> = [
    ['/erp/sales', '销售机会', '销售机会'],
    ['/erp/sales/quotations', '销售报价', '销售报价'],
    ['/erp/sales/orders', '销售订单', '销售订单'],
    ['/erp/sales/deliveries', '销售发货', '销售发货'],
    ['/erp/finance', '财务摘要', '财务摘要'],
    ['/erp/finance/ar-ap', 'AR/AP', 'AR/AP'],
    ['/erp/finance/vouchers', '会计凭证', '会计凭证'],
    ['/erp/finance/cost-candidates', '成本候选', '成本候选'],
  ]

  for (const [path, heading, navItem] of sections) {
    await expectHeading(page, path, heading)

    // 侧栏双高亮回归：只有目标项 active，节区首页项不得同时点亮。
    const active = page.locator('[data-sidebar="menu-button"][data-active="true"]')
    await expect(active).toHaveCount(1, { timeout: 15_000 })
    await expect(active).toHaveText(navItem)
  }
})

test('生产计划：就绪计划行尾「转工单」可点并打开下达工单弹窗', async ({ page }) => {
  // 计划数据来自共享 mock（PLAN-READY 可转 / PLAN-BLOCKED 受阻）。
  await page.goto('/mes/plans', { waitUntil: 'domcontentloaded' })
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '生产计划' }),
  ).toBeVisible({ timeout: 15_000 })
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
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '工单与派工' }),
  ).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('WO-001')).toBeVisible({ timeout: 15_000 })

  // 创建急单 → 弹窗打开（抓"点不了"）。
  await page.getByRole('button', { name: '创建急单' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await expect(page.getByText('急单用于生产插单和临时补单')).toBeVisible()
})

test('领料与齐套：领料申请渲染收料进度与「查看出库」闭环链接', async ({ page }) => {
  // 领料申请数据来自共享 mock（MIR-001，已收 4 / 应领 10，关联 WMS-OUT-001）。
  await page.goto('/mes/materials', { waitUntil: 'domcontentloaded' })
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '领料与齐套' }),
  ).toBeVisible({ timeout: 15_000 })
  await expect(page.getByText('MIR-001')).toBeVisible({ timeout: 15_000 })
  // 收料进度可读（已收 4）。
  await expect(page.getByText(/已收\s*4/)).toBeVisible()
  // 领料闭环：出库单「查看出库」可点、跳 WMS（不显 GUID）。
  await expect(page.getByRole('link', { name: '查看出库' })).toHaveAttribute(
    'href',
    /\/wms\/outbound/,
  )
  await expect(page.getByRole('heading', { name: '线边库存余额与账龄' })).toBeVisible()
  await expect(
    page.getByText('SKU-DAMPER-001', { exact: true }).filter({ visible: true }),
  ).toBeVisible()
  await expect(page.getByText(/在手 120 pcs/).filter({ visible: true })).toBeVisible()
  await expect(
    page.getByText(/4 天（部分批次缺少生产日期）/).filter({ visible: true }),
  ).toBeVisible()
  await expect(
    page.getByText('账龄未知（批次缺少生产日期）', { exact: true }).filter({ visible: true }),
  ).toBeVisible()
  const lineSideInventory = page.locator('section[aria-labelledby="line-side-inventory-title"]')
  await expect(lineSideInventory.locator('nav[aria-label="分页"]')).toBeVisible()
  await expect(lineSideInventory.locator('nav[aria-label="分页"]')).toHaveCount(1)
  if ((page.viewportSize()?.width ?? 0) >= 768) {
    await expect(lineSideInventory.locator('tbody tr')).toHaveCount(200)
    await expect(
      lineSideInventory.getByText('SKU-PAGE-200', { exact: true }).filter({ visible: true }),
    ).toBeVisible()
  }
  await expect(lineSideInventory).toContainText('第 1 / 2 页')
  await lineSideInventory.getByRole('button', { name: '下一页' }).click()
  await expect(
    lineSideInventory.getByText('SKU-PAGE-201', { exact: true }).filter({ visible: true }),
  ).toBeVisible()
  await expect(lineSideInventory).toContainText('第 2 / 2 页')
  await lineSideInventory.getByRole('button', { name: '上一页' }).click()
  await expect(
    lineSideInventory.getByText('SKU-DAMPER-001', { exact: true }).filter({ visible: true }),
  ).toBeVisible()
  await expect(lineSideInventory).toContainText('第 1 / 2 页')
  const mobileInventory = page.getByTestId('line-side-inventory-mobile')
  if ((page.viewportSize()?.width ?? 0) < 768) {
    await expect(mobileInventory).toBeVisible()
    await expect(mobileInventory).toContainText('4 天（部分批次缺少生产日期）')
  } else {
    await expect(mobileInventory).toBeHidden()
  }
})

test('领料与齐套：第 2 页失败后保留下方分页并可返回第 1 页', async ({ page }) => {
  await page.route(
    '**/api/business-console/v1/mes/line-side-inventory-balances*',
    async (route) => {
      const requestedPage = Number(new URL(route.request().url()).searchParams.get('page') ?? 1)
      if (requestedPage === 2) {
        return fulfillJson(route, {
          success: false,
          message: '第 2 页库存暂不可用',
          data: null,
        })
      }
      return fulfillJson(
        route,
        envelope({
          items: [
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A01',
              skuCode: 'SKU-RECOVERY-PAGE-1',
              uomCode: 'pcs',
              onHandQuantity: 12,
              reservedQuantity: 2,
              availableQuantity: 10,
              lotCount: 1,
              oldestProductionDate: '2026-08-25',
              ageDays: 1,
              ageCompleteness: 'complete',
            },
          ],
          totalCount: 201,
          page: 1,
          pageSize: 200,
          asOfDate: '2026-08-26',
        }),
      )
    },
  )

  await page.goto('/mes/materials', { waitUntil: 'domcontentloaded' })
  const lineSideInventory = page.locator('section[aria-labelledby="line-side-inventory-title"]')
  await expect(
    lineSideInventory.getByText('SKU-RECOVERY-PAGE-1', { exact: true }).filter({ visible: true }),
  ).toBeVisible({ timeout: 15_000 })
  await lineSideInventory.getByRole('button', { name: '下一页' }).click()

  await expect(lineSideInventory.getByRole('alert')).toContainText('第 2 页库存暂不可用')
  await expect(lineSideInventory.locator('nav[aria-label="分页"]')).toBeVisible()
  await expect(lineSideInventory.getByRole('button', { name: '上一页' })).toBeEnabled()
  await lineSideInventory.getByRole('button', { name: '上一页' }).click()
  await expect(
    lineSideInventory.getByText('SKU-RECOVERY-PAGE-1', { exact: true }).filter({ visible: true }),
  ).toBeVisible()
  await expect(lineSideInventory).toContainText('第 1 / 2 页')
})

test('工序执行：队列渲染、可报工行直显「报工」按钮且能进报工弹窗', async ({ page }) => {
  // 工序任务数据来自共享 mock（op-1：Ready + WO-001 → 可报工）。
  await page.goto('/mes/operation-tasks', { waitUntil: 'domcontentloaded' })
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '工序执行' }),
  ).toBeVisible({ timeout: 15_000 })

  // 可报工行：行尾直显「报工」按钮（不必翻下拉）。
  const reportBtn = page.getByRole('button', { name: '报工' }).first()
  await expect(reportBtn).toBeVisible({ timeout: 15_000 })

  // 点「报工」→ 就地打开报工弹窗（不跳页），上下文随行带出（抓"点不了"+上下文丢失）。
  await reportBtn.click()
  const reportDialog = page.getByRole('dialog')
  await expect(reportDialog).toBeVisible({ timeout: 15_000 })
  await expect(page).toHaveURL(/\/mes\/operation-tasks/)
  await expect(reportDialog.locator('[data-slot="carried-context"]')).toContainText('WO-001')
})

test('报工记录：报工历史渲染产量、查看工单就地速览不跳页', async ({ page }) => {
  // 报工记录数据来自共享 mock（report-1：WO-001，良品 5）。
  await page.goto('/mes/production-reports', { waitUntil: 'domcontentloaded' })
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '报工记录' }),
  ).toBeVisible({ timeout: 15_000 })
  // 产量可读（良品 5）。
  await expect(page.getByText(/良品\s*5/)).toBeVisible({ timeout: 15_000 })
  // 点工单号（WO-001）→ 就地弹出「工单速览」，URL 不变（不跳页、不打断操作）。
  await page.getByRole('button', { name: 'WO-001' }).first().click()
  await expect(page.getByRole('dialog').filter({ hasText: '工单速览' })).toBeVisible({
    timeout: 15_000,
  })
  await expect(page).toHaveURL(/\/mes\/production-reports/)
})

test('MES 实际工时读面在工序与报工页使用同一累计口径', async ({ page }, testInfo) => {
  await page.goto('/mes/operation-tasks', { waitUntil: 'domcontentloaded' })
  const operationActualHours = page.locator('[data-testid="actual-hours"]').first()
  await expect(operationActualHours).toContainText('人工')
  await expect(operationActualHours).toContainText('1.25 小时')
  await expect(operationActualHours).toContainText('机器')
  await expect(operationActualHours).toContainText('0.5 小时')
  await page.screenshot({ path: testInfo.outputPath('operation-actual-hours.png') })

  await page.goto('/mes/production-reports', { waitUntil: 'domcontentloaded' })
  const reportActualHours = page.locator('[data-testid="actual-hours"]').first()
  await expect(reportActualHours).toContainText('人工')
  await expect(reportActualHours).toContainText('2.75 小时')
  await expect(reportActualHours).toContainText('机器')
  await expect(reportActualHours).toContainText('1.5 小时')
  await page.screenshot({ path: testInfo.outputPath('report-actual-hours.png') })
})

test('完工入库：直接开为只读、回链工单；带工单上下文进来自动开登记弹窗', async ({ page }) => {
  // 直接打开：登记需从工单详情带上下文，按钮禁用并提示「从工单详情发起」。
  await page.goto('/mes/receipts', { waitUntil: 'domcontentloaded' })
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '完工入库' }),
  ).toBeVisible({ timeout: 15_000 })
  await expect(page.getByRole('button', { name: 'WO-001' }).first()).toBeVisible({
    timeout: 15_000,
  })
  await expect(page.getByRole('button', { name: '从工单详情发起' })).toBeDisabled()

  // 带工单上下文进来（模拟工单完工跳转）→ 登记弹窗自动打开（抓跨页带参+可登记）。
  await page.goto('/mes/receipts?workOrderId=WO-001&skuId=sku-1&quantity=5', {
    waitUntil: 'domcontentloaded',
  })
  await expect(page.getByRole('dialog')).toBeVisible({ timeout: 15_000 })
})

test('在制跟踪：在制进度渲染、查看工单就地速览不跳页', async ({ page }) => {
  // 在制数据来自共享 mock（WO-001：已产 5 / 计划 10）。
  await page.goto('/mes/wip', { waitUntil: 'domcontentloaded' })
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '在制跟踪' }),
  ).toBeVisible({ timeout: 15_000 })
  // 在制进度可读（已产 5）。
  await expect(page.getByText(/已产\s*5/)).toBeVisible({ timeout: 15_000 })
  // 点工单号（WO-001）→ 就地弹出「工单速览」，URL 不变（不跳页、不打断操作）。
  await page.getByRole('button', { name: 'WO-001' }).first().click()
  await expect(page.getByRole('dialog').filter({ hasText: '工单速览' })).toBeVisible({
    timeout: 15_000,
  })
  await expect(page).toHaveURL(/\/mes\/wip/)
})

async function expectHeading(page: Page, path: string, heading: string) {
  await page.goto(path, { waitUntil: 'domcontentloaded' })
  // 慢的 dev 环境 + 连续多页导航，放宽到 15s。
  await expect(
    page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: heading }),
  ).toBeVisible({ timeout: 15_000 })
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
          {
            productionPlanId: 'PLAN-READY',
            sourceSystem: 'sales',
            skuId: 'sku-1',
            plannedQuantity: 10,
            uomCode: 'EA',
            readinessStatus: 'Ready',
            plannedStartUtc: '2026-06-01T08:00:00.000Z',
            blockingReasons: [],
          },
          {
            productionPlanId: 'PLAN-BLOCKED',
            sourceSystem: 'forecast',
            skuId: 'sku-2',
            plannedQuantity: 5,
            readinessStatus: 'Blocked',
            blockingReasons: ['material_shortage'],
          },
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
            operationTaskId: 'WO-2026-08001-OP-10',
            operationTaskNo: 'WO-2026-08001-OP-10',
            workOrderId: 'WO-2026-08001',
            workOrderNo: 'WO-2026-08001',
            status: 'Completed',
            operationSequence: 10,
            workCenterId: 'WC-001',
            qualityStatus: 'Ready',
            actualLaborHours: 1.25,
            actualMachineHours: 0.5,
          },
          {
            operationTaskId: 'op-1',
            workOrderId: 'WO-001',
            status: 'Ready',
            operationSequence: 10,
            workCenterId: 'WC-001',
            qualityStatus: 'Ready',
          },
        ],
        total: 2,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/material-issue-requests') {
    return fulfillJson(
      route,
      envelope({
        items: [
          {
            requestId: 'MIR-001',
            workOrderId: 'WO-001',
            materialId: 'mat-1',
            requestedQuantity: 10,
            receivedQuantity: 4,
            status: 'PartiallyReceived',
            wmsRequestId: 'WMS-OUT-001',
            requestedAtUtc: '2026-06-01T08:00:00.000Z',
          },
        ],
        total: 1,
      }),
    )
  }

  if (pathname === '/api/business-console/v1/mes/line-side-inventory-balances') {
    const requestedPage = Number(url.searchParams.get('page') ?? 1)
    const items =
      requestedPage === 2
        ? [
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A04',
              skuCode: 'SKU-PAGE-201',
              uomCode: 'pcs',
              onHandQuantity: 8,
              reservedQuantity: 1,
              availableQuantity: 7,
              lotCount: 1,
              oldestProductionDate: '2026-08-25',
              ageDays: 1,
              ageCompleteness: 'complete',
            },
          ]
        : [
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A01',
              skuCode: 'SKU-DAMPER-001',
              uomCode: 'pcs',
              onHandQuantity: 120,
              reservedQuantity: 20,
              availableQuantity: 100,
              lotCount: 3,
              oldestProductionDate: '2026-08-20',
              ageDays: 6,
              ageCompleteness: 'complete',
            },
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A02',
              skuCode: 'SKU-SEAL-008',
              uomCode: 'pcs',
              onHandQuantity: 45,
              reservedQuantity: 5,
              availableQuantity: 40,
              lotCount: 2,
              oldestProductionDate: '2026-08-22',
              ageDays: 4,
              ageCompleteness: 'partial',
            },
            {
              siteCode: 'SITE-SH',
              locationCode: 'LINE-A03',
              skuCode: 'SKU-OIL-012',
              uomCode: 'l',
              onHandQuantity: 18,
              reservedQuantity: 0,
              availableQuantity: 18,
              lotCount: 1,
              oldestProductionDate: null,
              ageDays: null,
              ageCompleteness: 'unavailable',
            },
            ...Array.from({ length: 197 }, (_, index) => ({
              siteCode: 'SITE-SH',
              locationCode: `LINE-${String(index + 4).padStart(3, '0')}`,
              skuCode: `SKU-PAGE-${String(index + 4).padStart(3, '0')}`,
              uomCode: 'pcs',
              onHandQuantity: index + 1_000,
              reservedQuantity: 0,
              availableQuantity: index + 1_000,
              lotCount: 1,
              oldestProductionDate: '2026-08-25',
              ageDays: 1,
              ageCompleteness: 'complete',
            })),
          ]
    return fulfillJson(
      route,
      envelope({
        items,
        totalCount: 201,
        page: requestedPage,
        pageSize: 200,
        asOfDate: '2026-08-26',
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
            reportNo: 'PRPT-20260826-001',
            workOrderId: 'WO-001',
            workOrderNo: 'WO-001',
            operationTaskId: 'op-1',
            operationTaskNo: 'WO-001-OP-10',
            goodQuantity: 5,
            scrapQuantity: 0,
            reportedAtUtc: '2026-05-25T13:00:00.000Z',
            operationActualLaborHours: 2.75,
            operationActualMachineHours: 1.5,
          },
        ],
        total: 1,
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
