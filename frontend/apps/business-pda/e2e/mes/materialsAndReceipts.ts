import { test, expect } from '@playwright/test'

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
