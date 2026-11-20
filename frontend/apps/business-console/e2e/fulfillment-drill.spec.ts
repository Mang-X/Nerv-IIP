import { expect, test, type Page } from '@playwright/test'

const salesOrderNo = 'SO-100'
const targetDelivery = 'DO-314'
const shipments = [
  ...Array.from({ length: 12 }, (_, index) => ({
    salesOrderNo: 'SO-1000',
    deliveryOrderNo: `DO-OTHER-${index}`,
  })),
  { salesOrderNo, deliveryOrderNo: targetDelivery },
  { salesOrderNo, deliveryOrderNo: 'DO-OLD' },
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
      let data: unknown = { items: [], total: 0 }
      if (pathname.endsWith('/order-urgencies')) data = []
      if (pathname.endsWith('/receivables/by-source')) data = null
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
  const node = page.locator('.nv-ft-node').filter({ hasText: targetDelivery })
  const targetRead = page.waitForRequest((req) => {
    const url = new URL(req.url())
    return (
      url.pathname.endsWith('/delivery-orders') &&
      url.searchParams.get('keyword') === targetDelivery &&
      url.searchParams.get('organizationId') === 'org-sales' &&
      url.searchParams.get('environmentId') === 'env-sales'
    )
  })
  await node.getByRole('link').click()
  const keyword = page.getByRole('textbox', { name: '发货关键字' })
  await expect(keyword).toHaveValue(targetDelivery)
  await targetRead
  await expect(page.getByRole('row').filter({ hasText: targetDelivery })).toBeVisible()
  await page.goBack()
  await expect(page.getByRole('button', { name: '履约追踪' })).toBeVisible()
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
    await expect(node).toContainText('SKU-MOTOR × 12')
    await expect(node.getByRole('link')).toHaveAttribute(
      'href',
      '/planning?suggestionId=new-suggestion',
    )
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
  await expect(page.getByRole('button', { name: '履约追踪' })).toBeVisible()
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

test('只有生产需求时，需求节点进入普通工作台，建议节点保留空态', async ({ page }) => {
  await salesSession(page, ['business.erp.sales.read', 'business.planning.demands.read'])
  await page.route('**/v1/planning/demands?**', (route) =>
    route.fulfill({
      json: { success: true, data: { items: [{ sourceReference: salesOrderNo }], total: 1 } },
    }),
  )
  await openTimeline(page)
  const node = page.locator('.nv-ft-node')
  const suggestion = node.filter({ has: page.getByText('MRP 建议', { exact: true }) })
  await expect(suggestion).toContainText('尚未产生')
  await expect(suggestion.getByRole('link')).toHaveCount(0)
  await node
    .filter({ has: page.getByText('生产需求', { exact: true }) })
    .getByRole('link')
    .click()
  await expect(page).toHaveURL(/\/planning$/)
  await expect(page.getByRole('tab', { name: /需求池/ })).toHaveAttribute('aria-selected', 'true')
})
