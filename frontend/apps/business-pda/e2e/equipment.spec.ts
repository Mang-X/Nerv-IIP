import { expect, test } from '@playwright/test'
import { routeBusinessConsoleApi, routeConsoleApi, seedStoredSession } from './fixtures'

// 网关 Mock + 已登录主体（含 org/env + loginName，见 fixtures.principal）。
test.beforeEach(async ({ page }) => {
  await page.route('**/api/console/v1/**', routeConsoleApi)
  await page.route('**/api/business-console/v1/**', routeBusinessConsoleApi)
  await seedStoredSession(page)
})

test('报修：选设备 → 选优先级 → 填故障描述 → 提交 → 成功 Result', async ({ page }) => {
  await page.goto('/equipment/repair')
  await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()

  // 提供设备：扫描设备码（keyboard-wedge：type + Enter → @scan 写入 deviceAssetId）。
  // 走扫码而非直填 device-input，避免 ScanBar 的 active 抢焦把值吞进扫码框。
  const scan = page.locator('input[placeholder="扫描设备码"]')
  await scan.click()
  await scan.type('DEV-A')
  await scan.press('Enter')
  await expect(page.getByTestId('device-input')).toHaveValue('DEV-A')
  // 选优先级（中文选项「高」← high）
  await page.getByTestId('priority-select').selectOption('high')
  // 填故障描述
  await page.getByTestId('reason-input').fill('主轴异响，无法运转')

  await page.getByTestId('submit').click()

  // 成功离场态：Result success（POST work-orders → { workOrderId: 'WO-M-new' }）
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('报修已提交')).toBeVisible()
})

test('点检：选保养计划 → 选「通过」→ 提交 → 成功 Result', async ({ page }) => {
  await page.goto('/equipment/inspect')
  await expect(page.getByRole('heading', { name: '点检', exact: true })).toBeVisible()

  // 选择保养计划（PM-001 ← PLAN-1）
  await page.getByText('PM-001').click()
  // 选结果「通过」（pass → 通过）
  await page.getByTestId('result-pass').click()

  await page.getByTestId('submit').click()

  // 成功离场态（POST inspections → { inspectionId: 'INS-new' }）
  await expect(page.locator('[data-result][data-status="success"]')).toBeVisible()
  await expect(page.getByText('点检已记录')).toBeVisible()
})

test('报警 → 报修穿透：行详情「去报修」带 deviceAssetId + sourceAlarmId 跳报修页', async ({
  page,
}) => {
  await page.goto('/equipment/alarms')
  await expect(page.getByRole('heading', { name: '查看报警' })).toBeVisible()

  // 报警行渲染：设备 + 报警码 + 级别中文（严重，而非工程语言 'critical'）
  await expect(page.getByText('DEV-A · 报警码 E-101')).toBeVisible()
  await expect(page.getByText('严重')).toBeVisible()
  await expect(page.getByText('critical')).toHaveCount(0)

  // 去报修承载在行详情抽屉内（MAN-456 从行内移入详情）：先开详情再点。
  await page.getByTestId('detail-ALM-1').click()
  await page.getByTestId('repair-ALM-1').click()
  await expect(page).toHaveURL(/\/equipment\/repair\?/)
  const url = new URL(page.url())
  expect(url.pathname).toBe('/equipment/repair')
  expect(url.searchParams.get('deviceAssetId')).toBe('DEV-A')
  expect(url.searchParams.get('sourceAlarmId')).toBe('ALM-1')

  // 穿透后报修页设备已预填
  await expect(page.getByTestId('device-input')).toHaveValue('DEV-A')
})

test('首页 → 报修：点应用墙「报修」跳 /equipment/repair', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: '工作台' })).toBeVisible()

  await page.getByRole('button', { name: '报修' }).click()
  await expect(page).toHaveURL('/equipment/repair')
  await expect(page.getByRole('heading', { name: '故障报修' })).toBeVisible()
})
