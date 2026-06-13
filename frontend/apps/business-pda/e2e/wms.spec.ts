import { expect, test } from '@playwright/test'
import { routeBusinessConsoleApi, routeConsoleApi, seedStoredSession } from './fixtures'

test.beforeEach(async ({ page }) => {
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
  await expect(page.getByText('入库已完成')).toBeVisible()
})

test('盘点: select CN-1, enter counted quantity, confirm, see success result', async ({ page }) => {
  await page.goto('/wms/count')

  await expect(page.getByRole('heading', { name: '盘点' })).toBeVisible()
  await expect(page.getByText('盘点 CN-1')).toBeVisible()

  await page.getByText('盘点 CN-1').click()

  // Enter 实盘数量 in the teleported sheet, then confirm.
  const counted = page.getByTestId('counted-quantity')
  await expect(counted).toBeVisible()
  await counted.fill('98')

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
  await expect(page.getByText('待执行')).toBeVisible()

  const body = await page.locator('body').innerText()
  expect(body).not.toContain('pending')
  expect(body).not.toContain('wt-pk-1')
})

test('home wall → 收货入库 navigates to /wms/inbound', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { name: '工作台' })).toBeVisible()
  const entry = page.getByRole('button', { name: '收货入库' })
  await expect(entry).toBeEnabled()
  await entry.click()

  await expect(page).toHaveURL('/wms/inbound')
})
