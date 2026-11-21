import { test, expect } from '@playwright/test'
import { expectNoHorizontalOverflow } from '../fixtures'

export function registerInspection() {
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
    // 戴手套触点 ≥44px：数字键（键盘开着时量）+ 提交动作「完成」键（删除头部小按钮后仅剩
    // 底部大键，此前 E2E 只量数字键与 Cell、漏了提交动作，本处补上）。
    const digitBox = await keyboard.getByRole('button', { name: '8', exact: true }).boundingBox()
    expect(digitBox!.height).toBeGreaterThanOrEqual(44)
    expect(digitBox!.width).toBeGreaterThanOrEqual(44)
    const doneButtons = await keyboard.getByRole('button', { name: '完成' }).all()
    expect(doneButtons).toHaveLength(1)
    const doneBox = await doneButtons[0].boundingBox()
    expect(doneBox!.height).toBeGreaterThanOrEqual(44)
    expect(doneBox!.width).toBeGreaterThanOrEqual(44)
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
}
