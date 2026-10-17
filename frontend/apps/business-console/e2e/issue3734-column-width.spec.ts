import { expect, test, type Page, type Route } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

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

  // 票面验收第 3 条要「覆盖 w-* class 与 CSS 尺寸两种写法」，这张表上两种都有：
  // w-* 走 class（数量/成本/状态/操作），登记时间走 CSS 尺寸 inline style。
  // 另有未声明宽度的列走均分，不参与断言。
  await assertDeclaredWidthsHold(page, [
    { header: '入库数量', expectedPx: 112, style: 'class' },
    { header: '单位成本', expectedPx: 112, style: 'class' },
    { header: '入库状态', expectedPx: 192, style: 'class' },
    { header: '登记时间', expectedPx: 176, style: 'dimension' },
    { header: '操作', expectedPx: 208, style: 'class' },
  ])

  // 票面验收第 2 条：〈入库状态〉的徽标完整显示，不再被截成一个字。
  // 判据与票面实测一致：徽标内层 span 的可视宽 == 内容宽（scrollWidth ==
  // clientWidth）。票面记录的是 14 / 36（可视 14px、内容 36px）——被截断。
  const badge = page
    .locator('table.nv-dt-table tbody tr')
    .first()
    .locator('td')
    .nth(await statusColumnIndex(page))
    .locator('span.truncate')
    .first()
  await expect(badge).toBeVisible()
  await expect(badge).toHaveText('已入库')
  const measured = await badge.evaluate((element) => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth,
  }))
  expect(
    measured.scrollWidth,
    `徽标被裁切：可视 ${measured.clientWidth}px、内容 ${measured.scrollWidth}px`,
  ).toBeLessThanOrEqual(measured.clientWidth)
})

test('NvDataTable：任意值宽度类同样按声明生效（#3734 审核第 1、2 轮回归）', async ({ page }) => {
  // wms/inbound 的〈质检门禁〉声明 w-[22rem]（任意值宽度类）。table-layout: fixed
  // 只认 width，审核第 1 轮这里写的是 min-w-[22rem]，实测被算成 0px。
  await page.goto('/wms/inbound', { waitUntil: 'domcontentloaded' })
  const head = page.locator('table.nv-dt-table thead th', { hasText: '质检门禁' })
  await expect(head).toBeVisible({ timeout: 15_000 })
  const measured = await head.evaluate((element) => ({
    width: element.getBoundingClientRect().width,
    hasWidthClass: Array.from(element.classList).some((name) => name === 'w-[22rem]'),
  }))
  expect(measured.hasWidthClass, '列「质检门禁」应走 w-[22rem] 任意值宽度类').toBe(true)
  expect(
    Math.abs(measured.width - 352),
    `列「质检门禁」声明 352px，实际渲染 ${measured.width.toFixed(1)}px`,
  ).toBeLessThanOrEqual(1)
})

/**
 * 逐列断言「声明宽 == 渲染宽」，容差 ±1px（subpixel 舍入）。
 *
 * 同时核对声明是以哪种写法落到表头上的：`w-*` 应当只走 class，CSS 尺寸应当只走
 * inline style。只量宽度不核对落地形式的话，组件把某列悄悄换成另一种写法也能过，
 * 那正是「两种写法都覆盖」要防的。
 */
