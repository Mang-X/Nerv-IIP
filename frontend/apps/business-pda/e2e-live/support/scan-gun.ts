import type { Page } from '@playwright/test'

/**
 * 扫码枪信号 profile。
 *
 * - `interCharDelayMs`：字符间隔（默认 15ms），模拟键盘楔入的突发字符流（真实枪约 10–30ms）。
 * - `suffix`：仅支持 `'Enter'`——与 ScanBar 现契约一致（`@keydown.enter` 提交）；
 *   Tab/控制字符后缀属契约扩展，另立 issue，本 helper 不预支。
 * - `prefix`：部分扫码枪配置的前导字符（如 STX 替代符），默认无。
 */
export interface ScanGunProfile {
  interCharDelayMs?: number
  suffix?: 'Enter'
  prefix?: string
}

/**
 * 模拟扫码枪键盘楔入向当前页面注入一段条码。
 *
 * 保真定位声明（方案文档 §2 保真等级）：这是 **DOM 层键盘楔入近似**——桌面 Chromium
 * 合成键盘事件，不经过 Android IME/焦点系统，更不等价于 USB HID scan code、
 * KeyCharacterMap 或厂商扫码服务（如 Zebra DataWedge）。逐级逼近见 L3（adb input）与 L4（实体枪）。
 *
 * 契约要点：**不 `focus()`、不 `fill()`**——字符流必须经由 ScanBar「焦点常驻」的前提
 * 才能进入输入框（`onMounted` 聚焦 + `@blur` 后 RAF 回抢）。若页面焦点不在 ScanBar，
 * 注入的字符会像真实扫码枪一样丢失或落入其他控件，这正是要检测的行为。
 */
export async function simulateScanGun(
  page: Page,
  code: string,
  profile: ScanGunProfile = {},
): Promise<void> {
  const { interCharDelayMs = 15, suffix = 'Enter', prefix = '' } = profile
  await page.keyboard.type(`${prefix}${code}`, { delay: interCharDelayMs })
  if (suffix === 'Enter') {
    await page.keyboard.press('Enter')
  }
}
