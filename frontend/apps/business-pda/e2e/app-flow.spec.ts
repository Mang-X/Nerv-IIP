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
