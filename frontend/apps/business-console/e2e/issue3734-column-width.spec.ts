import { expect, test, type Page, type Route } from '@playwright/test'

// #3734：NvDataTable 的列宽声明此前落在 <th> 上、表格却是默认 table-layout: auto，
// 于是声明被内容挤掉（完工入库页〈入库状态〉声明 w-48 实测 64px，徽标被截成一个字）。
//
// 这条断言必须在真实浏览器里跑：jsdom 不实现表格布局、也不加载 SFC 样式表，用
// getComputedStyle 在单测里量列宽只会得到假绿（组件单测钉的是「声明落到了表头上」，
// 量不到「声明宽 == 渲染宽」）。CI 浏览器不变量这条 lane 见 ci.yml 的
// "Test Business Console browser invariants"，本 spec 挂在同一 job 上。

const STORAGE_KEY = 'nerv-iip.business-console.auth'

const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  // 逐页对应两页的 meta.requiredPermissions（缺一个码整页跳 /forbidden）：
  // /mes/receipts 与 /wms/inbound 都取 business.mes.receipts.read /
  // business.wms.receipts.read。
  permissionCodes: ['business.mes.receipts.read', 'business.wms.receipts.read'],
}

const session = {
  accessToken: 'access-token',
  refreshToken: 'refresh-token',
  sessionId: 'session-1',
  expiresAtUtc: '2099-01-01T00:00:00.000Z',
  principal,
}

test.use({ viewport: { width: 1440, height: 900 } })

test.beforeEach(async ({ page }) => {
  await page.addInitScript(
    ({ key, storedSession }) => localStorage.setItem(key, JSON.stringify(storedSession)),
    { key: STORAGE_KEY, storedSession: session },
  )
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

test('NvDataTable：声明宽 == 渲染宽，Tailwind 类与 CSS 尺寸两种写法（#3734）', async ({ page }) => {
  await page.goto('/mes/receipts', { waitUntil: 'domcontentloaded' })
  await expect(page.locator('table.nv-dt-table')).toBeVisible({ timeout: 15_000 })

  // 完工入库页的列定义：数量/成本 w-28、状态 w-48、登记时间 w-44、操作 w-28，
  // 另有未声明宽度的列走均分。两种写法都在这张表上：w-* 走 class。
  await assertDeclaredWidthsHold(page, [
    { header: '入库数量', expectedPx: 112 },
    { header: '单位成本', expectedPx: 112 },
    { header: '入库状态', expectedPx: 192 },
    { header: '登记时间', expectedPx: 176 },
    { header: '操作', expectedPx: 112 },
  ])

  // 票面验收第 2 条：〈入库状态〉的徽标完整显示，不再被截成一个字。
  // 判据与票面实测一致：徽标内层 span 的可视宽 == 内容宽（scrollWidth ==
  // clientWidth）。票面记录的是 14 / 36（可视 14px、内容 36px）——被截断。
  //
  // 这里只钉「没被裁切」这一件本 PR 负责的事，不断言标签文案：页面把
  // RECEIPT_STATUS_LABELS 按 PascalCase 查表、而接口给的是小写枚举，真实值
  // 一律落到「未知状态」（与本票的列宽问题无关，另行登记）。用「未知状态」
  // 这四个字当样本，恰恰说明「四个字放不下而只剩一个字」确实被修好了。
  const badge = page
    .locator('table.nv-dt-table tbody tr')
    .first()
    .locator('td')
    .nth(await statusColumnIndex(page))
    .locator('span.truncate')
    .first()
  await expect(badge).toBeVisible()
  const badgeText = (await badge.textContent())?.trim() ?? ''
  expect(badgeText.length).toBeGreaterThan(1)
  const measured = await badge.evaluate((element) => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
  }))
  expect(
    measured.scrollWidth,
    `徽标被裁切：可视 ${measured.clientWidth}px、内容 ${measured.scrollWidth}px`,
  ).toBeLessThanOrEqual(measured.clientWidth)
})

test('NvDataTable：最小宽度类同样按声明生效（#3734 审核第 1 轮回归）', async ({ page }) => {
  // wms/inbound 的〈质检门禁〉声明 min-w-[22rem]。table-layout: fixed 只认 width，
  // 若组件不把 min-w-* 解析成 width，这列会被算成 0px（审核第 1 轮实测）。
  await page.goto('/wms/inbound', { waitUntil: 'domcontentloaded' })
  const head = page.locator('table.nv-dt-table thead th', { hasText: '质检门禁' })
  await expect(head).toBeVisible({ timeout: 15_000 })
  const width = await head.evaluate((element) => element.getBoundingClientRect().width)
  expect(width).toBeGreaterThan(0)
  expect(width).toBeCloseTo(352, -1)
})

/** 逐列断言：渲染宽度落在声明宽度的 ±1px 内（subpixel 舍入）。 */
async function assertDeclaredWidthsHold(
  page: Page,
  expected: { header: string; expectedPx: number }[],
) {
  for (const column of expected) {
    const head = page.locator('table.nv-dt-table thead th', { hasText: column.header })
    await expect(head, `列头未渲染：${column.header}`).toBeVisible()
    const width = await head.evaluate((element) => element.getBoundingClientRect().width)
    expect(
      Math.abs(width - column.expectedPx),
      `列「${column.header}」声明 ${column.expectedPx}px，实际渲染 ${width.toFixed(1)}px`,
    ).toBeLessThanOrEqual(1)
  }
}

async function statusColumnIndex(page: Page) {
  const heads = page.locator('table.nv-dt-table thead th')
  return heads.evaluateAll((elements) =>
    elements.findIndex((element) => element.textContent?.trim() === '入库状态'),
  )
}

async function routeConsoleApi(route: Route) {
  const url = new URL(route.request().url())
  if (url.pathname === '/api/console/v1/auth/refresh') {
    return route.fulfill({ json: { success: true, data: session } })
  }
  if (url.pathname === '/api/console/v1/auth/me') {
    return route.fulfill({ json: { success: true, data: principal } })
  }
  return route.fulfill({ json: { success: true, data: {} } })
}

async function routeBusinessConsoleApi(route: Route) {
  const { pathname } = new URL(route.request().url())

  if (pathname === '/api/business-console/v1/me/work-context') {
    return route.fulfill({ json: { success: true, data: { organizationId: 'org-001' } } })
  }

  // 完工入库：状态徽标要放得下完整文案，否则量的是被裁切后的宽度。
  if (pathname === '/api/business-console/v1/mes/finished-goods-receipt-requests') {
    return route.fulfill({
      json: {
        success: true,
        data: {
          items: [
            {
              receiptRequestId: 'receipt-1',
              workOrderNo: 'WO-001',
              requestNo: 'RCV-2026-0001',
              skuCode: 'SKU-001',
              quantity: 5,
              unitCost: 1280,
              // 线上枚举是小写（generated types 的联合类型即小写），页面按它查标签表。
              receiptStatus: 'completed',
              requestedAtUtc: '2026-09-22T10:31:00.000Z',
            },
          ],
        },
      },
    })
  }

  if (pathname === '/api/business-console/v1/wms/inbound-orders') {
    return route.fulfill({
      json: {
        success: true,
        data: {
          items: [
            {
              inboundOrderId: 'inbound-1',
              inboundOrderNo: 'PO-2026-0001',
              status: 'Pending',
              quality: '合格 5 / 拒收 0',
              createdAtUtc: '2026-09-22T10:31:00.000Z',
            },
          ],
        },
      },
    })
  }

  return route.fulfill({ json: { success: true, data: {} } })
}
