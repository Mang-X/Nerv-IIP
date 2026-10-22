/**
 * 主数据目录 → 可选项 / 可读名（框架无关，纯 TS）。
 *
 * 这段逻辑原先在 `business-console/src/pages/mes/handovers.vue` 与 PDA 交接班 composable 里
 * 各存一份（含同一条 GUID 正则），是第三份副本出现前的收拢点。搬到这里**不需要改动 console**：
 * console 那份继续按原样跑，后续替换由它自己那张票承担。
 *
 * 判据的成因各不相同，合并任意两条都会让某一种真相被谎报成另一种：
 * - 空值 → 回落文案（「未排班」/「未指派班组」），说的是「没这个东西」；
 * - GUID 形状 → 回落文案，工程标识符任何情况下都不上屏（ADR/设计准则：界面无工程语言）；
 * - 目录里查不到 → **原样回显业务码**（DAY / TEAM-ASSY-A 这类码在车间本身可读），
 *   吞成回落文案会把「查不到名字」谎报成「没排班」。
 */

/**
 * 主数据码是否是工程标识符（GUID 形状）。
 *
 * 容忍花括号/圆括号包裹：.NET 的 `Guid.ToString("B")` / `("P")` 会产出这两种写法，
 * 读面偶尔原样透出。只认整串匹配——业务码里嵌一段 GUID 也不该被整条丢掉。
 */
const SYSTEM_ID_PATTERN =
  /^[{(]?[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}[)}]?$/i

export function isSystemIdentifier(value?: string | null): boolean {
  return SYSTEM_ID_PATTERN.test((value ?? '').trim())
}

export interface DirectoryOptionSource {
  code?: string
  displayName?: string
  active?: boolean
}

export interface DirectoryOption {
  value: string
  label: string
}

/**
 * 主数据行 → 选择器选项。
 *
 * `active` 缺省视为可选：读面不回这个字段时整表消失比留着更糟。
 */
export function toDirectoryOptions(resources: readonly DirectoryOptionSource[]): DirectoryOption[] {
  return resources
    .map((resource) => {
      const value = resource.code?.trim()
      if (!value || resource.active === false || isSystemIdentifier(value)) return undefined
      const displayName = resource.displayName?.trim()
      const label = displayName && !isSystemIdentifier(displayName) ? displayName : value
      return { value, label }
    })
    .filter((option): option is DirectoryOption => Boolean(option))
    .sort((a, b) => a.label.localeCompare(b.label, 'zh-Hans-CN'))
}

/** 主数据码 → 中文显示名；查不到时原样回显业务码，见文件头三条判据。 */
export function resolveDirectoryLabel(
  value: string | null | undefined,
  labels: ReadonlyMap<string, string>,
  fallback: string,
): string {
  const code = (value ?? '').trim()
  if (!code || isSystemIdentifier(code)) return fallback
  return labels.get(code) ?? code
}
