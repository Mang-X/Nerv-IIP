import { expect, test, type Page } from '@playwright/test'

const salesOrderNo = 'SO-100'
const targetDelivery = 'DO-314'
const shipments = [
  ...Array.from({ length: 12 }, (_, index) => ({
    salesOrderNo: 'SO-1000',
    deliveryOrderNo: `DO-OTHER-${index}`,
  })),
  ...Array.from({ length: 90 }, (_, i) => ({
    salesOrderNo: 'SO-1009',
    deliveryOrderNo: 'DO-NEAR-' + i,
  })),
  { salesOrderNo, deliveryOrderNo: targetDelivery },
  {
    salesOrderNo,
    deliveryOrderNo: 'DO-OLD',
    lines: [{ skuCode: 'SKU-MOTOR', quantity: 48, shippedQuantity: 48, uomCode: 'EA' }],
  },
]

async function salesSession(page: Page, permissions = ['business.erp.sales.read']) {
  const principal = {
    principalId: 'sales-user',
    principalType: 'User',
    loginName: 'sales',
    organizationId: 'org-sales',
    environmentId: 'env-sales',
    permissionVersion: 1,
    permissionCodes: permissions,
  }
  const session = {
    principal,
    accessToken: 'fixture-access',
    refreshToken: 'fixture-refresh',
    sessionId: 'fixture-sales',
    expiresAtUtc: '2099-01-01T00:00:00Z',
  }
  await page.addInitScript(
    (value) => localStorage.setItem('nerv-iip.business-console.auth', JSON.stringify(value)),
    session,
  )
  await page.route(
    (url) => url.pathname.startsWith('/api/'),
    async (route) => {
      const { pathname, searchParams } = new URL(route.request().url())
      let data: unknown
      if (
        [
          '/master-data/resources',
          '/master-data/skus',
          '/planning/mps',
          '/planning/demands',
          '/planning/mrp-runs',
          '/planning/suggestions',
          '/erp/finance/payables',
          '/erp/procurement/purchase-orders',
        ].some((path) => pathname.endsWith(path))
      )
        data = { items: [], total: 0 }
      if (pathname.endsWith('/erp/finance/receivables/by-source')) {
        const source = searchParams.get('sourceDocumentNo')
        if (
          searchParams.get('organizationId') !== 'org-sales' ||
          searchParams.get('environmentId') !== 'env-sales'
        )
          throw new Error('应收复核范围错误')
        if (source !== 'DO-OLD')
          return route.fulfill({
            status: 400,
            json: {
              success: false,
              code: 400,
              message: `来源单据『${source}』的应收单不存在。`,
              errorData: [],
            },
          })
        data = { receivableNo: 'AR-OLD', sourceDocumentNo: source, openAmount: 30 }
      }
      if (pathname.endsWith('/erp/finance/receivables')) {
        const keyword = searchParams.get('keyword') ?? ''
        const rows = [
          ...Array.from({ length: 201 }, (_, i) => ({
            receivableNo: 'AR-NEAR-' + i,
            sourceDocumentNo: 'DO-OLD9',
            openAmount: 99,
          })),
          ...Array.from({ length: 201 }, (_, i) => ({
            receivableNo: 'AR-OTHER-' + i,
            sourceDocumentNo: targetDelivery + '9',
            openAmount: 1,
          })),
          { receivableNo: 'AR-OLD', sourceDocumentNo: 'DO-OLD', openAmount: 30 },
        ].filter(
          (row) => row.receivableNo.includes(keyword) || row.sourceDocumentNo.includes(keyword),
        )
        const skip = Number(searchParams.get('skip'))
        data = {
          items: rows.slice(skip, skip + Number(searchParams.get('take') ?? 10)),
          total: rows.length,
        }
      }
      if (pathname.endsWith('/order-urgencies')) data = []
      if (pathname.endsWith('/erp/finance/summary')) data = {}
      if (pathname.endsWith('/auth/me')) data = principal
      if (pathname.endsWith('/auth/refresh')) data = session
      if (pathname.endsWith('/sales-orders'))
        data = { items: [{ salesOrderNo, status: 'released' }], total: 1 }
      if (pathname.endsWith('/delivery-orders')) {
        const keyword = searchParams.get('keyword') ?? ''
        const rows = shipments.filter(
          (row) => row.salesOrderNo.includes(keyword) || row.deliveryOrderNo.includes(keyword),
        )
        const skip = Number(searchParams.get('skip'))
        data = {
          items: rows.slice(skip, skip + Number(searchParams.get('take') ?? 10)),
          total: rows.length,
        }
      }
      if (data === undefined) throw new Error('未声明的请求 ' + pathname)
      await route.fulfill({ json: { success: true, data } })
    },
  )
}

