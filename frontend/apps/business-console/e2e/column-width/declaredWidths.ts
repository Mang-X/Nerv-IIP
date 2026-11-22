import { test, expect } from '@playwright/test'
import { assertDeclaredWidthsHold, statusColumnIndex } from './fixture'

export function registerDeclaredWidths() {
  test('NvDataTable：声明宽 == 渲染宽，Tailwind 类与 CSS 尺寸两种写法（#3734）', async ({
    page,
  }) => {
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
}
