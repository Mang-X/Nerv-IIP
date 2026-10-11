import { expect, test, type Route } from '@playwright/test'
import { writeFile } from 'node:fs/promises'

// #3735：三条都是「肉眼看代码看不出来、只有真浏览器能判」的呈现问题，所以这些断言
// 必须在真实浏览器里跑：
//
//   ① 指标卡在 1440px 下被裁掉右半 —— jsdom 不实现 flex 布局、也不加载 SFC 样式表，
//      单测里量宽度只会得到假绿。判据是「条带 scrollWidth 不超过卡片宽 + 四格等宽」。
//   ③ 生产日报筛选区吃掉整个首屏 —— 判据是六个筛选器顶沿同高（横排）且表头落在 900px 内。
//   ④ 工序列不再重复渲染完整工单号 —— 判据是工单号字符串在该单元格内出现 0 次。
//
// 四条断言都在**未修复**的代码上验过是红的（实测数字见 PR 描述），不是先写断言再补实现。

const STORAGE_KEY = 'nerv-iip.business-console.auth'

const principal = {
  principalId: 'principal-1',
  principalType: 'User',
  loginName: 'admin',
  email: 'admin@example.test',
  organizationId: 'org-001',
  environmentId: 'env-dev',
  permissionVersion: 1,
  permissionCodes: [
    'business.mes.overview.read',
    'business.mes.operations.read',
    'business.mes.work-orders.read',
    'business.mes.work-orders.write',
    'business.mes.reporting.read',
    'business.mes.operations.write',
    'business.mes.reporting.write',
    'business.masterdata.resources.read',
    'business.erp.procurement.read',
  ],
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

test('① 1440px 下指标卡等宽不溢出，第四格的文字完整可见（#3735）', async ({ page }) => {
  await page.goto('/mes', { waitUntil: 'domcontentloaded' })

  // 驾驶舱上有两条指标条：上面「我负责的 · …」四格，下面「全厂…」四格。
  // 票面说的就是四卡那一条，两条都量——共享组件改坏了任一条都要看得见。
  const strips = page.locator('[data-slot="nv-metric-strip"]')
  await expect(strips.first()).toBeVisible({ timeout: 15_000 })
  await expect(strips).toHaveCount(2)

  const count = await strips.count()
  for (let index = 0; index < count; index += 1) {
    const measured = await strips.nth(index).evaluate((root) => {
      const card = root.getBoundingClientRect()
      const cells = Array.from(root.children).map((cell) => {
        const box = cell.getBoundingClientRect()
        return {
          label: cell.querySelector('p')?.textContent?.trim() ?? '',
          width: box.width,
          // 溢出判据：格子右沿不得越过卡片右沿。
          overflowRight: box.right - card.right,
        }
      })
      return { cardWidth: card.width, scrollWidth: root.scrollWidth, cells }
    })

    expect(measured.cells, '指标条应渲染出格子').toHaveLength(4)
    expect(
      measured.scrollWidth,
      `指标条内容 ${measured.scrollWidth}px 宽于容器 ${measured.cardWidth.toFixed(1)}px`,
    ).toBeLessThanOrEqual(Math.ceil(measured.cardWidth) + 1)

    for (const cell of measured.cells) {
      expect(
        cell.overflowRight,
        `格「${cell.label}」右沿溢出卡片 ${cell.overflowRight.toFixed(1)}px`,
      ).toBeLessThanOrEqual(1)
    }

    // 等宽：四格宽度差在 subpixel 舍入内。
    const widths = measured.cells.map((cell) => cell.width)
    expect(Math.max(...widths) - Math.min(...widths)).toBeLessThanOrEqual(1)
  }
})

test('③ 生产日报筛选器横排，数据表头落在首屏内（#3735）', async ({ page }) => {
  await page.goto('/mes/reports', { waitUntil: 'domcontentloaded' })
  const toolbar = page.locator('[data-slot="nv-field"]')
  await expect(toolbar.first()).toBeVisible({ timeout: 15_000 })

  // 六个筛选器横向排布：最右一个的顶沿不得低于最左一个的底沿（即在同一行内）。
  const rows = await toolbar.evaluateAll((fields) => {
    const tops = fields.map((field) => Math.round(field.getBoundingClientRect().top))
    return [...new Set(tops)]
  })
  expect(
    rows.length,
    `六个筛选器应横向排布，实际占了 ${rows.length} 行（顶沿 ${rows.join(',')}）`,
  ).toBeLessThanOrEqual(2)

  // 首屏（900px）内能看到数据表头，而不是被筛选区顶到折叠线以下。
  const head = page.locator('table.nv-dt-table thead th').first()
  await expect(head).toBeVisible({ timeout: 15_000 })
  const headTop = await head.evaluate((element) => element.getBoundingClientRect().top)
  expect(headTop, `数据表头在 ${headTop.toFixed(0)}px，落在 900px 首屏之外`).toBeLessThan(900)
})

test('③ 业务日可清除，且始终是本地化日期而不是美式 mm/dd/yyyy（#3735）', async ({ page }) => {
  await page.goto('/mes/reports', { waitUntil: 'domcontentloaded' })
  const field = page
    .locator('[data-slot="nv-field"]')
    .filter({ has: page.getByText('业务日', { exact: true }) })
    .first()
  await expect(field).toBeVisible({ timeout: 15_000 })

  // 断言行为而不是组件形状：票面要的是「不出现未样式化的 `mm/dd/yyyy`」，
  // 任何本地化日期控件都该过。选一个非今天的日子，屏幕上是 `YYYY-MM-DD`。
  const trigger = field.locator('button').first()
  await trigger.click()
  const day = page.locator('[role="dialog"] button', { hasText: /^15$/ }).first()
  await day.click()
  await expect(field).toHaveText(/202\d-\d{2}-\d{2}/)
  // 美式顺序一旦漏回来这里会红：`15/09/2026` 不匹配上面的模式。
  expect(await field.innerText()).not.toMatch(/\d{1,2}\/\d{1,2}\/\d{4}/)

  // 被它替掉的原生 `<input type="date">` 自带清除按钮，日历触发器没有——不补
  // 清除路径，选完就只能刷页才能回到「全部业务日」（#3735 阻断项）。
  const clear = field.getByRole('button', { name: /清除/ })
  await expect(clear).toBeVisible()
  await clear.click()
  await expect(field).toContainText('选择业务日')
  expect(await field.getByRole('button', { name: /清除/ }).count()).toBe(0)
})

test('④ 工序列不再逐道重复渲染完整工单号（#3735）', async ({ page }) => {
  await page.goto('/mes/work-orders', { waitUntil: 'domcontentloaded' })
  const firstRow = page.locator('table.nv-dt-table tbody tr').first()
  await expect(firstRow).toBeVisible({ timeout: 15_000 })

  // 按表头定位「工序」列，不靠单元格文案（工序号是 10/20/…，不是 1/2/…）。
  const columnIndex = await page
    .locator('table.nv-dt-table thead th')
    .evaluateAll((heads) => heads.findIndex((head) => head.textContent?.trim() === '工序'))
  expect(columnIndex, '工单表未找到〈工序〉列').toBeGreaterThan(0)

  const operations = await firstRow
    .locator('td')
    .nth(columnIndex)
    .filter({ hasText: '道' })
    .innerText()

  // 单号形如 WO-20260922-000001-OP-10：工序列里一次都不该再出现。
  expect(operations, `工序列仍在重复渲染单号：\n${operations}`).not.toMatch(/WO-[\w-]*-OP-\d+/)
  // 「第几道 · 工作中心」和状态是这格真正要留的信息。
  expect(operations).toMatch(/第\s*\d+\s*道/)
  expect(operations).toContain('缸筒加工中心一线')
  expect(operations).toContain('执行中')
})

// #4306：登记的中文名称与业务号；短行同时守住普通列表显示。
const overlapResources = {
  'device-asset': [
    {
      resourceType: 'device-asset',
      code: 'EQ00001',
      displayName: '数控车床 CK6150（精密轴套加工）',
      active: true,
      snapshotVersion: '2026-09-28T09:00:00Z',
      siteCode: 'SITE-01',
    },
    {
      resourceType: 'device-asset',
      code: 'EQ02',
      displayName: '车床',
      active: true,
      snapshotVersion: '2026-09-28T09:00:00Z',
      siteCode: 'SITE-02',
    },
  ],
  site: [
    {
      resourceType: 'site',
      code: 'SITE-01',
      displayName: '常州精密机械制造有限公司第一工厂',
      active: true,
      snapshotVersion: '1',
    },
    {
      resourceType: 'site',
      code: 'SITE-02',
      displayName: '一厂',
      active: true,
      snapshotVersion: '1',
    },
  ],
  'business-partner': [
    {
      resourceType: 'business-partner',
      code: 'SUP-20260928-000001',
      displayName: '常州恒力弹簧有限公司',
      partnerType: 'Supplier',
      partnerRoles: ['Supplier'],
      active: true,
      snapshotVersion: '1',
    },
    {
      resourceType: 'business-partner',
      code: 'SUP02',
      displayName: '恒力',
      partnerType: 'Supplier',
      partnerRoles: ['Supplier'],
      active: true,
      snapshotVersion: '1',
    },
  ],
  sku: [
    {
      resourceType: 'sku',
      code: 'RM-20260928-000001',
      displayName: '精密数控车床用高强度合金弹簧轴套',
      baseUomCode: 'EA',
      active: true,
      snapshotVersion: '1',
    },
    {
      resourceType: 'sku',
      code: 'RM02',
      displayName: '轴套',
      baseUomCode: 'EA',
      active: true,
      snapshotVersion: '1',
    },
  ],
}

const overlapCases = [
  {
    path: '/master-data/devices',
    name: '设备',
    headers: ['设备编码', '设备名称', '工厂'],
    values: [
      ['EQ00001', '数控车床 CK6150（精密轴套加工）', '常州精密机械制造有限公司第一工厂'],
      ['EQ02', '车床', '一厂'],
    ],
  },
  {
    path: '/erp/procurement/purchase-orders',
    name: '采购',
    headers: ['采购单', '供应商', '物料'],
    values: [
      ['PO-20260928-000001', '常州恒力弹簧有限公司', '精密数控车床用高强度合金弹簧轴套'],
      ['PO02', '恒力', '轴套'],
    ],
  },
  {
    path: '/mes/production-reports',
    name: '报工',
    headers: ['报工单', '工单', '工序任务'],
    values: [
      ['PRPT-20260928-000001', 'WO-20260928-000001', 'WO-20260928-000001-OP-10'],
      ['PR02', 'WO02', 'WO02-OP10'],
    ],
  },
]

for (const scenario of overlapCases) {
  test(`#4306 ${scenario.name}：长内容完整可读且不覆盖邻列，短行与操作保持`, async ({
    page,
  }, testInfo) => {
    await page.goto(scenario.path, { waitUntil: 'domcontentloaded' })
    const table = page.locator('table.nv-dt-table').first()
    await expect(table).toBeVisible()
    const rows = table.locator('tbody tr')
    await expect(rows).toHaveCount(2)
    await expect(rows.first()).toContainText(scenario.values[0][0])
    // 等目录名称进入真实页面，避免拿“目录尚未到”的业务码测中文布局。
    for (const value of scenario.values[0]) await expect(rows.first()).toContainText(value)
    await page.evaluate(() => document.fonts.ready)

    const measurements = []
    for (const header of scenario.headers) {
      const index = await table
        .locator('thead th')
        .evaluateAll(
          (heads, label) => heads.findIndex((head) => head.textContent?.trim() === label),
          header,
        )
      expect(index, `找不到${header}列`).toBeGreaterThanOrEqual(0)
      for (let rowIndex = 0; rowIndex < scenario.values.length; rowIndex += 1) {
        const cell = rows.nth(rowIndex).locator('td').nth(index)
        const expected = scenario.values[rowIndex][scenario.headers.indexOf(header)]
        await expect(cell).toContainText(expected)
        const measured = await cell.evaluate((element) => {
          const box = element.getBoundingClientRect()
          const style = getComputedStyle(element)
          const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT)
          const fragments = []
          let node = walker.nextNode()
          while (node) {
            if (node.textContent?.trim()) {
              const range = document.createRange()
              range.selectNodeContents(node)
              for (const rect of range.getClientRects()) {
                fragments.push({
                  text: node.textContent.trim(),
                  left: rect.left,
                  right: rect.right,
                  top: rect.top,
                  bottom: rect.bottom,
                })
              }
            }
            node = walker.nextNode()
          }
          return {
            left: box.left + parseFloat(style.paddingLeft),
            right: box.right - parseFloat(style.paddingRight),
            top: box.top,
            bottom: box.bottom,
            width: box.width,
            scrollWidth: element.scrollWidth,
            clientWidth: element.clientWidth,
            fragments,
          }
        })
        measurements.push({ header, rowIndex, expected, ...measured })
      }
    }
    await writeFile(
      testInfo.outputPath('4306-geometry.json'),
      JSON.stringify(measurements, null, 2),
    )
    await page.screenshot({ path: testInfo.outputPath('4306-layout.png'), fullPage: true })
    for (const cell of measurements) {
      expect.soft(cell.width, `${cell.header}应有可读宽度`).toBeGreaterThan(16)
      expect.soft(cell.fragments.length, `${cell.header}应有实际绘制文本`).toBeGreaterThan(0)
      expect
        .soft(cell.scrollWidth, `${cell.header}内容不得横向溢出`)
        .toBeLessThanOrEqual(cell.clientWidth + 1)
      for (const fragment of cell.fragments) {
        expect
          .soft(fragment.left, `${cell.header}：${fragment.text}左侧越界`)
          .toBeGreaterThanOrEqual(cell.left - 1)
        expect
          .soft(fragment.right, `${cell.header}：${fragment.text}覆盖邻列`)
          .toBeLessThanOrEqual(cell.right + 1)
        expect
          .soft(fragment.top, `${cell.header}文本顶部应可读`)
          .toBeGreaterThanOrEqual(cell.top - 1)
        expect
          .soft(fragment.bottom, `${cell.header}文本底部应可读`)
          .toBeLessThanOrEqual(cell.bottom + 1)
      }
    }
    if (scenario.name === '设备') {
      await rows.nth(1).getByRole('button', { name: '设备操作 EQ02', exact: true }).click()
      await page.getByRole('menuitem', { name: '查看详情' }).click()
      await expect(page.getByRole('dialog')).toContainText('车床')
    }
    if (scenario.name === '采购') {
      // 未改列表筛选语义：普通短行仍可读，选择框可操作。
      await page.getByRole('combobox', { name: '订单状态' }).click()
      await page.getByRole('option', { name: '已关闭', exact: true }).click()
      await expect(page.getByRole('combobox', { name: '订单状态' })).toContainText('已关闭')
    }
    if (scenario.name === '报工') {
      await expect(rows.nth(1).getByRole('button', { name: '冲销', exact: true })).toBeEnabled()
      await rows.nth(1).getByRole('button', { name: 'WO02', exact: true }).click()
      await expect(page.getByRole('dialog')).toContainText('工单速览')
      await expect(page.getByRole('dialog')).toContainText('wo-short')
    }
  })
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

  if (pathname === '/api/business-console/v1/master-data/resources') {
    const resourceType = new URL(route.request().url()).searchParams.get('resourceType')
    const resources = overlapResources[resourceType as keyof typeof overlapResources] ?? []
    return route.fulfill({ json: { success: true, data: { resources, total: resources.length } } })
  }
  if (pathname === '/api/business-console/v1/master-data/skus') {
    return route.fulfill({
      json: { success: true, data: { resources: overlapResources.sku, total: 2 } },
    })
  }
  if (pathname === '/api/business-console/v1/erp/procurement/purchase-orders') {
    const items = [
      {
        purchaseOrderNo: 'PO-20260928-000001',
        supplierCode: 'SUP-20260928-000001',
        siteCode: 'SITE-01',
        status: 'Released',
        receiptReadiness: 'Open',
        totalAmount: 120,
        lines: [
          {
            lineNo: '10',
            skuCode: 'RM-20260928-000001',
            uomCode: 'EA',
            orderedQuantity: 12,
            receivedQuantity: 0,
            openQuantity: 12,
            finalDelivery: false,
            unitPrice: 10,
            promisedDate: '2026-09-30',
          },
        ],
      },
      {
        purchaseOrderNo: 'PO02',
        supplierCode: 'SUP02',
        siteCode: 'SITE-02',
        status: 'Released',
        receiptReadiness: 'Open',
        totalAmount: 10,
        lines: [
          {
            lineNo: '10',
            skuCode: 'RM02',
            uomCode: 'EA',
            orderedQuantity: 1,
            receivedQuantity: 0,
            openQuantity: 1,
            finalDelivery: false,
            unitPrice: 10,
            promisedDate: '2026-09-30',
          },
        ],
      },
    ]
    return route.fulfill({ json: { success: true, data: { items, total: 2 } } })
  }
  if (pathname === '/api/business-console/v1/mes/production-reports') {
    const items = [
      {
        productionReportId: 'report-long',
        reportNo: 'PRPT-20260928-000001',
        reversalReportNo: 'PRPT-20260928-000002',
        workOrderId: 'wo-long',
        workOrderNo: 'WO-20260928-000001',
        operationTaskId: 'task-long',
        operationTaskNo: 'WO-20260928-000001-OP-10',
        goodQuantity: 12,
        scrapQuantity: 0,
        reworkQuantity: 0,
        reportedAtUtc: '2026-09-28T09:00:00Z',
        workOrderStatus: 'started',
        operationActualLaborHours: 1.25,
        operationActualMachineHours: 0.5,
      },
      {
        productionReportId: 'report-short',
        reportNo: 'PR02',
        workOrderId: 'wo-short',
        workOrderNo: 'WO02',
        operationTaskId: 'task-short',
        operationTaskNo: 'WO02-OP10',
        goodQuantity: 1,
        scrapQuantity: 0,
        reworkQuantity: 0,
        reportedAtUtc: '2026-09-28T09:00:00Z',
        workOrderStatus: 'started',
        operationActualLaborHours: 0.25,
        operationActualMachineHours: 0.5,
      },
    ]
    return route.fulfill({ json: { success: true, data: { items, total: 2 } } })
  }

  // 作业范围是工单/工序列表的准入前提：读面按 `selectedScope` 过滤，
  // 响应里必须同时给 `authorizedScopes`（可选清单）与 `selectedScope`（当前选择），
  // 否则页面停在「没有已授权的作业范围」而不出数据行。
  if (pathname === '/api/business-console/v1/me/work-context') {
    return route.fulfill({
      json: {
        success: true,
        data: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          applicablePermissionCode: 'business.mes.work-orders.read',
          resolutionStatus: 'resolved',
          authorizedScopes: [
            { kind: 'workCenter', id: 'WC-CYL-01', displayName: '缸筒加工中心一线' },
            { kind: 'team', id: 'TEAM-A', displayName: '缸筒班组' },
          ],
          selectedScope: {
            kind: 'workCenter',
            id: 'WC-CYL-01',
            displayName: '缸筒加工中心一线',
          },
        },
      },
    })
  }

  if (pathname === '/api/business-console/v1/mes/overview') {
    return route.fulfill({
      json: {
        success: true,
        data: {
          counts: [
            { key: 'work-orders', count: 4760 },
            { key: 'operation-tasks', count: 31554 },
          ],
          blockers: [
            { areaCode: 'material', code: 'missing', message: '缺料待补', count: 12 },
            { areaCode: 'quality', code: 'hold', message: '质量保留', count: 5 },
          ],
          pendingWork: [
            {
              roleCode: 'dispatcher',
              workType: '待下达工单',
              count: 7,
              routeHint: '/mes/work-orders',
            },
            { roleCode: 'supervisor', workType: '待派工', count: 21, routeHint: '/mes/dispatch' },
            {
              roleCode: 'operator',
              workType: '待报工',
              count: 63,
              routeHint: '/mes/operation-tasks',
            },
          ],
        },
      },
    })
  }

  // 工序任务列表：返回带 total 的信封，四个状态各来一次。
  if (pathname === '/api/business-console/v1/mes/operation-tasks') {
    const status = new URL(route.request().url()).searchParams.get('status')
    const totals: Record<string, number> = {
      queued: 88,
      inProgress: 34,
      paused: 6,
      scheduleInvalidated: 9,
    }
    return route.fulfill({
      json: {
        success: true,
        data: { items: [], total: totals[status ?? ''] ?? 0 },
      },
    })
  }

  if (pathname === '/api/business-console/v1/mes/work-orders') {
    return route.fulfill({
      json: {
        success: true,
        data: {
          total: 2,
          items: [
            {
              workOrderId: 'wo-1',
              workOrderNo: 'WO-20260922-000001',
              skuCode: 'SKU-CYL-01',
              status: 'released',
              quantity: 120,
              dueUtc: '2026-09-30T08:00:00.000Z',
              operationTasks: [
                {
                  operationTaskId: 'ot-1',
                  operationTaskNo: 'WO-20260922-000001-OP-10',
                  operationSequence: 10,
                  workCenterCode: 'WC-CYL-01',
                  workCenterName: '缸筒加工中心一线',
                  status: 'InProgress',
                },
                {
                  operationTaskId: 'ot-2',
                  operationTaskNo: 'WO-20260922-000001-OP-20',
                  operationSequence: 20,
                  workCenterCode: 'WC-CYL-02',
                  workCenterName: '缸筒热处理',
                  status: 'Queued',
                },
              ],
            },
          ],
        },
      },
    })
  }

  if (pathname === '/api/business-console/v1/mes/production-statistics') {
    return route.fulfill({
      json: {
        success: true,
        data: {
          total: 2,
          items: [
            { key: '2026-09-22', dimensionValue: '2026-09-22', completedQuantity: 1240 },
            { key: '2026-09-21', dimensionValue: '2026-09-21', completedQuantity: 1180 },
          ],
        },
      },
    })
  }

  if (pathname === '/api/business-console/v1/mes/wip') {
    return route.fulfill({
      json: {
        success: true,
        data: { total: 2, items: [{ skuCode: 'SKU-CYL-01', workInProcessQuantity: 320 }] },
      },
    })
  }

  if (pathname.includes('/telemetry/oee-aggregates')) {
    return route.fulfill({ json: { success: true, data: { total: 0, items: [] } } })
  }

  return route.fulfill({ json: { success: true, data: {} } })
}
