/**
 * Dev-only 扫码枪注码器 —— 按真实扫码枪楔入时序向 `document` 派发 keydown 字符流。
 *
 * 与 e2e-live 的 `simulateScanGun()`（Playwright `page.keyboard`，trusted 事件）互补：
 * 本模块供人工浏览器走查在页面内点按注码，走的是**合成（untrusted）KeyboardEvent**。
 * 合成事件浏览器不会执行原生文本插入，因此值只能经由 ScanBar 的 document 级
 * capture 缓冲真实消费 —— 恰好覆盖 M1 落地的「焦点常驻 + 失焦兜底缓冲」产品路径。
 *
 * ScanBar 捕获契约（见 packages/ui-mobile/src/components/scan-bar/ScanBar.vue）：
 *  - 焦点在 ScanBar 自身 input / 其它可编辑元素上时，capture 路径让位（预期原生
 *    input 事件接管）。合成 keydown 产生不了原生 input 事件，所以派发前必须先把
 *    可编辑焦点 blur 掉（典型：ScanBar 焦点常驻的 input）。blur 会触发 ScanBar 的
 *    RAF 回焦，但本次派发与 blur 同步同任务，回焦最早在下一帧 —— 当前字符必然
 *    先被捕获；下一个字符到达时再做一次同样的检查即可。
 *  - 突发时序：同枪相邻字符间隔须 < SCAN_BURST_GAP_MS(100ms)，Enter 相对最后一个
 *    字符须 < SCAN_FRESHNESS_MS(300ms)，且总长 ≥ MIN_SCAN_CHARS(3)。默认 15ms
 *    间隔与 e2e-live 的枪型 profile 一致，满足全部约束。
 */

/** 同枪相邻字符的注码间隔。须远低于 ScanBar 的 100ms 突发间隔阈值。 */
export const SCAN_GUN_CHAR_INTERVAL_MS = 15

function isEditableElement(el: unknown): el is HTMLElement {
  if (!(el instanceof HTMLElement)) return false
  if (
    el instanceof HTMLInputElement ||
    el instanceof HTMLTextAreaElement ||
    el instanceof HTMLSelectElement
  ) {
    return true
  }
  return el.isContentEditable || el.hasAttribute('contenteditable')
}

/**
 * 派发一次扫码按键：先让位判定（blur 可编辑焦点），再向 `document` 派发
 * `keydown`。`bubbles`/`cancelable` 与真实按键一致 —— ScanBar 会对捕获的字符
 * `preventDefault()` 收进缓冲。
 */
function dispatchScanKey(key: string): void {
  const active = document.activeElement
  if (isEditableElement(active)) active.blur()
  document.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }))
}

const delay = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * 以扫码枪时序把 `code` 逐字符注入 document，并以 Enter 后缀收尾
 * （与 ScanBar「仅处理 Enter 后缀」的现契约一致）。
 */
export async function injectScanKeystrokes(
  code: string,
  options: { interCharDelayMs?: number } = {},
): Promise<void> {
  const interCharDelayMs = options.interCharDelayMs ?? SCAN_GUN_CHAR_INTERVAL_MS
  for (const char of code) {
    // ScanBar 只捕获 key.length === 1 的可打印键；条码值为 ASCII，逐 code point
    // 展开即逐字符。
    dispatchScanKey(char)
    await delay(interCharDelayMs)
  }
  dispatchScanKey('Enter')
}