async function openTimeline(page: Page) {
  await page.goto('/erp/sales/orders')
  await page.getByRole('button', { name: '履约追踪' }).click()
}

test('从订单节点打开实际发货单，返回后普通入口仍可搜索及翻页', async ({ page }) => {
  await salesSession(page)
  await openTimeline(page)
  const node = page
    .locator('.nv-ft-node')
    .filter({ has: page.getByText('发货单', { exact: true }) })
  const targetRead = page.waitForRequest((req) => {
    const url = new URL(req.url())
    return (
      url.pathname.endsWith('/delivery-orders') &&
      url.searchParams.get('keyword') === targetDelivery &&
      url.searchParams.get('organizationId') === 'org-sales' &&
      url.searchParams.get('environmentId') === 'env-sales'
    )
  })
  await node.getByRole('link', { name: '查看发货单 · DO-314', exact: true }).click()
  const keyword = page.getByRole('textbox', { name: '发货关键字' })
  await expect(keyword).toHaveValue(targetDelivery)
  await targetRead
  await expect(page.getByRole('row').filter({ hasText: targetDelivery })).toBeVisible()
  await page.goBack()
  await expect(page.getByRole('dialog')).toContainText(salesOrderNo)
  await page.goto('/erp/sales/deliveries?keyword=SO-100')
  await expect(keyword).toHaveValue('SO-100')
  await keyword.fill('DO-OTHER')
  await page.getByRole('button', { name: /下一页/ }).click()
  await expect(page.getByText('DO-OTHER-11', { exact: true })).toBeVisible()
  const menu = page.locator('a[href="/erp/sales/deliveries"]')
  if (!(await menu.isVisible())) await page.getByRole('button', { name: '展开或收起侧栏' }).click()
  await menu.click()
  if (await page.getByRole('dialog').isVisible()) await page.keyboard.press('Escape')
  await expect(keyword).toHaveValue('')
  await expect(page.getByText('DO-OTHER-0', { exact: true })).toBeVisible()
})

test('发货深链接使用现有销售读权限', async ({ page }) => {
  await salesSession(page, ['business.planning.mrp.read'])
  await page.goto('/erp/sales/deliveries?keyword=DO-314')
  await expect(page).toHaveURL(/\/forbidden\?/)
  await expect(page.getByRole('textbox', { name: '发货关键字' })).toHaveCount(0)
})

test('发货单号未命中时可清除来源筛选并恢复列表', async ({ page }) => {
  await salesSession(page)
  await page.goto('/erp/sales/deliveries?keyword=DO-MISSING')
  await expect(page.getByText(/当前筛选条件下没有匹配的发货单/)).toBeVisible()
  await expect(page.getByRole('link', { name: '去销售订单' })).toHaveCount(0)
  await page.getByRole('button', { name: '清空筛选' }).click()
  await expect(page).toHaveURL(/\/erp\/sales\/deliveries$/)
  await expect(page.getByRole('textbox', { name: '发货关键字' })).toHaveValue('')
  await expect(page.getByText('DO-OTHER-0', { exact: true })).toBeVisible()
})

const suggestions = [
  { suggestionId: 'new-suggestion', runId: 'new-run', quantity: 12, status: 'Open' },
  { suggestionId: 'accepted-suggestion', runId: 'old-run', quantity: 48, status: 'Accepted' },
].map((row) => ({
  ...row,
  skuCode: 'SKU-MOTOR',
  uomCode: 'EA',
  suggestionType: 'planned-work-order',
}))

