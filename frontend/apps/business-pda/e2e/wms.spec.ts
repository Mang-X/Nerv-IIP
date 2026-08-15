import { expect, test, type Page } from '@playwright/test'
import { routeBusinessConsoleApi, routeConsoleApi, seedStoredSession } from './fixtures'

async function expectTaskSheetKeyboardOverlay(
  page: Page,
  input: { route: '/wms/pick' | '/wms/putaway'; heading: '拣货' | '上架'; taskNo: string },
) {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.goto(input.route)

  expect(await page.viewportSize()).toEqual({ width: 375, height: 812 })
  await expect(page.getByRole('heading', { name: input.heading })).toBeVisible()

  const task = page.locator(`[data-task-no="${input.taskNo}"]`)
  await expect(task).toHaveCount(0)

  const filters = page.getByTestId('wms-task-filters')
  await filters.getByRole('button', { name: '待执行', exact: true }).click()
  await filters.getByRole('button', { name: '执行中', exact: true }).click()
  await expect(task).toBeVisible()
  await task.click()
  const sheet = page.locator('[data-slot="mobile-sheet-content"]')
  const quantity = page.getByTestId('executed-quantity')
  const keyboard = page.locator('[data-slot="number-keyboard"]')

  await expect(sheet).toBeVisible()
  await quantity.click()
  await expect(keyboard).toBeVisible()

  await keyboard.getByRole('button', { name: '7', exact: true }).click()
  await keyboard.getByRole('button', { name: '删除' }).click()
  await page
    .locator('[data-mobile-overlay-layer="input-backdrop"]')
    .click({ position: { x: 8, y: 8 } })
  await expect(keyboard).toHaveCount(0)
  await expect(sheet).toBeVisible()

  await quantity.click()
  await keyboard.getByRole('button', { name: '8', exact: true }).click()
  await keyboard.getByRole('button', { name: '完成' }).click()
  await expect(keyboard).toHaveCount(0)
  await expect(sheet).toBeVisible()

  await quantity.click()
  await expect(keyboard).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(sheet).toHaveCount(0)
  await expect(keyboard).toHaveCount(0)
}

test.beforeEach(async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  // Seed the stored session (principal carries org/env scope) so guarded WMS routes
  // load straight away without driving the login form.
  await seedStoredSession(page)
})

test('收货入库: select order IN-1, confirm in sheet, see success result', async ({ page }) => {
  await page.goto('/wms/inbound')

  await expect(page.getByRole('heading', { name: '收货入库' })).toBeVisible()
  // Order row renders the business order number (no raw status / GUID).
  await expect(page.getByText('IN-1', { exact: true })).toBeVisible()

  await page.getByText('IN-1', { exact: true }).click()

  // BottomSheet (teleported) confirm action.
  const confirm = page.getByTestId('confirm-complete')
  await expect(confirm).toBeVisible()
  await confirm.click()

  // Success Result.
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('入库完成，待质检')).toBeVisible()
})

