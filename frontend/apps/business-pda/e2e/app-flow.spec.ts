import { expect, test } from '@playwright/test'
import {
  expectNoHorizontalOverflow,
  expectTouchTargets,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
} from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
})

test('logs in and lands on the workbench home', async ({ page }) => {
  await page.goto('/login')
  await page.getByLabel('账号').fill('operator01')
  await page.getByLabel('密码').fill('Operator123!')
  await page.getByRole('button', { name: '登录' }).click()

  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: '工作台' })).toBeVisible()
})

test('failed login shows an error and stays on the login route', async ({ page }) => {
  // Override just the login endpoint with a 401; later-registered routes win in Playwright.
  await page.route('**/api/console/v1/auth/login', (route) =>
    route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ success: false, message: null, data: null }),
    }),
  )

  await page.goto('/login')
  await page.getByLabel('账号').fill('operator01')
  await page.getByLabel('密码').fill('wrong-password')
  await page.getByRole('button', { name: '登录' }).click()

  // Error surfaces, the router did NOT navigate away, and the login form is still shown.
  await expect(page.getByText('账号密码错误或会话已过期。')).toBeVisible()
  await expect(page).toHaveURL('/login')
  await expect(page.getByRole('button', { name: '登录' })).toBeVisible()
})

test('home shows scan bar, my-tasks empty state and a disabled app wall', async ({ page }) => {
  await seedStoredSession(page)
  await page.goto('/')

  await expect(page.getByRole('heading', { name: '工作台' })).toBeVisible()
  // scan bar focus input present
  await expect(page.locator('input[placeholder^="扫描"]')).toBeVisible()
  // my-tasks empty state (no fake data)
  await expect(page.getByText('暂无分配给你的任务')).toBeVisible()
  // app wall labels render and are disabled until M2 pages land
  await expect(page.getByRole('button', { name: '收货入库' })).toBeDisabled()
  await expect(page.getByRole('button', { name: '报工' })).toBeDisabled()

  await expectNoHorizontalOverflow(page)
  await expectTouchTargets(page)
})

test('clicking a not-ready app-wall entry does not navigate away', async ({ page }) => {
  await seedStoredSession(page)
  await page.goto('/')
  await page.getByRole('button', { name: '收货入库' }).click({ force: true })
  await expect(page).toHaveURL('/')
})

test('home scan: type + Enter echoes in-page and keeps the operator on the workbench', async ({
  page,
}) => {
  // R3 fix: scanning must NOT navigate to the not-yet-existent /scan route; it echoes
  // the value in-page (`[data-testid="last-scan"]` → `已扫码：{value}`) so the operator
  // stays on the workbench instead of being dropped on a dead route.
  await seedStoredSession(page)
  await page.goto('/')

  const scanInput = page.locator('input[placeholder^="扫描"]')
  await scanInput.focus()
  await scanInput.type('SKU-12345')
  await scanInput.press('Enter')

  // Still on the workbench — no fake jump to /scan or any dead route.
  await expect(page).toHaveURL('/')
  // The in-page echo proves the scan was handled honestly.
  await expect(page.getByTestId('last-scan')).toContainText('已扫码：SKU-12345')
})
