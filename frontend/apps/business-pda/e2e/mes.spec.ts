import { expect, test } from '@playwright/test'
import { routeBusinessConsoleApi, routeConsoleApi, seedStoredSession } from './fixtures'

test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

test('工序执行：列表 → 完成（二次确认）→ 成功结果', async ({ page }) => {
  await page.goto('/mes/operation')

  await expect(page.getByRole('heading', { name: '工序执行' })).toBeVisible()

  // Running 工序任务行（OP-1）渲染：title=工单·工序，subtitle=状态·工作中心。
  const row = page.getByText('WO-1 · 工序 10')
  await expect(row).toBeVisible()

  // 点行打开 BottomSheet 动作面板（teleport 到 body）。
  await row.click()
  // Running → 可用动作含「完成」（终态、destructive）。
  const completeBtn = page.getByTestId('action-complete')
  await expect(completeBtn).toBeVisible()

  // 第一次点「完成」进入二次确认，并未直接调用。
  await completeBtn.click()
  const confirmBtn = page.getByTestId('confirm-complete')
  await expect(confirmBtn).toBeVisible()

  // 确认完成 → 成功 Result。
  await confirmBtn.click()
  const result = page.locator('[data-result][data-status="success"]')
  await expect(result).toBeVisible()
  await expect(result.getByText('工序已完成')).toBeVisible()
})

test('报工：选工单 → 选工序 → 录良品数 → 提交 → 成功结果', async ({ page }) => {
  await page.goto('/mes/report')

  await expect(page.getByRole('heading', { name: '报工' })).toBeVisible()

  // 步骤 1：选工单（WO-1）。
  const workOrderRow = page.getByText('WO-1', { exact: true })
  await expect(workOrderRow).toBeVisible()
  await workOrderRow.click()

  // 步骤 2：选工序（按工单过滤后仍返回 mock 工序）。
  await expect(page.getByText('当前工单')).toBeVisible()
  const taskRow = page.getByText('WO-1 · 工序 10')
  await expect(taskRow).toBeVisible()
  await taskRow.click()

  // 步骤 3：录数量（BottomSheet）→ 良品数。
  const goodQty = page.getByTestId('good-quantity')
  await expect(goodQty).toBeVisible()
  await goodQty.fill('5')

  // 提交报工 → 成功 Result。
  await page.getByTestId('submit-report').click()
  const result = page.locator('[data-result][data-status="success"]')
  await expect(result).toBeVisible()
  await expect(result.getByText('报工成功')).toBeVisible()
})

test('首页 → 工序执行：点击应用墙入口跳转到 /mes/operation', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('heading', { name: '工作台' })).toBeVisible()
  await page.getByRole('button', { name: '工序执行' }).click()

  await expect(page).toHaveURL('/mes/operation')
  await expect(page.getByRole('heading', { name: '工序执行' })).toBeVisible()
})
