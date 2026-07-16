import { expect, test } from '@playwright/test'
import {
  expectNoHorizontalOverflow,
  routeBusinessConsoleApi,
  routeConsoleApi,
  seedStoredSession,
} from './fixtures'

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

// MAN-458 #812：数字键盘录入（含 ± 负号）+ 超差即时警示 + 提交前汇总确认。真实 Chromium /
// Pixel 5 视口验证 jsdom 测不到的：Teleport 键盘浮层、计算样式红警示、触点尺寸、ScanBar 抢焦。
test('点检：数字键盘录入（含负号）+ 超差警示 + 提交确认', async ({ page }) => {
  // 键盘/弹窗过渡置 none：组件 @media(prefers-reduced-motion) 走 transition:none，
  // Teleport + Transition 的离场即时移除，消除 headless 下 transitionend 滞留（测行为非动画）。
  await page.emulateMedia({ reducedMotion: 'reduce' })

  await page.goto('/equipment/inspect')
  await page.getByText('PM-001').click()
  await page.getByTestId('result-pass').click()

  // 特性 + 单位：**真实 tap + fill**。文本获焦时页面停用 ScanBar 回焦（focusin），故正常录入
  // 不再被 ScanBar 抢走（#812 戴手套可完成录入的核心验收，不再靠原生 setter 假绿）。
  const characteristic = page.getByTestId('measurement-characteristic')
  await characteristic.click()
  await characteristic.fill('轴承温度')
  await expect(characteristic).toHaveValue('轴承温度')
  const uom = page.getByTestId('measurement-uom')
  await uom.click()
  await uom.fill('C')
  await expect(uom).toHaveValue('C')

  // 数字键盘录入（只读 Cell 触发，防系统键盘）：下限 0 / 上限 70 / 测量值 -80（± 负号 →
  // 低于下限超差）。键盘是底部 sheet + fixed inset-0 背板：字段间须「完成」收起再点下一格。
  const keyboard = page.locator('[data-slot="number-keyboard"]')
  const enterViaKeyboard = async (cell: string, digits: string) => {
    await page.getByTestId(cell).click()
    await expect(keyboard).toBeVisible()
    for (const d of digits) {
      await keyboard.getByRole('button', { name: d, exact: true }).click()
    }
  }
  const closeKeyboard = async () => {
    await keyboard.getByRole('button', { name: '完成' }).last().click()
    await expect(keyboard).toBeHidden()
  }

  await enterViaKeyboard('measurement-lower', '0')
  // 戴手套触点 ≥44px：数字键盘大键（键盘开着时量）。
  const digitBox = await keyboard.getByRole('button', { name: '8', exact: true }).boundingBox()
  expect(digitBox!.height).toBeGreaterThanOrEqual(44)
  expect(digitBox!.width).toBeGreaterThanOrEqual(44)
  await closeKeyboard()

  await enterViaKeyboard('measurement-upper', '70')
  await closeKeyboard()

  // 测量值：± → 8 → 0 = -80（负号回归覆盖；-80 < 下限 0 → 超差）。
  await page.getByTestId('measurement-value').click()
  await expect(keyboard).toBeVisible()
  await keyboard.getByRole('button', { name: '正负号' }).click()
  await keyboard.getByRole('button', { name: '8', exact: true }).click()
  await keyboard.getByRole('button', { name: '0', exact: true }).click()
  await closeKeyboard()
  await expect(page.getByTestId('measurement-value-text')).toHaveText('-80')

  // 测量值 Cell 触点 ≥44px。
  const cellBox = await page.getByTestId('measurement-value').boundingBox()
  expect(cellBox!.height).toBeGreaterThanOrEqual(44)

  // 超差即时警示：红标 + 数值变红 + 规格公差呈现；移动视口无横向溢出。
  await expect(page.getByTestId('out-of-tolerance')).toBeVisible()
  await expect(page.getByTestId('measurement-value-text')).toHaveClass(/text-destructive/)
  await expect(page.getByTestId('spec-range')).toHaveText('0 ~ 70 C')
  await expectNoHorizontalOverflow(page)

  // 提交 → 超差汇总确认「1 项测量值超差」→ 仍要提交 → 成功离场。
  await page.getByTestId('submit').click()
  const dialog = page.locator('[data-slot="mobile-dialog-content"]')
  await expect(dialog).toContainText('1 项测量值超差')
  await dialog.getByRole('button', { name: '仍要提交' }).click()
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
