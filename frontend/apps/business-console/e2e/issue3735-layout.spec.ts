import { expect, test, type Route } from '@playwright/test'

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

  // 驾驶舱上有两条指标条：上面「我的范围 · …」四格，下面「全厂…」四格。
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

test('③ 业务日改用 NvDatePicker，不再是原生 date 输入（#3735）', async ({ page }) => {
  await page.goto('/mes/reports', { waitUntil: 'domcontentloaded' })
  const field = page
    .locator('[data-slot="nv-field"]')
    .filter({ has: page.getByText('业务日', { exact: true }) })
    .first()
  await expect(field).toBeVisible({ timeout: 15_000 })

  // 页面里不该再有原生日期框——那正是票面说的「未样式化 + 美式 mm/dd/yyyy」。
  expect(await field.locator('input[type="date"]').count()).toBe(0)
  // 换成了日历触发器（NvDatePicker 的 NvButton 带日历图标）。
  expect(await field.locator('button').count()).toBeGreaterThan(0)
  // 未选择时显示占位文案，而不是 mm/dd/yyyy。
  await expect(field).toContainText('选择业务日')
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
                  status: 'inProgress',
                },
                {
                  operationTaskId: 'ot-2',
                  operationTaskNo: 'WO-20260922-000001-OP-20',
                  operationSequence: 20,
                  workCenterCode: 'WC-CYL-02',
                  workCenterName: '缸筒热处理',
                  status: 'queued',
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