test('盘点: select CN-1, enter counted quantity, confirm, see success result', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 })
  await page.goto('/wms/count')

  expect(await page.viewportSize()).toEqual({ width: 375, height: 812 })

  await expect(page.getByRole('heading', { name: '盘点' })).toBeVisible()
  await expect(page.getByText('盘点 CN-1')).toBeVisible()

  await page.getByText('盘点 CN-1').click()

  // Enter 实盘数量 through the mobile numeric keyboard, then confirm.
  const counted = page.getByTestId('counted-quantity')
  await expect(counted).toBeVisible()
  await counted.click()
  const keyboard = page.locator('[data-slot="number-keyboard"]')
  await expect(keyboard).toBeVisible()
  await expect
    .poll(() => keyboard.evaluate((element) => getComputedStyle(element).transform))
    .toBe('none')
  const sheet = page.locator('[data-slot="mobile-sheet-content"]')
  const digitNine = keyboard.getByRole('button', { name: '9', exact: true })
  const hitTarget = await digitNine.evaluate((button) => {
    const rect = button.getBoundingClientRect()
    const target = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2)
    return {
      isButton: target === button || button.contains(target),
      target: target instanceof HTMLElement ? target.outerHTML.slice(0, 200) : null,
      buttonPointerEvents: getComputedStyle(button).pointerEvents,
      keyboardPointerEvents: getComputedStyle(button.closest('[data-slot="number-keyboard"]')!)
        .pointerEvents,
      keyboardZ: Number(getComputedStyle(button.closest('[data-slot="number-keyboard"]')!).zIndex),
      sheetZ: Number(
        getComputedStyle(document.querySelector('[data-slot="mobile-sheet-content"]')!).zIndex,
      ),
    }
  })
  expect(hitTarget.isButton, JSON.stringify(hitTarget)).toBe(true)
  expect(hitTarget.keyboardZ).toBeGreaterThan(hitTarget.sheetZ)

  for (const digit of ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0']) {
    await keyboard.getByRole('button', { name: digit, exact: true }).click()
  }
  await keyboard.getByRole('button', { name: '删除' }).click()

  // The priority backdrop closes only the keyboard; Dialog outside handling keeps the sheet open.
  await page
    .locator('[data-mobile-overlay-layer="input-backdrop"]')
    .click({ position: { x: 8, y: 8 } })
  await expect(keyboard).toHaveCount(0)
  await expect(sheet).toBeVisible()
  await expect(counted).toContainText('123456789')

  // The confirm key is a second keyboard-only close path.
  await counted.click()
  await keyboard.getByRole('button', { name: '完成' }).click()
  await expect(keyboard).toHaveCount(0)
  await expect(sheet).toBeVisible()

  // Closing the business sheet while the keyboard is open synchronously closes both overlays.
  await counted.click()
  await expect(keyboard).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(sheet).toHaveCount(0)
  await expect(keyboard).toHaveCount(0)

  // Re-open the task and finish the original business flow.
  await page.getByText('盘点 CN-1').click()
  await counted.click()
  await keyboard.getByRole('button', { name: '9', exact: true }).click()
  await keyboard.getByRole('button', { name: '8', exact: true }).click()
  await keyboard.getByRole('button', { name: '完成' }).click()

  const confirm = page.getByTestId('confirm-complete')
  await expect(confirm).toBeEnabled()
  await confirm.click()

  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('盘点已提交')).toBeVisible()
})

test('拣货 read-only: task PK-1 shows Chinese status (no raw code / GUID)', async ({ page }) => {
  await page.goto('/wms/pick')

  await expect(page.getByRole('heading', { name: '拣货' })).toBeVisible()
  // Task number + Chinese status surface; raw engineering code never does.
  await expect(page.getByText('任务 PK-1')).toBeVisible()
  await expect(page.getByText('待执行', { exact: true }).first()).toBeVisible()

  const body = await page.locator('body').innerText()
  expect(body).not.toContain('pending')
  expect(body).not.toContain('wt-pk-1')
})

test('拣货: 375x812 task sheet keeps open for real number-keyboard clicks', async ({ page }) => {
  await expectTaskSheetKeyboardOverlay(page, {
    route: '/wms/pick',
    heading: '拣货',
    taskNo: 'PK-EXEC-1',
  })
})

test('上架: 375x812 task sheet keeps open for real number-keyboard clicks', async ({ page }) => {
  await expectTaskSheetKeyboardOverlay(page, {
    route: '/wms/putaway',
    heading: '上架',
    taskNo: 'PA-EXEC-1',
  })
})

test('home wall → 收货入库 navigates to /wms/inbound', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByTestId('home-name')).toBeVisible()
  const entry = page.getByRole('button', { name: '收货入库' })
  await expect(entry).toBeEnabled()
  await entry.click()

  await expect(page).toHaveURL('/wms/inbound')
})