test('同一物料跨运行时，从屏上建议进入对应历史运行和建议行', async ({ page }) => {
  await salesSession(page, ['business.erp.sales.read', 'business.planning.mrp.read'])
  const reads: URL[] = []
  let releaseSuggestion!: () => void
  const acceptedResponse = new Promise<void>((resolve) => {
    releaseSuggestion = resolve
  })
  await page.route('**/v1/planning/**', async (route) => {
    const url = new URL(route.request().url())
    reads.push(url)
    let items: unknown[]
    if (url.pathname.endsWith('/mrp-runs')) {
      items = suggestions.map((row) => ({ runId: row.runId, status: 'Completed' }))
    } else if (url.pathname.endsWith('/pegging')) {
      const source = suggestions.find((row) => url.pathname.endsWith('/' + row.runId + '/pegging'))!
      const peg = {
        suggestionId: source.suggestionId,
        peggingType: 'demand',
        demandSourceReference: salesOrderNo,
        parentSkuCode: source.skuCode,
        quantity: source.quantity,
      }
      items = [{ ...peg, suggestionId: 'unrelated', demandSourceReference: 'SO-1000' }, peg]
    } else if (url.pathname.endsWith('/suggestions')) {
      if (url.searchParams.get('status') === 'Accepted') await acceptedResponse
      items = suggestions.filter(
        (row) => !url.searchParams.has('status') || row.status === url.searchParams.get('status'),
      )
    } else {
      return route.fallback()
    }
    await route.fulfill({ json: { success: true, data: { items, total: items.length } } })
  })
  await openTimeline(page)
  const node = page
    .locator('.nv-ft-node')
    .filter({ has: page.getByText('MRP 建议', { exact: true }) })
  try {
    await expect(node).toContainText('加载中')
    await expect(node.getByRole('link')).toHaveCount(0)
  } finally {
    releaseSuggestion()
  }
  await expect(node).toContainText('SKU-MOTOR × 48 EA')
  await node.getByRole('link').click()
  await expect(page).toHaveURL(/\/planning\?suggestionId=accepted-suggestion$/)
  await expect(page.getByRole('combobox', { name: '建议运行' })).toContainText('第 1 次')
  const row = page.getByRole('row').filter({ hasText: 'SKU-MOTOR' })
  await expect(row).toHaveCount(1)
  await expect(row).toContainText('48')
  await expect(row.getByRole('button', { name: '定位追溯' })).toBeVisible()
  expect(
    reads.some(
      (url) =>
        url.pathname.endsWith('/suggestions') &&
        !url.searchParams.has('status') &&
        url.searchParams.get('organizationId') === 'org-sales' &&
        url.searchParams.get('environmentId') === 'env-sales',
    ),
  ).toBe(true)
  await page.goBack()
  await expect(page.getByRole('dialog')).toContainText(salesOrderNo)
})

test('建议节点显示读权限反馈，规划深链接沿用路由权限', async ({ page }) => {
  await salesSession(page)
  await page.route('**/v1/planning/mrp-runs?**', (route) =>
    route.fulfill({ status: 403, json: { success: false } }),
  )
  await openTimeline(page)
  const node = page
    .locator('.nv-ft-node')
    .filter({ has: page.getByText('MRP 建议', { exact: true }) })
  await expect(node).toContainText('权限受限')
  await expect(node.getByRole('link')).toHaveCount(0)
  await page.goto('/planning?suggestionId=accepted-suggestion')
  await expect(page).toHaveURL(/\/forbidden\?/)
})

test('需求岗按来源打开需求池，读取和刷新均遵循岗位权限', async ({ page }) => {
  const forbiddenReads: string[] = []
  page.on('request', (req) => {
    if (/\/planning\/(mrp-runs|mps|suggestions)(?:[/?]|$)/.test(req.url()))
      forbiddenReads.push(req.url())
  })
  await salesSession(page, ['business.erp.sales.read', 'business.planning.demands.read'])
  await page.route('**/v1/planning/demands?**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: {
          items: [
            {
              sourceReference: salesOrderNo,
              demandType: 'sales-order',
              demandSourceId: 'demand1',
              skuCode: 'SKU-DEMAND',
            },
          ],
          total: 1,
        },
      },
    }),
  )
  await openTimeline(page)
  const node = page.locator('.nv-ft-node')
  const suggestion = node.filter({ has: page.getByText('MRP 建议', { exact: true }) })
  await expect(suggestion).toContainText('权限受限')
  await expect(suggestion.getByRole('link')).toHaveCount(0)
  await node
    .filter({ has: page.getByText('生产需求', { exact: true }) })
    .getByRole('link')
    .click()
  await expect(page).toHaveURL(/\/planning\?demandSource=SO-100$/)
  await expect(page.getByRole('row').filter({ hasText: 'SKU-DEMAND' })).toContainText(salesOrderNo)
  for (const name of ['运行 MRP', '新建 MPS', '新建需求']) {
    await expect(page.getByRole('button', { name, exact: true })).toHaveCount(0)
  }
  await expect(page.getByRole('tab', { name: /需求池/ })).toHaveAttribute('aria-selected', 'true')
  await Promise.all([
    page.waitForResponse(
      (response) => new URL(response.url()).pathname.endsWith('/planning/demands') && response.ok(),
    ),
    page.getByRole('button', { name: '刷新', exact: true }).first().click(),
  ])
  expect(forbiddenReads).toEqual([])
})