async function assertDeclaredWidthsHold(
  page: Page,
  expected: { header: string; expectedPx: number; style: 'class' | 'dimension' }[],
) {
  for (const column of expected) {
    const head = page.locator('table.nv-dt-table thead th', { hasText: column.header })
    await expect(head, `列头未渲染：${column.header}`).toBeVisible()
    const measured = await head.evaluate((element) => ({
      width: element.getBoundingClientRect().width,
      inlineWidth: element.style.width,
      hasWidthClass: Array.from(element.classList).some((name) => name.startsWith('w-')),
    }))
    expect(
      Math.abs(measured.width - column.expectedPx),
      `列「${column.header}」声明 ${column.expectedPx}px，实际渲染 ${measured.width.toFixed(1)}px`,
    ).toBeLessThanOrEqual(1)
    if (column.style === 'class') {
      expect(measured.hasWidthClass, `列「${column.header}」应走 w-* class 写法`).toBe(true)
      expect(measured.inlineWidth, `列「${column.header}」不该同时挂 inline width`).toBe('')
    } else {
      expect(measured.inlineWidth, `列「${column.header}」应走 CSS 尺寸 inline style`).not.toBe('')
      expect(measured.hasWidthClass, `列「${column.header}」不该同时挂 w-* class`).toBe(false)
    }
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
              // 取 api-client 生成类型 receiptStatus 联合类型里的真值（页面按它查标签表）：
              // `Posted` 才是「已入库」（`completed` 属工单状态域，入库语境不查这张表）。
              // #3912：契约收敛到 FinishedGoodsReceiptRequest 的真实值域后运行时是 PascalCase；
              // 旧 fixture 写的小写 `posted` 之所以还能过，是因为词表有一层大小写归一化把它吸收了。
              receiptStatus: 'Posted',
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

test.describe('长诊断内容留在所属列（#4330）', () => {
  // Regression / #4330、#4286 Spec r1：实际渲染中长内容留在所属列，原文及业务动作可达。
  // API fixture 只证明浏览器布局；真实服务证据单独记录。
  const eventType = 'erp.OperationActualTimeLaborCostSettlementRequestedForCompletedWorkOrder'
  const consumerName = 'business-erp.operation-actual-time-labor-cost-settlement-consumer'
  const failureCode = 'missing-work-center-cost-rate'
  const failureMessage =
    '工作中心 WC-TUB-01（制管一线）在工序完工时间没有生效的人工成本费率，请维护费率后再处理。'
  const principal = {
    principalId: 'principal-1',
    principalType: 'User',
    loginName: 'admin',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: [
      'business.dlq.read',
      'business.dlq.manage',
      'business.mes.receipts.read',
      'business.mes.receipts.manage',
    ],
  }
  const session = {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    sessionId: 'session-1',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
    principal,
  }
  const deadLetter = {
    id: 'dl-1',
    eventType,
    consumerName,
    failureCode,
    failureMessage,
    status: 'pending',
    deadLetteredAtUtc: '2026-10-12T02:31:59Z',
    eventJson: JSON.stringify({ workOrderNo: 'WO-TUB-20261012-001' }),
  }

  test.beforeEach(async ({ page }) => {
    await page.addInitScript((stored) => {
      localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored))
    }, session)
    await page.route('**/api/console/v1/**', async (route) => {
      const path = new URL(route.request().url()).pathname
      await route.fulfill({
        json: { success: true, data: path.endsWith('/auth/me') ? principal : session },
      })
    })
    await page.route('**/api/business-console/v1/**', async (route) => {
      const path = new URL(route.request().url()).pathname
      let data: unknown = {}
      if (path.endsWith('/me/work-context')) data = { organizationId: 'org-001' }
      if (path.endsWith('/dead-letters/metrics'))
        data = {
          pendingCount: 1,
          failedCount: 0,
          replayedCount: 0,
          ignoredCount: 0,
          services: [{ service: 'Erp', metrics: { pendingCount: 1, failedCount: 0 } }],
        }
      if (path.endsWith('/dead-letters')) data = { items: [{ service: 'Erp', deadLetter }] }
      if (path.endsWith('/dead-letters/Erp/dl-1')) data = deadLetter
      if (path.endsWith('/mes/finished-goods-receipt-requests'))
        data = {
          totalCount: 2,
          items: [
            {
              receiptRequestId: 'receipt-1',
              requestNo: 'FGR-20261012-001',
              workOrderId: 'wo-1',
              workOrderNo: 'WO-TUB-20261012-001',
              skuCode: 'FG-TUB-001',
              quantity: 8,
              receiptStatus: 'Requested',
              costCapitalization: { workOrderCompleted: false },
              requestedAtUtc: '2026-10-12T02:31:59Z',
            },
            {
              receiptRequestId: 'receipt-2',
              requestNo: 'FGR-20261012-002',
              workOrderId: 'wo-2',
              workOrderNo: 'WO-TUB-20261012-002',
              skuCode: 'FG-TUB-001',
              quantity: 8,
              receiptStatus: 'InventoryPostingFailed',
              inventoryPostingFailureMessage:
                failureMessage + ' CostSettlementReference-WC-TUB-20261012-001',
              requestedAtUtc: '2026-10-12T02:31:59Z',
            },
          ],
        }
      await route.fulfill({ json: { success: true, data } })
    })
  })

  async function assertTextContained(page: Page) {
    const overflow = await page.locator('table tbody td').evaluateAll((cells) =>
      cells.flatMap((cell) => {
        const bounds = cell.getBoundingClientRect()
        const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
        const failures: string[] = []
        let node: Node | null
        while ((node = walker.nextNode())) {
          if (!node.textContent?.trim()) continue
          const range = document.createRange()
          range.selectNodeContents(node)
          for (const rect of range.getClientRects()) {
            if (rect.left < bounds.left - 1 || rect.right > bounds.right + 1)
              failures.push(node.textContent)
          }
        }
        return failures
      }),
    )
    expect(overflow, '文字跨出了所属单元格').toEqual([])
  }

  for (const width of [1024, 1280, 1440, 1366]) {
    for (const theme of ['light', 'dark']) {
      test(`死信长内容 ${width}×900 ${theme}`, async ({ page }, testInfo) => {
        await page.setViewportSize({ width, height: 900 })
        await page.addInitScript((mode) => localStorage.setItem('nerv-iip-color-mode', mode), theme)
        await page.goto('/ops/dead-letters', { waitUntil: 'domcontentloaded' })
        await expect(page.getByText(eventType, { exact: true })).toBeVisible()
        await expect(page.getByText(consumerName, { exact: true })).toBeVisible()
        await expect(
          page.getByRole('button', {
            name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
          }),
        ).toBeVisible()
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
          width,
        )
        const row = page.getByRole('row').filter({ hasText: eventType })
        await expect(row.getByText(failureCode, { exact: true })).toBeVisible()
        await expect(row.getByText('待处理', { exact: true })).toBeVisible()
        await assertTextContained(page)
        const detail = page.getByRole('button', { name: '详情', exact: true })
        await detail.scrollIntoViewIfNeeded()
        await expect(page.getByRole('button', { name: `重放死信：${eventType}` })).toBeEnabled()
        await page.screenshot({ path: testInfo.outputPath('dead-letters.png'), fullPage: true })
        await detail.click()
        const dialog = page.getByRole('dialog')
        await expect(dialog.getByText(eventType, { exact: true })).toBeVisible()
        await expect(dialog.getByText(consumerName, { exact: true })).toBeVisible()
        await expect(dialog.getByText(failureMessage, { exact: true })).toBeVisible()
        await expect(dialog.locator('pre')).toContainText('WO-TUB-20261012-001')
        await page.screenshot({
          path: testInfo.outputPath('dead-letter-detail.png'),
          fullPage: true,
        })
      })
      test(`完工长内容 ${width}×900 ${theme}`, async ({ page }, testInfo) => {
        await page.setViewportSize({ width, height: 900 })
        await page.addInitScript((mode) => localStorage.setItem('nerv-iip-color-mode', mode), theme)
        await page.goto('/mes/receipts', { waitUntil: 'domcontentloaded' })
        await expect(page.getByTestId('receipt-pending-reason')).toContainText(
          '等待 ERP 收到工单完工',
        )
        await expect(
          page.getByRole('button', {
            name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
          }),
        ).toBeVisible()
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
          width,
        )
        await expect(
          page.getByText(failureMessage + ' CostSettlementReference-WC-TUB-20261012-001', {
            exact: true,
          }),
        ).toBeVisible()
        await expect(page.getByText('入库失败', { exact: true })).toBeVisible()
        await assertTextContained(page)
        const retry = page.getByRole('button', { name: '重试', exact: true })
        await retry.scrollIntoViewIfNeeded()
        await expect(retry).toBeEnabled()
        await expect(page.getByText('待入库', { exact: true })).toBeVisible()
        await page.screenshot({ path: testInfo.outputPath('receipts.png'), fullPage: true })
      })
    }
  }
})

