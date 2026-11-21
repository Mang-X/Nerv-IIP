import { test, fulfillJson, envelope } from './fixture'
import { expect } from '@playwright/test'

export function registerOrdersAndMaterials() {
  test('生产计划：就绪计划行尾「转工单」可点并打开下达工单弹窗', async ({ page }) => {
    // 计划数据来自共享 mock（PLAN-READY 可转 / PLAN-BLOCKED 受阻）。
    await page.goto('/mes/plans', { waitUntil: 'domcontentloaded' })
    await expect(
      page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '生产计划' }),
    ).toBeVisible({ timeout: 15_000 })
    // 确认计划数据已渲染。
    await expect(page.getByText('PLAN-READY')).toBeVisible({ timeout: 15_000 })

    // 就绪计划：行尾「转工单」按钮可见可点。
    const convertBtn = page.getByRole('button', { name: '转工单' })
    await expect(convertBtn).toBeVisible()
    await expect(convertBtn).toBeEnabled()

    // 点开「下达工单」弹窗（抓"点不了"）。exact 避免匹配到「确认下达工单」按钮。
    await convertBtn.click()
    await expect(page.getByText('下达工单', { exact: true })).toBeVisible()
  })

  // 标题渲染为面包屑当前页 <span data-slot="breadcrumb-page">；侧栏激活链接也带 aria-current="page"，
  // 故用 data-slot 精确定位。SPA 用 domcontentloaded 更稳（默认 load 可能因 HMR 长连接不触发）。
  test('工单与派工：工单队列渲染、创建急单弹窗可开', async ({ page }) => {
    // 工单数据来自共享 mock（WO-001）。
    await page.goto('/mes/work-orders', { waitUntil: 'domcontentloaded' })
    await expect(
      page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '工单与派工' }),
    ).toBeVisible({ timeout: 15_000 })
    await expect(page.getByText('WO-001')).toBeVisible({ timeout: 15_000 })

    // 创建急单 → 弹窗打开（抓"点不了"）。
    await page.getByRole('button', { name: '创建急单' }).click()
    const rushOrder = page.getByRole('dialog', { name: '创建急单' })
    await expect(rushOrder).toBeVisible()
    await expect(rushOrder.getByRole('button', { name: '生产版本', exact: true })).toBeVisible()
  })

  test('领料与齐套：领料申请渲染收料进度与「查看出库」闭环链接', async ({ page }) => {
    // 领料申请数据来自共享 mock（MIR-001，已收 4 / 应领 10，关联 WMS-OUT-001）。
    await page.goto('/mes/materials', { waitUntil: 'domcontentloaded' })
    await expect(
      page.locator('[data-slot="breadcrumb-page"]').filter({ hasText: '领料与齐套' }),
    ).toBeVisible({ timeout: 15_000 })
    await expect(page.getByText('MIR-001')).toBeVisible({ timeout: 15_000 })
    // 收料进度可读（已收 4）。
    await expect(page.getByText(/已收\s*4/)).toBeVisible()
    // 领料闭环：出库单「查看出库」可点、跳 WMS（不显 GUID）。
    await expect(page.getByRole('link', { name: '查看出库' })).toHaveAttribute(
      'href',
      /\/wms\/outbound/,
    )
    await expect(page.getByRole('heading', { name: '线边库存余额与账龄' })).toBeVisible()
    await expect(
      page.getByText('SKU-DAMPER-001', { exact: true }).filter({ visible: true }),
    ).toBeVisible()
    await expect(page.getByText(/在手 120 pcs/).filter({ visible: true })).toBeVisible()
    await expect(
      page.getByText(/4 天（部分批次缺少生产日期）/).filter({ visible: true }),
    ).toBeVisible()
    await expect(
      page.getByText('账龄未知（批次缺少生产日期）', { exact: true }).filter({ visible: true }),
    ).toBeVisible()
    const lineSideInventory = page.locator('section[aria-labelledby="line-side-inventory-title"]')
    await expect(lineSideInventory.locator('nav[aria-label="分页"]')).toBeVisible()
    await expect(lineSideInventory.locator('nav[aria-label="分页"]')).toHaveCount(1)
    if ((page.viewportSize()?.width ?? 0) >= 768) {
      await expect(lineSideInventory.locator('tbody tr')).toHaveCount(200)
      await expect(
        lineSideInventory.getByText('SKU-PAGE-200', { exact: true }).filter({ visible: true }),
      ).toBeVisible()
    }
    await expect(lineSideInventory).toContainText('第 1 / 2 页')
    await lineSideInventory.getByRole('button', { name: '下一页' }).click()
    await expect(
      lineSideInventory.getByText('SKU-PAGE-201', { exact: true }).filter({ visible: true }),
    ).toBeVisible()
    await expect(lineSideInventory).toContainText('第 2 / 2 页')
    await lineSideInventory.getByRole('button', { name: '上一页' }).click()
    await expect(
      lineSideInventory.getByText('SKU-DAMPER-001', { exact: true }).filter({ visible: true }),
    ).toBeVisible()
    await expect(lineSideInventory).toContainText('第 1 / 2 页')
    const mobileInventory = page.getByTestId('line-side-inventory-mobile')
    if ((page.viewportSize()?.width ?? 0) < 768) {
      await expect(mobileInventory).toBeVisible()
      await expect(mobileInventory).toContainText('4 天（部分批次缺少生产日期）')
    } else {
      await expect(mobileInventory).toBeHidden()
    }
  })

  test('领料与齐套：第 2 页失败后保留下方分页并可返回第 1 页', async ({ page }) => {
    await page.route(
      '**/api/business-console/v1/mes/line-side-inventory-balances*',
      async (route) => {
        const requestedPage = Number(new URL(route.request().url()).searchParams.get('page') ?? 1)
        if (requestedPage === 2) {
          return fulfillJson(route, {
            success: false,
            message: '第 2 页库存暂不可用',
            data: null,
          })
        }
        return fulfillJson(
          route,
          envelope({
            items: [
              {
                siteCode: 'SITE-SH',
                locationCode: 'LINE-A01',
                skuCode: 'SKU-RECOVERY-PAGE-1',
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
            totalCount: 201,
            page: 1,
            pageSize: 200,
            asOfDate: '2026-08-26',
          }),
        )
      },
    )

    await page.goto('/mes/materials', { waitUntil: 'domcontentloaded' })
    const lineSideInventory = page.locator('section[aria-labelledby="line-side-inventory-title"]')
    await expect(
      lineSideInventory.getByText('SKU-RECOVERY-PAGE-1', { exact: true }).filter({ visible: true }),
    ).toBeVisible({ timeout: 15_000 })
    await lineSideInventory.getByRole('button', { name: '下一页' }).click()

    await expect(lineSideInventory.getByRole('alert')).toContainText('第 2 页库存暂不可用')
    await expect(lineSideInventory.locator('nav[aria-label="分页"]')).toBeVisible()
    await expect(lineSideInventory.getByTestId('line-side-inventory-pagination')).toBeVisible()
    await expect(lineSideInventory.locator('[data-slot="table-container"]')).toHaveCount(0)
    await expect(lineSideInventory.getByRole('button', { name: '上一页' })).toBeEnabled()
    await lineSideInventory.getByRole('button', { name: '上一页' }).click()
    await expect(
      lineSideInventory.getByText('SKU-RECOVERY-PAGE-1', { exact: true }).filter({ visible: true }),
    ).toBeVisible()
    await expect(lineSideInventory).toContainText('第 1 / 2 页')
  })

  test('领料与齐套：总量收缩后自动回到最后有效页', async ({ page }) => {
    let pageOneRequests = 0
    await page.route(
      '**/api/business-console/v1/mes/line-side-inventory-balances*',
      async (route) => {
        const requestedPage = Number(new URL(route.request().url()).searchParams.get('page') ?? 1)
        if (requestedPage === 2) {
          return fulfillJson(
            route,
            envelope({ items: [], totalCount: 200, page: 2, pageSize: 200 }),
          )
        }
        pageOneRequests += 1
        return fulfillJson(
          route,
          envelope({
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
          }),
        )
      },
    )

    await page.goto('/mes/materials', { waitUntil: 'domcontentloaded' })
    const lineSideInventory = page.locator('section[aria-labelledby="line-side-inventory-title"]')
    await expect(
      lineSideInventory.getByText('SKU-CLAMPED-PAGE-1', { exact: true }).filter({ visible: true }),
    ).toBeVisible({ timeout: 15_000 })
    await lineSideInventory.getByRole('button', { name: '下一页' }).click()

    await expect.poll(() => pageOneRequests).toBe(2)
    await expect(
      lineSideInventory.getByText('SKU-CLAMPED-PAGE-1', { exact: true }).filter({ visible: true }),
    ).toBeVisible()
    await expect(lineSideInventory).toContainText('第 1 / 1 页')
    await expect(lineSideInventory).not.toContainText('第 2 / 1 页')
    await page.waitForTimeout(500)
    expect(pageOneRequests).toBe(2)
  })
}
