/**
 * 空值占位统一约定。
 *
 * 走查反复出现同一类失格：字段有标签、值却是一片空白（扫码记录的状态列、
 * 版本解析卡片的部分字段…）。读者无法区分「没这个值」和「界面坏了」。
 *
 * 约定：**任何有标签的字段，值为空时一律渲染 `—`（em dash），不留空白。**
 * 在此之前包内只有 NvDescriptions 一个组件带了 `'—'` 字面量，app 侧则手写了
 * 60+ 次 `?? '—'`，且 NvStatusBadge 用的是第三种写法 `'未知'`。
 *
 * 例外（有意保留、不要改成 `—`）：
 * - **状态语义**：状态类字段空值走 `NvStatusBadge` 的 `'未知'`，因为「状态未知」
 *   是一个有业务含义的状态，不是「无此字段」。
 * - **纯装饰性副标题**：没有标签、纯补充说明的文本，空了就整块 `v-if` 掉，
 *   不要留一个孤零零的 `—`。
 */

/** 空值占位符：em dash。不要用 `-`（hyphen）或 `--`。 */
export const EMPTY_TEXT = '—'

/**
 * 判断一个字段值是否应当显示为空值占位。
 *
 * 视为空：`undefined`、`null`、空字符串、只含空白的字符串。
 * **不**视为空：`0`、`false`、`NaN` —— 这些是真实业务值（0 件库存、否、无效读数），
 * 用 `!value` 判空会把它们错误地吞掉，这是走查里另一类常见 bug。
 */
export function isEmptyValue(value: unknown): boolean {
  if (value === undefined || value === null) return true
  if (typeof value === 'string') return value.trim() === ''
  return false
}

/**
 * 取字段的展示文本：空值回落到占位符。
 *
 * @param value 原始值
 * @param fallback 自定义占位文本，默认 `—`
 */
export function displayValue(value: unknown, fallback: string = EMPTY_TEXT): string {
  return isEmptyValue(value) ? fallback : String(value)
}