test.describe('MPS 物料与工厂可读性（#4471）', () => {
  // Regression / #4471、父 #4455 r1：路由 fixture 仅证明浏览器布局与交互，不证明真实栈。
  const skuCode = 'FG-PRECISION-HYDRAULIC-SHOCK-ABSORBER-20261017-001'
  const skuName = '高精度液压减振器总成（重载车辆加强型长行程）'
  const siteCode = 'SITE-NINGBO-PRECISION-MANUFACTURING-20261017-01'
  const siteName = '宁波精密制造第一工厂液压减振器总装生产基地'
  const principal = {
    principalId: 'planner-1',
    principalType: 'User',
    loginName: 'planner',
    organizationId: 'org-001',
    environmentId: 'env-dev',
    permissionVersion: 1,
    permissionCodes: [
      'business.planning.demands.read',
      'business.planning.mps.read',
      'business.planning.mrp.read',
      'business.planning.suggestions.manage',
    ],
  }
  const session = {
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    sessionId: 'session-1',
    expiresAtUtc: '2099-01-01T00:00:00.000Z',
    principal,
  }

  for (const viewport of [
    { width: 1024, height: 768 },
    { width: 1280, height: 800 },
    { width: 1440, height: 900 },
  ]) {
    for (const theme of ['light', 'dark']) {
      test(`MPS 长物料与工厂 ${viewport.width}×${viewport.height} ${theme}`, async ({
        page,
      }, testInfo) => {
        await page.setViewportSize(viewport)
        await page.addInitScript(
          ({ session, theme }) => {
            localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(session))
            localStorage.setItem('nerv-iip-color-mode', theme)
          },
          { session, theme },
        )
        let status = 'Draft'
        const actions: string[] = []
        await page.route('**/api/console/v1/**', async (route) => {
          const path = new URL(route.request().url()).pathname
          await route.fulfill({
            json: { success: true, data: path.endsWith('/refresh') ? session : principal },
          })
        })
        await page.route('**/api/business-console/v1/**', async (route) => {
          const url = new URL(route.request().url())
          let data: unknown = { items: [], total: 0 }
          if (url.pathname.endsWith('/me/work-context'))
            data = { organizationId: 'org-001', environmentId: 'env-dev' }
          else if (url.pathname.endsWith('/master-data/skus'))
            data = { resources: [{ code: skuCode, displayName: skuName }], total: 1 }
          else if (url.pathname.endsWith('/master-data/resources')) {
            const type = url.searchParams.get('resourceType')
            data = {
              resources:
                type === 'site'
                  ? [{ code: siteCode, displayName: siteName }]
                  : type === 'unit-of-measure'
                    ? [{ code: 'pcs', displayName: '件' }]
                    : [],
              total: 1,
            }
          } else if (url.pathname.endsWith('/planning/mps')) {
            data = {
              items: Array.from({ length: 11 }, (_, index) => ({
                mpsId: `mps-${index + 1}`,
                skuCode,
                siteCode,
                uomCode: 'pcs',
                bucketDate: index === 10 ? '2026-10-28' : '2026-10-17',
                quantity: 123456.75,
                status: index === 0 ? status : 'Released',
              })),
              total: 11,
            }
          } else if (url.pathname.endsWith('/mps-1/review')) {
            status = 'Reviewed'
            actions.push('review')
          } else if (url.pathname.endsWith('/mps-1/release')) {
            status = 'Released'
            actions.push('release')
          }
          await route.fulfill({ json: { success: true, data } })
        })
        await page.goto('/planning', { waitUntil: 'domcontentloaded' })
        await page.getByRole('tab', { name: /MPS 主计划/ }).click()
        const table = page.locator('table.nv-dt-table')
        const row = table.locator('tbody tr').first()
        await expect(row).toContainText(skuName)
        await expect(row).toContainText(siteName)
        await expect(row).toContainText('123456.75 件')
        await expect(row).toContainText('2026-10-17')
        await expect(row).toContainText('草稿')
        await expect(
          page.getByRole('button', {
            name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
          }),
        ).toBeVisible()
        await page.screenshot({ path: testInfo.outputPath('mps-left.png'), fullPage: true })
        const measurement = await table.evaluate((table) => {
          const failures: string[] = []
          const widths = Array.from(table.querySelectorAll('thead th')).map((th) => ({
            header: th.textContent?.trim(),
            width: th.getBoundingClientRect().width,
          }))
          for (const cell of table.querySelectorAll('tbody td')) {
            const bounds = cell.getBoundingClientRect()
            const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
            while (walker.nextNode()) {
              const node = walker.currentNode
              if (!node.textContent?.trim()) continue
              const range = document.createRange()
              range.selectNodeContents(node)
              for (const rect of range.getClientRects()) {
                if (rect.left < bounds.left - 1 || rect.right > bounds.right + 1)
                  failures.push(node.textContent)
              }
            }
          }
          return { widths, failures, documentWidth: document.documentElement.scrollWidth }
        })
        const measurementPath = testInfo.outputPath('mps-dom-measurement.json')
        await writeFile(
          measurementPath,
          JSON.stringify({ viewport, theme, ...measurement }, null, 2),
        )
        await testInfo.attach('mps-dom-measurement', {
          path: measurementPath,
          contentType: 'application/json',
        })
        expect(measurement.failures, '文字不得跨出所属列').toEqual([])
        expect(measurement.documentWidth).toBeLessThanOrEqual(viewport.width)
        await expect(row).toContainText(skuCode)
        await expect(row).toContainText(siteCode)
        const edit = row.getByRole('button', { name: `编辑主计划行 ${skuCode}` })
        await edit.scrollIntoViewIfNeeded()
        const scroll = await edit.evaluate((button) => {
          const container = button.closest('[data-slot="table-container"]')!
          const bounds = container.getBoundingClientRect()
          const action = button.getBoundingClientRect()
          return {
            clientWidth: container.clientWidth,
            scrollWidth: container.scrollWidth,
            scrollLeft: container.scrollLeft,
            actionContained: action.left >= bounds.left && action.right <= bounds.right,
          }
        })
        await writeFile(testInfo.outputPath('mps-scroll.json'), JSON.stringify(scroll, null, 2))
        if (scroll.scrollWidth > scroll.clientWidth) expect(scroll.scrollLeft).toBeGreaterThan(0)
        expect(scroll.actionContained, '滚动后编辑操作必须在表格可视区域内').toBe(true)
        await page.screenshot({ path: testInfo.outputPath('mps-actions.png'), fullPage: true })
        await edit.click()
        const dialog = page.getByRole('dialog')
        await expect(dialog.getByRole('heading', { name: '编辑主计划行' })).toBeVisible()
        await expect(dialog.locator('#mps-qty')).toHaveValue('123456.75')
        await dialog.getByRole('button', { name: '取消', exact: true }).click()
        await row.getByRole('button', { name: '评审', exact: true }).click()
        await expect(row).toContainText('已评审')
        await row.getByRole('button', { name: '发布', exact: true }).click()
        await expect(row).toContainText('已发布')
        expect(actions).toEqual(['review', 'release'])
        await expect(row.getByRole('button', { name: `编辑主计划行 ${skuCode}` })).toHaveCount(0)
        await page.getByRole('button', { name: '下一页', exact: true }).click()
        await expect(table.locator('tbody tr')).toHaveCount(1)
        await expect(table).toContainText('2026-10-28')
        await page.getByRole('button', { name: '上一页', exact: true }).click()
        await expect(table.locator('tbody tr')).toHaveCount(10)
      })
    }
  }
})

