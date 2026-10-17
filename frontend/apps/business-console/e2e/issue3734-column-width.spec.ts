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

test.describe('工序执行关键列可读性（#4468）', () => {
  const workOrderNo = 'WO-ROD-20261017-000128-精密活塞杆'
  const task = {
    operationTaskId: 'OPT-ROD-20261017-000128-OP-020',
    operationTaskNo: 'OPT-ROD-20261017-000128-OP-020',
    workOrderId: workOrderNo,
    workOrderNo,
    operationSequence: 20,
    workCenterId: 'WC-ROD-01',
    workCenterName: '活塞杆精密加工一线工作中心',
    deviceAssetId: 'EQ-ROD-CNC-20261017-000128',
    deviceAssetCode: 'EQ-ROD-CNC-20261017-000128',
    deviceAssetName: '数控外圆磨床（精密活塞杆加工单元）',
    shiftId: 'SHIFT-DAY',
    status: 'InProgress',
    qualityStatus: 'QUALITY_PLAN_MISSING',
    plannedStartUtc: '2026-10-17T01:30:00Z',
    actualLaborHours: 12.5,
    actualMachineHours: 10.25,
  }

  for (const viewport of [
    { width: 1280, height: 800 },
    { width: 1440, height: 900 },
    { width: 1024, height: 768 },
  ]) {
    for (const theme of ['light', 'dark'] as const) {
      test(`${viewport.width}×${viewport.height} ${theme}：长标识不跨列、滚动后行操作可用`, async ({
        page,
      }, testInfo) => {
        const operator = {
          ...principal,
          permissionCodes: [
            'business.mes.operations.read',
            'business.mes.work-orders.read',
            'business.mes.reporting.write',
            'business.master-data.read',
          ],
        }
        await page.setViewportSize(viewport)
        await page.emulateMedia({ colorScheme: theme })
        await page.addInitScript(
          ({ stored, theme }) => {
            localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(stored))
            localStorage.setItem('nerv-iip-color-mode', theme)
          },
          { stored: { ...session, principal: operator }, theme },
        )
        await page.route('**/api/console/v1/**', (route) =>
          route.fulfill({
            json: {
              success: true,
              data: new URL(route.request().url()).pathname.endsWith('/auth/me')
                ? operator
                : { ...session, principal: operator },
            },
          }),
        )
        await page.route('**/api/business-console/v1/**', async (route) => {
          const url = new URL(route.request().url())
          const path = url.pathname
          let data: unknown = { items: [], total: 0 }
          if (path.endsWith('/me/work-context')) {
            const scope = { kind: 'workCenter', id: 'WC-ROD-01', displayName: '活塞杆精密加工一线' }
            data = {
              organizationId: 'org-001',
              environmentId: 'env-dev',
              applicablePermissionCode: url.searchParams.get('permissionCode'),
              resolutionStatus: 'resolved',
              authorizedScopes: [scope],
              availableScopeKinds: ['workCenter'],
              selectedScope: scope,
              issues: [],
            }
          }
          if (path.endsWith('/mes/operation-tasks')) data = { items: [task], total: 1 }
          if (path.endsWith(`/mes/work-orders/${encodeURIComponent(workOrderNo)}`))
            data = {
              workOrderId: workOrderNo,
              workOrderNo,
              status: 'InProgress',
              operationTasks: [task],
            }
          if (
            path.endsWith('/master-data/resources') &&
            url.searchParams.get('resourceType') === 'shift'
          )
            data = { items: [{ code: 'SHIFT-DAY', displayName: '白班（08:00—16:00）' }], total: 1 }
          await route.fulfill({ json: { success: true, data } })
        })
        await page.goto('/mes/operation-tasks')
        // 使用实际主题类，避免系统主题或本机偏好令两组截图落在同一主题。
        await page
          .locator('html')
          .evaluate((el, dark) => el.classList.toggle('dark', dark), theme === 'dark')
        const table = page.locator('table.nv-dt-table').first()
        const row = table.locator('tbody tr').first()
        await expect(row).toContainText(task.operationTaskNo)
        await expect(row).toContainText(task.deviceAssetName)
        await page.screenshot({ path: testInfo.outputPath('operations-left.png'), fullPage: true })
        const measurement = await table.evaluate((table) => {
          const headers = Array.from(table.querySelectorAll('thead th')).map((th) => ({
            header: th.textContent?.trim(),
            width: th.getBoundingClientRect().width,
          }))
          const escaped: string[] = []
          for (const cell of table.querySelectorAll('tbody tr:first-child td')) {
            const bounds = cell.getBoundingClientRect()
            const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT)
            while (walker.nextNode()) {
              const node = walker.currentNode
              if (!node.textContent?.trim() || node.parentElement?.closest('.sr-only')) continue
              const range = document.createRange()
              range.selectNodeContents(node)
              for (const rect of range.getClientRects()) {
                if (rect.width && (rect.left < bounds.left - 1 || rect.right > bounds.right + 1))
                  escaped.push(node.textContent.trim())
              }
            }
          }
          const container = table.closest('[data-slot="table-container"]') as HTMLElement
          return {
            headers,
            escaped,
            containerWidth: container.clientWidth,
            scrollWidth: container.scrollWidth,
            documentWidth: document.documentElement.scrollWidth,
            viewportWidth: window.innerWidth,
          }
        })
        await testInfo.attach('DOM测量（浏览器fixture）', {
          body: JSON.stringify(measurement, null, 2),
          contentType: 'application/json',
        })
        console.log(JSON.stringify({ viewport, theme, ...measurement }))
        expect(measurement.escaped, '文本应留在所属单元格内').toEqual([])
        expect(measurement.documentWidth).toBeLessThanOrEqual(measurement.viewportWidth)
        await row.getByRole('button', { name: workOrderNo, exact: true }).click()
        await expect(
          page.getByRole('dialog', { name: '工单速览' }).getByText('工序 20', { exact: true }),
        ).toBeVisible()
        await page.keyboard.press('Escape')
        await expect(page.getByRole('dialog', { name: '工单速览' })).toBeHidden()
        const scroll = table.locator('..')
        await scroll.evaluate((el) => {
          el.scrollLeft = el.scrollWidth
        })
        expect(await scroll.evaluate((el) => el.scrollLeft)).toBeGreaterThan(0)
        await page.screenshot({ path: testInfo.outputPath('operations-right.png'), fullPage: true })
        await row.getByRole('button', { name: '工序任务操作 工序 20' }).click()
        await expect(page.getByRole('menuitem', { name: '查看工单' })).toBeVisible()
        await page.getByRole('menuitem', { name: '查看工单' }).click()
        await expect(page.getByRole('dialog', { name: '工单速览' })).toBeVisible()
        await page.keyboard.press('Escape')
        await expect(page.getByRole('dialog', { name: '工单速览' })).toBeHidden()

        // 同一页面继续走真实分页/搜索事件；fixture 按请求的页窗返回数据。
        await page.route('**/mes/operation-tasks?**', async (route) => {
          const query = new URL(route.request().url()).searchParams
          const skip = Number(query.get('skip') ?? 0)
          const take = Number(query.get('take') ?? 10)
          const all = Array.from({ length: 11 }, (_, index) => ({
            ...task,
            operationTaskId: `${task.operationTaskId}-${index}`,
            operationTaskNo: `${task.operationTaskNo}-${index}`,
          }))
          const filtered = query.get('keyword')
            ? all.filter((item) => item.operationTaskNo.endsWith('-10'))
            : all
          await route.fulfill({
            json: {
              success: true,
              data: { items: filtered.slice(skip, skip + take), total: filtered.length },
            },
          })
        })
        await page.getByRole('button', { name: '刷新', exact: true }).click()
        await expect(table.locator('tbody tr')).toHaveCount(10)
        await page.getByRole('button', { name: '下一页', exact: true }).click()
        await expect(table.locator('tbody tr')).toHaveCount(1)
        await expect(row).toContainText(`${task.operationTaskNo}-10`)
        const searchRequest = page.waitForRequest((request) => {
          const url = new URL(request.url())
          return (
            url.pathname.endsWith('/mes/operation-tasks') &&
            url.searchParams.get('keyword') === '-10' &&
            url.searchParams.get('skip') === '0'
          )
        })
        await page.getByPlaceholder('搜索任务、工单、设备').fill('-10')
        await searchRequest
        await expect(row).toContainText(`${task.operationTaskNo}-10`)
        await expect(page.getByRole('button', { name: '下一页', exact: true })).toHaveCount(0)
      })
    }
  }
})
