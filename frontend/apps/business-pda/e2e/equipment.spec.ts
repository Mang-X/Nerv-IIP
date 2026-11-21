import { test, expect } from '@playwright/test'
import { routeConsoleApi, routeBusinessConsoleApi, seedStoredSession } from './fixtures'
import { registerSelfLookup } from './equipment/selfLookup'
import { registerSelfReadback } from './equipment/selfReadback'
import { registerRepair } from './equipment/repair'
import { registerInspection } from './equipment/inspection'
import { registerAlarmToRepair } from './equipment/alarmToRepair'

// 网关 Mock + 已登录主体（含 org/env + loginName，见 fixtures.principal）。
test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await page.route('**/api/business-console/v2/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

registerSelfLookup()

registerSelfReadback()

registerRepair()

registerInspection()

registerAlarmToRepair()

test('首页 → 报修：点应用墙「报修」跳 /equipment/repair', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByTestId('home-name')).toBeVisible()

  await page.getByRole('button', { name: '报修' }).click()
  await expect(page).toHaveURL('/equipment/repair')
  await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()
})
