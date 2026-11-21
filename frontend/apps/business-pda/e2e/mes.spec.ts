import { test, expect } from '@playwright/test'
import { routeConsoleApi, routeBusinessConsoleApi, seedStoredSession } from './fixtures'
import { registerOperationList } from './mes/operationList'
import { registerOperationScope } from './mes/operationScope'
import { registerReporting } from './mes/reporting'
import { registerMaterialsAndReceipts } from './mes/materialsAndReceipts'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

registerOperationList()

registerOperationScope()

registerReporting()

registerMaterialsAndReceipts()

test('首页 → 工序执行：点击应用墙入口跳转到 /mes/operation', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByTestId('home-name')).toBeVisible()
  await page.getByRole('button', { name: '工序执行' }).click()

  await expect(page).toHaveURL('/mes/operation')
  await expect(page.getByRole('heading', { name: '工序执行' })).toBeVisible()
})