test('后发单尚无应收时仍能核对早先发货的应收', async ({ page }) => {
  await salesSession(page, ['business.erp.sales.read', 'business.erp.finance.read'])
  await openTimeline(page)
  await expect(page.getByText('AR-OLD', { exact: true })).toBeVisible()
  await expect(page.getByText(/SKU-MOTOR 已发 48 \/ 48 EA/)).toBeVisible()
  await expect(page.getByText(/DO-314 · 未查到应收/)).toBeVisible()
  await expect(page.getByText(/AR-NEAR-0/)).toHaveCount(0)
  await page.getByRole('link', { name: '核对账单 · AR-OLD', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '应收关键字' })).toHaveValue('AR-OLD')
  await expect(page.getByRole('row').filter({ hasText: 'AR-OLD' })).toContainText('DO-OLD')
  await page.goto('/erp/finance/ar-ap?keyword=AR-MISSING&from=timeline')
  await page.getByRole('button', { name: '清空应收筛选' }).click()
  await expect(page).toHaveURL(/\/erp\/finance\/ar-ap\?from=timeline$/)
  await expect(page.getByRole('row').filter({ hasText: 'AR-NEAR-0' })).toBeVisible()
})

test('应收按来源保留可读账单，并区分拒绝与可重试错误', async ({ page }) => {
  await salesSession(page, ['business.erp.sales.read', 'business.erp.finance.read'])
  let status = 400
  await page.route('**/erp/finance/receivables/by-source?**', (route) =>
    new URL(route.request().url()).searchParams.get('sourceDocumentNo') === targetDelivery
      ? route.fulfill({
          status,
          json: { success: false, code: status, message: '请求未通过校验。', errorData: [] },
        })
      : route.fallback(),
  )
  await openTimeline(page)
  const node = page.locator('.nv-ft-node').filter({ has: page.getByText('应收', { exact: true }) })
  await expect(node).toContainText('DO-314 · 读取失败')
  await expect(node.getByRole('link', { name: '核对账单 · AR-OLD' })).toBeVisible()
  status = 403
  await node.getByRole('button', { name: '重试' }).click()
  await expect(node).toContainText('DO-314 · 权限受限')
  await expect(node.getByRole('link', { name: '按来源核对应收 · DO-314' })).toHaveCount(0)
  await expect(node.getByRole('link', { name: '核对账单 · AR-OLD' })).toBeVisible()
  await page.route('**/erp/finance/receivables?**', (route) =>
    route.fulfill({ status: 403, json: { success: false, code: 403, message: '禁止访问。' } }),
  )
  await openTimeline(page)
  await expect(node).toContainText('权限受限，无权查看该节点数据。')
  await expect(node.getByRole('link')).toHaveCount(0)
})

for (const status of [403, 503]) {
  test(`建议本体返回 ${status} 时保留发货事实及适用恢复入口`, async ({ page }) => {
    await salesSession(page, ['business.erp.sales.read', 'business.planning.mrp.read'])
    const items = (rows: unknown[]) => ({ json: { success: true, data: { items: rows } } })
    await page.route('**/planning/mrp-runs?**', (route) =>
      route.fulfill(items([{ runId: 'run-1' }])),
    )
    await page.route('**/run-1/pegging?**', (route) =>
      route.fulfill(
        items([
          {
            suggestionId: 'S1',
            peggingType: 'demand',
            demandSourceReference: salesOrderNo,
            quantity: 7,
          },
        ]),
      ),
    )
    let failed = true
    await page.route('**/planning/suggestions?**', (route) =>
      route.fulfill(
        failed
          ? { status, json: { success: false } }
          : items([{ suggestionId: 'S1', skuCode: 'MOTOR', quantity: 7 }]),
      ),
    )
    await openTimeline(page)
    const node = page
      .locator('.nv-ft-node')
      .filter({ has: page.getByText('MRP 建议', { exact: true }) })
    await expect(page.getByText(targetDelivery, { exact: true })).toBeVisible()
    if (status === 403) {
      await expect(node).toContainText('权限受限')
      await expect(node.getByRole('link')).toHaveCount(0)
    } else {
      failed = false
      await node.getByRole('button', { name: '重试' }).click()
      await expect(node).toContainText('MOTOR × 7')
    }
  })
}

