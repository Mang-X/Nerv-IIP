/**
 * 作业范围选择键（`kind:id`）的编解码。
 *
 * 「授权作业范围」在 MES 与 WMS 两侧是同一个领域概念（self / work-pool / site / work-center…），
 * 选择器 option 的 `value` 必须是单个字符串，因此把 `{ kind, id }` 压成 `kind:id`，
 * 用于下拉选中值、localStorage 记忆值与共享选择表的键。
 *
 * 收敛理由：business-console 的 MES 侧、business-console 的 WMS 侧、business-pda 的 WMS 侧
 * 原本各有一份逐字相同的实现。三份分头演进一旦分叉（比如某侧改成 `split(':')`），
 * 就会出现「同一个记忆值在 A 页认得、B 页认不得」，用户的范围选择被静默丢弃。
 */

/** 解析出的作业范围。`kind` 是范围类别，`id` 是该类别下的具体标识。 */
export interface WorkScopeKeyParts {
  kind: string
  id: string
}

/** 把 `{ kind, id }` 压成选择器 option 的 `value`。 */
export function formatWorkScopeKey(kind: string, id: string): string {
  return `${kind}:${id}`
}

/**
 * 解析作业范围选择键；解析不出就返回 `undefined`（由调用方回退到授权清单第一项）。
 *
 * 只按**第一个**冒号切分：`id` 本身可能含冒号（如 `site:SITE-001:LINE-1` 这类复合标识），
 * 用 `split(':')` 会把它截断成越权/不存在的范围。空串、无冒号、冒号在首位（kind 为空）、
 * 冒号在末位（id 为空）一律判定为无效。
 */
export function parseWorkScopeKey(value: string | undefined): WorkScopeKeyParts | undefined {
  if (!value) return undefined
  const separator = value.indexOf(':')
  if (separator <= 0 || separator === value.length - 1) return undefined
  return { kind: value.slice(0, separator), id: value.slice(separator + 1) }
}
