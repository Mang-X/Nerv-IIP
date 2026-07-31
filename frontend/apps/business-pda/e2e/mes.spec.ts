import { expect, test } from '@playwright/test'
import {
  productionReportReceipt,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
} from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

// #1297：PDA 侧作业范围闭环。后端不带 scopeKind/scopeId 只回授权清单、selectedScope 恒空，
// 所以「操作工登录后能不能拿到工序/工单数据」完全取决于前端是否走完
// 「清单 → 选择 → 带参重核验」。这条断言的是范围参数真的进了业务查询，且换范围会重取。
test('作业范围：授权清单渲染成移动端选择器，切换后按新范围重取工序任务', async ({ page }) => {
  const requestedScopeIds: string[] = []
  await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
    requestedScopeIds.push(new URL(route.request().url()).searchParams.get('scopeId') ?? '')
    return routeBusinessConsoleApi(route)
  })

  await page.goto('/mes/operation')
  await expect(page.getByRole('heading', { name: '工序执行' })).toBeVisible()

  // 自动兜底选中清单第一项，并且工序任务查询只在带上该范围之后才发出。
  const scopeTrigger = page.getByTestId('mes-work-scope-select').locator('button').first()
  await expect(scopeTrigger).toHaveText('精加工一线（工作中心）')
  await expect.poll(() => requestedScopeIds).toEqual(['WC-A'])

  // 单手可达的下拉面板里换范围 → 重新核验 → 按新范围重取。
  await scopeTrigger.click()
  await page.getByRole('button', { name: '精加工二线（工作中心）', exact: true }).click()
  await expect(scopeTrigger).toHaveText('精加工二线（工作中心）')
  await expect.poll(() => requestedScopeIds).toEqual(['WC-A', 'WC-B'])
})

test('工序执行：列表 → 完成（二次确认）→ 成功结果', async ({ page }) => {
  await page.goto('/mes/operation')

  await expect(page.getByRole('heading', { name: '工序执行' })).toBeVisible()

  // Running 工序任务行（OP-1）渲染：title=工单·工序，subtitle=状态·工作中心。
  const row = page.getByText('WO-1 · 工序 10')
  await expect(row).toBeVisible()

  // 点行打开 BottomSheet 动作面板（teleport 到 body）。
  await row.click()
  // Running → 可用动作含「完成」（终态、destructive）。
  const completeBtn = page.getByTestId('action-complete')
  await expect(completeBtn).toBeVisible()

  // 第一次点「完成」进入二次确认，并未直接调用。
  await completeBtn.click()
  const confirmBtn = page.getByTestId('confirm-complete')
  await expect(confirmBtn).toBeVisible()

  // 确认完成 → 成功 Result。
  await confirmBtn.click()
  const result = page.locator('[data-result][data-status="success"]')
  await expect(result).toBeVisible()
  await expect(result.getByText('工序已完成')).toBeVisible()
})

test('报工：选工单 → 选工序 → 录良品数 → 提交 → 成功结果', async ({ page }) => {
  let submittedReport: Record<string, unknown> | undefined
  await page.route('**/api/business-console/v1/mes/production-reports', async (route) => {
    if (route.request().method() !== 'POST') {
      return routeBusinessConsoleApi(route)
    }
    submittedReport = route.request().postDataJSON() as Record<string, unknown>
    const productionReportId = '019f-e2e-production-report'
    return route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        data: {
          productionReportId,
          reportNo: 'RPT-E2E-0001',
          // 写操作必须带权威回执，否则前端只会判「结果尚未核实」（#1219）。
          operationReceipt: productionReportReceipt(
            productionReportId,
            String(submittedReport.idempotencyKey ?? ''),
          ),
        },
      }),
    })
  })
  await page.goto('/mes/report')

  await expect(page.getByRole('heading', { name: '报工' })).toBeVisible()

  // 步骤 1：选工单（WO-1）。
  const workOrderRow = page.getByText('WO-1', { exact: true })
  await expect(workOrderRow).toBeVisible()
  await workOrderRow.click()

  // 步骤 2：选工序（按工单过滤后仍返回 mock 工序）。
  await expect(page.getByText('当前工单')).toBeVisible()
  const taskRow = page.getByText('WO-1 · 工序 10')
  await expect(taskRow).toBeVisible()
  await taskRow.click()

  // 步骤 3：录数量（BottomSheet）→ 良品数。
  const goodQty = page.getByTestId('good-quantity')
  await expect(goodQty).toBeVisible()
  await goodQty.fill('5')

  // 提交报工 → 成功 Result。
  await page.getByTestId('submit-report').click()
  const result = page.locator('[data-result][data-status="success"]')
  await expect(result).toBeVisible()
  await expect(result.getByText('报工成功')).toBeVisible()
  await expect(result).toContainText('RPT-E2E-0001')
  await expect(result).toContainText('019f-e2e-production-report')
  expect(submittedReport).toMatchObject({
    workOrderId: 'WO-1',
    operationTaskId: 'OP-1',
    goodQuantity: 5,
  })
})

