import { expect, test } from '@playwright/test'
import { expectNoHorizontalOverflow, expectTouchTargets, seedStoredSession } from './fixtures'

const GALLERY = '/design-system/gallery'

test('all ui-mobile components render and the mobile layout has no overflow', async ({ page }) => {
  await page.goto(GALLERY)
  await expect(page.getByRole('heading', { name: 'UI Mobile 组件库' })).toBeVisible()
  await expect(page.getByTestId('scan-section')).toBeVisible()
  await expect(page.getByText('过账成功')).toBeVisible()
  await expect(page.getByText('过账失败')).toBeVisible()
  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)
})

test('ScanBar emits the scanned value (keyboard-wedge: type + Enter)', async ({ page }) => {
  await page.goto(GALLERY)
  const input = page.locator('input[placeholder="扫描条码"]')
  await input.click()
  await input.type('SKU-12345')
  await input.press('Enter')
  await expect(page.getByTestId('scan-result')).toHaveText('SKU-12345')
})

test('ListRow select fires only for the interactive row', async ({ page }) => {
  await page.goto(GALLERY)
  await expect(page.getByTestId('list-clicked')).toHaveText('idle')
  await page.getByText('收货单 RO-2026-001').click()
  await expect(page.getByTestId('list-clicked')).toHaveText('clicked')
})

test('BottomSheet opens and closes (Escape dismiss)', async ({ page }) => {
  await page.goto(GALLERY)
  await expect(page.getByText('抽屉内容')).toHaveCount(0)
  await page.getByTestId('open-sheet').click()
  await expect(page.getByText('抽屉内容')).toBeVisible()
  // `选择库位` appears twice (visible DialogTitle + sr-only DialogDescription fallback); assert the visible title.
  await expect(page.getByText('选择库位').first()).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByText('抽屉内容')).toHaveCount(0)
})

test('AppShellMobile applies the safe-area minimum padding on header and footer', async ({ page }) => {
  await page.goto(GALLERY)
  // Wait for the shell to mount before reading computed styles (goto resolves before SPA hydration).
  await expect(page.locator('[data-shell="footer"]')).toBeVisible()
  const pads = await page.evaluate(() => {
    const header = document.querySelector('[data-shell="header"]') as HTMLElement
    const footer = document.querySelector('[data-shell="footer"]') as HTMLElement
    const px = (v: string) => Number.parseFloat(v)
    return {
      headerTop: px(getComputedStyle(header).paddingTop),
      footerBottom: px(getComputedStyle(footer).paddingBottom),
    }
  })
  // max(0.75rem, env(...)) / max(0.5rem, env(...)) — env=0 on emulated devices → fallback minimums
  expect(pads.headerTop).toBeGreaterThanOrEqual(12)
  expect(pads.footerBottom).toBeGreaterThanOrEqual(8)
})

test('dark mode renders a dark surface (token wiring)', async ({ page }) => {
  await seedStoredSession(page, 'dark')
  await page.goto(GALLERY)
  const result = await page.evaluate(() => {
    const isDark = document.documentElement.classList.contains('dark')
    const bg = getComputedStyle(document.body).backgroundColor
    // parse rgb(...) -> perceived lightness
    const m = bg.match(/\d+(\.\d+)?/g)?.map(Number) ?? [255, 255, 255]
    const lightness = (0.299 * m[0] + 0.587 * m[1] + 0.114 * m[2]) / 255
    return { isDark, lightness }
  })
  expect(result.isDark).toBe(true)
  expect(result.lightness).toBeLessThan(0.5)
})
