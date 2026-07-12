// 大屏多流 scope 一致性（MAN-467 review 修正）：board/ops 两条独立 useScreenData 流各自标记
// 取数时的 scope key。页面仅在 ops 的 scope 与当前 board 的 scope 一致时才用 ops 覆盖 board，
// 避免切换工厂/persona 的瞬间（旧 ops 请求 in-flight 或后续失败）用旧 scope 的 tick 覆盖已刷新的
// 新 board。board 是完整快照，作为权威兜底。
export interface Scoped<T> {
  scopeKey: string
  data: T
}

/** ops 仅当其 scope 与 board 一致时才优先返回其 data；否则 undefined（页面回退 board 完整快照）。 */
export function scopedOverride<T>(
  override: Scoped<T> | undefined,
  base: Scoped<unknown> | undefined,
): T | undefined {
  if (!override || !base) return undefined
  return override.scopeKey === base.scopeKey ? override.data : undefined
}