test('报工：router pair 切换、延迟旧请求与浏览器 back/forward 始终重绑同一实体', async ({
  page,
}) => {
  let operationTaskDiscoveryCalls = 0
  let resolveFirstDetailStarted!: () => void
  let resolveFirstDetailRelease!: () => void
  const firstDetailStarted = new Promise<void>((resolve) => {
    resolveFirstDetailStarted = resolve
  })
  const firstDetailRelease = new Promise<void>((resolve) => {
    resolveFirstDetailRelease = resolve
  })
  let workOrderOneDetailCalls = 0
  await page.route('**/api/business-console/v1/mes/operation-tasks**', async (route) => {
    operationTaskDiscoveryCalls += 1
    return routeBusinessConsoleApi(route)
  })
  await page.route('**/api/business-console/v1/mes/work-orders/WO-1**', async (route) => {
    workOrderOneDetailCalls += 1
    if (workOrderOneDetailCalls === 1) {
      resolveFirstDetailStarted()
      await firstDetailRelease
    }
    return routeBusinessConsoleApi(route)
  })

  try {
    await page.goto('/mes/report')
    await expect(page.getByRole('heading', { name: '报工' })).toBeVisible()
    await page.evaluate(async (target) => {
      const { router } = await import(/* @vite-ignore */ '/src/router/index.ts')
      await router.push(target)
    }, '/mes/report?workOrderId=WO-1&operationTaskId=OP-1')
    await firstDetailStarted
    await page.evaluate(async (target) => {
      const { router } = await import(/* @vite-ignore */ '/src/router/index.ts')
      await router.push(target)
    }, '/mes/report?workOrderId=WO-2&operationTaskId=OP-3')

    await expect(page.getByText('当前工单')).toBeVisible()
    await expect(page.getByRole('heading', { name: 'WO-2 · 工序 10', exact: true })).toBeVisible()
    await expect(page.getByTestId('good-quantity')).toBeVisible()
    await expect(page.getByTestId('report-route-issue')).toHaveCount(0)
    resolveFirstDetailRelease()
    await expect(page.getByText('WO-1 · 工序 10')).toHaveCount(0)
    expect(operationTaskDiscoveryCalls).toBe(0)

    await page.goBack()
    await expect(page.getByRole('heading', { name: 'WO-1 · 工序 10', exact: true })).toBeVisible()
    await expect(page.getByTestId('good-quantity')).toBeVisible()

    await page.goForward()
    await expect(page.getByRole('heading', { name: 'WO-2 · 工序 10', exact: true })).toBeVisible()
    await expect(page.getByTestId('good-quantity')).toBeVisible()
  } finally {
    // Never leave the intercepted route pending when an assertion or navigation fails.
    resolveFirstDetailRelease()
  }
})

test('报工：详情前 500 项不含目标时，完整 pair URL 分页解析第 501 项', async ({ page }) => {
  await page.goto('/mes/report?workOrderId=WO-501&operationTaskId=OP-501')

  await expect(page.getByText('当前工单')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'WO-501 · 工序 501', exact: true })).toBeVisible()
  await expect(page.getByTestId('good-quantity')).toBeVisible()
  await expect(page.getByTestId('report-route-issue')).toHaveCount(0)
})

test('领料：列表渲染领料申请行（不退化为空态）', async ({ page }) => {
  await page.goto('/mes/issue')

  await expect(page.getByRole('heading', { name: '领料' })).toBeVisible()

  // 领料申请行：title = 工单 · 物料，不外显原始 requestId。
  await expect(page.getByText('WO-1 · 物料 MAT-1')).toBeVisible()
  await expect(page.getByText('WO-1 · 物料 MAT-2')).toBeVisible()
  await expect(page.getByText('暂无领料申请')).toHaveCount(0)
})

test('完工入库：列表渲染入库申请行（不退化为空态）', async ({ page }) => {
  await page.goto('/mes/receipt')

  await expect(page.getByRole('heading', { name: '完工入库' })).toBeVisible()

  // 入库申请行：title = 工单 · 物料(SKU)，不外显原始 receiptRequestId。
  await expect(page.getByText('WO-1 · 物料 SKU-1')).toBeVisible()
  await expect(page.getByText('WO-1 · 物料 SKU-2')).toBeVisible()
  await expect(page.getByText('暂无完工入库申请')).toHaveCount(0)
})

test('首页 → 工序执行：点击应用墙入口跳转到 /mes/operation', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByTestId('home-name')).toBeVisible()
  await page.getByRole('button', { name: '工序执行' }).click()

  await expect(page).toHaveURL('/mes/operation')
  await expect(page.getByRole('heading', { name: '工序执行' })).toBeVisible()
})
