import { test, expect } from '@playwright/test'
import { principal, session } from '../fixtures/auth'
import { envelope } from '../fixtures/http'

export function registerMaterialsAndReceipts() {
  test('领料：列表渲染领料申请行（不退化为空态）', async ({ page }) => {
    await page.goto('/mes/issue')

    await expect(page.getByRole('heading', { name: '领料' })).toBeVisible()

    // 领料申请行：title = 工单 · 物料，不外显原始 requestId。
    await expect(page.getByText('WO-1 · 物料 MAT-1')).toBeVisible()
    await expect(page.getByText('WO-1 · 物料 MAT-2')).toBeVisible()
    await expect(page.getByText('暂无领料申请')).toHaveCount(0)
    await expect(page.getByRole('heading', { name: '线边库存' })).toBeVisible()
    await expect(page.getByText('SKU-DAMPER-001', { exact: true })).toBeVisible()
    await expect(page.getByText(/可用 100 pcs/)).toBeVisible()
    await expect(page.getByText(/6 天 · 账龄完整/)).toBeVisible()
    await expect(page.getByText(/账龄未知（批次缺少生产日期）/)).toBeVisible()
    const lineSideInventory = page.locator(
      'section[aria-labelledby="pda-line-side-inventory-title"]',
    )
    await expect(lineSideInventory).toContainText('第 1 / 2 页')
    await lineSideInventory.getByRole('button', { name: '下一页' }).click()
    await expect(lineSideInventory.getByText('SKU-PAGE-201', { exact: true })).toBeVisible()
    await expect(lineSideInventory).toContainText('第 2 / 2 页')
    await lineSideInventory.getByRole('button', { name: '上一页' }).click()
    await expect(lineSideInventory.getByText('SKU-DAMPER-001', { exact: true })).toBeVisible()
    await expect(lineSideInventory).toContainText('第 1 / 2 页')
  })

  test('领料只读角色可查看申请且没有线边接收入口', async ({ page }) => {
    await page.goto('/mes/issue')
    await expect(page.getByText('WO-1 · 物料 MAT-1')).toBeVisible()
    await expect(page.getByTestId('receive-ISS-1')).toHaveCount(0)
  })

  test('线边部分收料：响应丢失后恢复原回执和收料数量，再发起新的收料', async ({ page }) => {
    const operator = {
      ...principal,
      permissionCodes: [...principal.permissionCodes, 'business.mes.materials.manage'],
    }
    await page.route('**/api/console/v1/auth/me', (route) =>
      route.fulfill({
        json: envelope(operator),
      }),
    )
    await page.route('**/api/console/v1/auth/refresh', (route) =>
      route.fulfill({
        json: envelope({ ...session, principal: operator }),
      }),
    )

    const base = '**/api/business-console/v1/mes/material-issue-requests'
    const bodies: { idempotencyKey?: string; receivedQuantity: number }[] = []
    let receivedQuantity = 0
    let status = 'Requested'
    let readBackOriginal = false
    await page.route(`${base}*`, (route) => {
      if (route.request().method() !== 'GET') return route.fallback()
      const query = new URL(route.request().url()).searchParams
      if (bodies.length === 2 && query.get('workOrderId') === 'WO-1') readBackOriginal = true
      return route.fulfill({
        json: envelope({
          items: [
            {
              requestId: 'ISS-1',
              workOrderId: 'WO-1',
              materialId: 'MAT-1',
              requestedQuantity: 10,
              receivedQuantity,
              status,
            },
          ],
          total: 1,
        }),
      })
    })
    await page.route(`${base}/ISS-1/line-side-receipts*`, async (route) => {
      const body = route.request().postDataJSON() as (typeof bodies)[number]
      bodies.push(body)
      const receipt = { accepted: true, downstreamDocumentId: 'MI-001' }
      if (bodies.length === 1) {
        receivedQuantity = 4
        status = 'ReceiptPosting'
        return route.abort('connectionreset')
      }
      if (bodies.length === 2) {
        if (!body.idempotencyKey || body.idempotencyKey !== bodies[0].idempotencyKey) {
          return route.fulfill({ status: 409, json: { success: false } })
        }
        status = 'PartiallyReceived'
      } else {
        receivedQuantity += body.receivedQuantity
      }
      return route.fulfill({ json: envelope(receipt) })
    })

    await page.goto('/mes/issue')
    async function receive(quantity: string) {
      await page.getByTestId('receive-ISS-1').click()
      await page.getByTestId('received-quantity').fill(quantity)
      await page.getByTestId('submit-receive').click()
    }

    await receive('4')
    await page.getByTestId('retry-issue').click()
    await expect(page.getByText(/MI-001.*已收 4/)).toBeVisible()
    expect(readBackOriginal).toBe(true)
    expect(bodies[0]).toEqual({ idempotencyKey: expect.any(String), receivedQuantity: 4 })
    expect(bodies[1]).toEqual(bodies[0])

    await page.getByRole('button', { name: '继续', exact: true }).click()
    await receive('2')
    await expect(page.getByText(/MI-001.*已收 6/)).toBeVisible()
    expect(bodies).toHaveLength(3)
    expect(bodies[2]).toEqual({ idempotencyKey: expect.any(String), receivedQuantity: 2 })
    expect(bodies[2].idempotencyKey).not.toBe(bodies[0].idempotencyKey)
  })

  test('领料：总量收缩后自动回到最后有效页', async ({ page }) => {
    let pageOneRequests = 0
    await page.route(
      '**/api/business-console/v1/mes/line-side-inventory-balances*',
      async (route) => {
        const requestedPage = Number(new URL(route.request().url()).searchParams.get('page') ?? 1)
        if (requestedPage === 2) {
          return route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
              success: true,
              data: { items: [], totalCount: 200, page: 2, pageSize: 200 },
            }),
          })
        }
        pageOneRequests += 1
        return route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            success: true,
            data: {
              items: [
                {
                  siteCode: 'SITE-SH',
                  locationCode: 'LINE-A01',
                  skuCode: 'SKU-CLAMPED-PAGE-1',
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
              totalCount: pageOneRequests === 1 ? 201 : 200,
              page: 1,
              pageSize: 200,
            },
          }),
        })
      },
    )

    await page.goto('/mes/issue')
    const lineSideInventory = page.locator(
      'section[aria-labelledby="pda-line-side-inventory-title"]',
    )
    await expect(lineSideInventory.getByText('SKU-CLAMPED-PAGE-1', { exact: true })).toBeVisible()
    await lineSideInventory.getByRole('button', { name: '下一页' }).click()

    await expect.poll(() => pageOneRequests).toBe(2)
    await expect(lineSideInventory.getByText('SKU-CLAMPED-PAGE-1', { exact: true })).toBeVisible()
    await expect(lineSideInventory).toContainText('第 1 / 1 页')
    await expect(lineSideInventory).not.toContainText('第 2 / 1 页')
    await page.waitForTimeout(500)
    expect(pageOneRequests).toBe(2)
  })

  test('完工入库：列表渲染入库申请行（不退化为空态）', async ({ page }) => {
    await page.goto('/mes/receipt')

    await expect(page.getByRole('heading', { name: '完工入库' })).toBeVisible()

    // 入库申请行：title = 工单 · 物料(SKU)，不外显原始 receiptRequestId。
    await expect(page.getByText('WO-1 · 物料 SKU-1')).toBeVisible()
    await expect(page.getByText('WO-1 · 物料 SKU-2')).toBeVisible()
    await expect(page.getByText('暂无完工入库申请')).toHaveCount(0)
  })
}