test.describe('停机列表长内容（#4470）', () => {
  // #4455 批准规格 r1：fixture 仅证明浏览器布局和既有交互，真实栈由父票另验。
  const deviceName = '制管一线高精度数控双端自动切割及端面加工设备'
  const deviceCode = 'EQ-TUB-CUTTING-DOUBLE-END-AUTOMATIC-20261017-001'
  const reasonName = '机械故障：主轴轴承温升异常，需要检查传动机构、润滑系统和密封组件后确认恢复'
  const rows = Array.from({ length: 12 }, (_, index) => ({
    downtimeEventId: `DT-TUB-20261017-${String(index + 1).padStart(3, '0')}`,
    workCenterId: 'WC-TUB-DOUBLE-END-CUTTING-01',
    workOrderId: `wo-${index + 1}`,
    workOrderNo: `WO-TUB-20261017-${String(index + 1).padStart(3, '0')}`,
    deviceAssetId: 'device-1',
    deviceAssetCode: deviceCode,
    deviceAssetName: deviceName,
    reasonCode: 'DT-MECH',
    reasonName,
    status: index % 2 === 0 ? 'Open' : 'Recovered',
    startedAtUtc: '2026-10-17T01:23:45Z',
    recoveredAtUtc: index % 2 === 0 ? null : '2026-10-17T02:34:56Z',
  }))
  const principal = {
    ...session.principal,
    loginName: 'mes-team-leader',
    permissionCodes: ['business.mes.downtime.read', 'business.mes.downtime.manage'],
  }

  for (const viewport of [
    { width: 1024, height: 768 },
    { width: 1280, height: 800 },
    { width: 1440, height: 900 },
  ]) {
    for (const theme of ['light', 'dark']) {
      test(`停机可读性与恢复 ${viewport.width}×${viewport.height} ${theme}`, async ({
        page,
      }, testInfo) => {
        await page.setViewportSize(viewport)
        await page.addInitScript(
          ({ principal, session, theme }) => {
            localStorage.setItem(
              'nerv-iip.business-console.auth',
              JSON.stringify({ ...session, principal }),
            )
            localStorage.setItem('nerv-iip-color-mode', theme)
          },
          { principal, session, theme },
        )
        let recoveredId = ''
        const recoveries: { id: string; body: Record<string, unknown> }[] = []
        const queries: URLSearchParams[] = []
        await page.route('**/api/console/v1/**', async (route) => {
          const path = new URL(route.request().url()).pathname
          await route.fulfill({
            json: {
              success: true,
              data: path.endsWith('/auth/me') ? principal : { ...session, principal },
            },
          })
        })
        await page.route('**/api/business-console/v1/**', async (route) => {
          const url = new URL(route.request().url())
          let data: unknown = {}
          if (url.pathname.endsWith('/me/work-context')) data = { organizationId: 'org-001' }
          if (url.pathname.endsWith('/directories/downtime-reason'))
            data = { items: [{ code: 'DT-MECH', displayName: reasonName }] }
          if (url.pathname.endsWith('/mes/downtime-events')) {
            queries.push(url.searchParams)
            const keyword = url.searchParams.get('keyword') ?? ''
            const status = url.searchParams.get('status')
            const matching = rows
              .map((row) =>
                row.downtimeEventId === recoveredId
                  ? { ...row, status: 'Recovered', recoveredAtUtc: '2026-10-17T03:00:00Z' }
                  : row,
              )
              .filter(
                (row) =>
                  (!status || row.status.toLowerCase() === status.toLowerCase()) &&
                  row.workOrderNo.includes(keyword),
              )
            const skip = Number(url.searchParams.get('skip') ?? 0)
            const take = Number(url.searchParams.get('take') ?? 10)
            data = {
              items: matching.slice(skip, skip + take),
              total: matching.length,
              reasonSummary: [],
            }
          }
          if (url.pathname.endsWith('/recover')) {
            recoveredId = decodeURIComponent(url.pathname.split('/').at(-2)!)
            recoveries.push({ id: recoveredId, body: route.request().postDataJSON() })
          }
          await route.fulfill({ json: { success: true, data } })
        })
        await page.goto('/mes/downtime', { waitUntil: 'domcontentloaded' })
        const table = page.locator('table.nv-dt-table')
        await expect(table.locator('tbody tr')).toHaveCount(10)
        await expect(
          page.getByRole('button', {
            name: theme === 'dark' ? '切换到亮色主题' : '切换到暗色主题',
          }),
        ).toBeVisible()
        await expect(table).toContainText(deviceName)
        await expect(table).toContainText(deviceCode)
        await expect(table).toContainText(reasonName)
        await expect(table).toContainText('未恢复')
        await expect(table).toContainText('已恢复')
        const measure = await table.evaluate((element) => ({
          headers: Array.from(element.querySelectorAll('th')).map((cell) => ({
            text: cell.textContent?.trim(),
            width: cell.getBoundingClientRect().width,
          })),
          cells: Array.from(element.querySelectorAll('tbody tr:first-child td')).map((cell) => ({
            text: cell.textContent?.trim(),
            width: cell.getBoundingClientRect().width,
            height: cell.getBoundingClientRect().height,
          })),
          overflow: Array.from(element.querySelectorAll('tbody td')).flatMap((cell) => {
            const bounds = cell.getBoundingClientRect()
            const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
            const failures: string[] = []
            let node: Node | null
            while ((node = walker.nextNode())) {
              if (!node.textContent?.trim()) continue
              const range = document.createRange()
              range.selectNodeContents(node)
              if (
                Array.from(range.getClientRects()).some(
                  (rect) => rect.left < bounds.left - 1 || rect.right > bounds.right + 1,
                )
              )
                failures.push(node.textContent)
            }
            return failures
          }),
        }))
        const measurementPath = testInfo.outputPath('dom-measurements.json')
        await writeFile(measurementPath, JSON.stringify({ viewport, theme, ...measure }, null, 2))
        await testInfo.attach('dom-measurements', {
          path: measurementPath,
          contentType: 'application/json',
        })
        await page.screenshot({ path: testInfo.outputPath('downtime.png'), fullPage: true })
        expect.soft(measure.overflow, '文字必须留在所属单元格且完整显示').toEqual([])
        expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(
          viewport.width,
        )
        // 显式移动表格自己的滚动容器，再操作右侧恢复入口。
        const scroll = await table.evaluate((element) => {
          const parent = element.parentElement!
          parent.scrollLeft = parent.scrollWidth
          return { left: parent.scrollLeft, width: parent.clientWidth, content: parent.scrollWidth }
        })
        expect(scroll.content).toBeGreaterThan(scroll.width)
        expect(scroll.left).toBeGreaterThan(0)
        await writeFile(
          testInfo.outputPath('scroll-measurements.json'),
          JSON.stringify(scroll, null, 2),
        )
        const firstRow = table.locator('tbody tr').first()
        const recover = firstRow.getByRole('button', { name: '恢复', exact: true })
        await expect(recover).toBeInViewport()
        await page.screenshot({
          path: testInfo.outputPath('downtime-scrolled.png'),
          fullPage: true,
        })
        await recover.click()
        const dialog = page.getByRole('dialog')
        await expect(dialog).toContainText(rows[0]!.downtimeEventId)
        await expect(dialog).toContainText(deviceName)
        await dialog.getByRole('button', { name: '确认恢复', exact: true }).click()
        await expect(dialog).not.toBeVisible()
        expect(recoveries).toHaveLength(1)
        expect(recoveries[0]).toMatchObject({
          id: rows[0]!.downtimeEventId,
          body: { organizationId: 'org-001', environmentId: 'env-dev' },
        })
        expect(Number.isFinite(Date.parse(String(recoveries[0]!.body.recoveredAtUtc)))).toBe(true)
        await expect(firstRow).toContainText('已恢复')
        await expect(firstRow.getByRole('button', { name: '恢复', exact: true })).toHaveCount(0)
        await page.getByRole('button', { name: '下一页', exact: true }).click()
        await expect(table.locator('tbody tr')).toHaveCount(2)
        await expect(table).toContainText(rows[10]!.downtimeEventId)
        await page.getByRole('textbox', { name: '搜索停机事件' }).fill(rows[0]!.workOrderNo)
        await expect(table.locator('tbody tr')).toHaveCount(1)
        await expect(table).toContainText(rows[0]!.downtimeEventId)
        expect(queries.at(-1)!.get('skip')).toBe('0')
        expect(queries.at(-1)!.get('keyword')).toBe(rows[0]!.workOrderNo)
        await page.getByRole('textbox', { name: '搜索停机事件' }).fill('')
        await expect(table.locator('tbody tr')).toHaveCount(10)
        await page.getByRole('combobox', { name: '停机状态', exact: true }).click()
        await page.getByRole('option', { name: '未恢复', exact: true }).click()
        await expect(table.locator('tbody tr')).toHaveCount(5)
        expect(queries.at(-1)!.get('status')).toBe('Open')
        await expect(table.locator('tbody')).not.toContainText('已恢复')
        await page.getByRole('combobox', { name: '按停机原因筛选', exact: true }).click()
        await page.getByRole('option', { name: reasonName, exact: true }).click()
        await expect.poll(() => queries.at(-1)!.get('reasonCode')).toBe('DT-MECH')
        await expect(table.locator('tbody tr')).toHaveCount(5)
      })
    }
  }
})
