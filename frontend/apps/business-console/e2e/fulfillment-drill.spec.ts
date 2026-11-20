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