test('缺少目标运行时给出返回需求池的入口', async ({ page }) => {
  await salesSession(page, [
    'business.planning.mrp.read',
    'business.planning.demands.read',
    'business.planning.mrp.run',
    'business.planning.demands.manage',
    'business.planning.mps.manage',
  ])
  await page.route('**/planning/mrp-runs?**', (route) =>
    route.fulfill({
      json: { success: true, data: { items: [{ runId: 'recent', status: 'Completed' }] } },
    }),
  )
  await page.route('**/planning/suggestions?**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: { items: [{ runId: 'recent', suggestionId: 'other', skuCode: 'OTHER-SKU' }] },
      },
    }),
  )
  await Promise.all([
    page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname.endsWith('/planning/mrp-runs') && response.ok(),
    ),
    page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname.endsWith('/planning/suggestions') && response.ok(),
    ),
    page.goto('/planning?runId=missing'),
  ])
  await expect(
    page.getByText('当前可读范围内未定位到来源建议或运行。', { exact: false }),
  ).toBeVisible()
  await expect(page.getByRole('row').filter({ hasText: 'OTHER-SKU' })).toHaveCount(0)
  for (const name of ['运行 MRP', '新建 MPS', '新建需求']) {
    await expect(page.getByRole('button', { name, exact: true })).toBeVisible()
  }
  await page.getByRole('link', { name: '返回需求池' }).click()
  await expect(page.getByRole('tab', { name: /需求池/ })).toHaveAttribute('aria-selected', 'true')
})

test('已授权出库范围按 ERP 发货单号回读实际出库行', async ({ page }) => {
  await salesSession(page, ['business.erp.sales.read', 'business.wms.shipments.read'])
  await page.route('**/wms/work-scopes/shipments?**', (route) =>
    route.fulfill({
      json: {
        success: true,
        data: { items: [{ scopeKind: 'site', scopeId: 'WH-1', displayName: '一号仓' }] },
      },
    }),
  )
  await page.route('**/wms/operational-candidates/shipments?**', (route) =>
    route.fulfill({
      json: { success: true, data: { items: [] } },
    }),
  )
  await page.route('**/wms/outbound-orders?**', (route) => {
    const params = new URL(route.request().url()).searchParams
    const scoped =
      params.get('scopeKind') === 'site' &&
      params.get('scopeId') === 'WH-1' &&
      params.get('organizationId') === 'org-sales' &&
      params.get('environmentId') === 'env-sales'
    const rows =
      scoped && (!params.get('keyword') || params.get('keyword') === 'DO-OLD')
        ? [
            {
              outboundOrderNo: 'DO-OLD',
              outboundOrderId: 'outbound-old',
              status: 'Completed',
              siteCode: 'WH-1',
            },
          ]
        : []
    return route.fulfill({ json: { success: true, data: { items: rows, total: rows.length } } })
  })
  await openTimeline(page)
  await page.getByRole('link', { name: '核对出库 · DO-OLD', exact: true }).click()
  await expect(page.getByRole('textbox', { name: '关键字搜索' })).toHaveValue('DO-OLD')
  await expect(page.getByRole('row').filter({ hasText: 'DO-OLD' })).toBeVisible()
  await page.goto('/wms/outbound?keyword=DO-MISSING&from=timeline')
  await expect(page.getByText('当前作业范围内没有匹配筛选条件的出库单。')).toBeVisible()
  await page.getByRole('button', { name: '清空筛选' }).click()
  await expect(page).toHaveURL(/\/wms\/outbound\?from=timeline$/)
  await expect(page.getByRole('row').filter({ hasText: 'DO-OLD' })).toBeVisible()
})
