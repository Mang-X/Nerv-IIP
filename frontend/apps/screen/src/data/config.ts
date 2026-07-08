// 大屏数据模式开关（MAN-466 样板）：mock（默认，dev fallback）↔ real（真实 business-console facade）。
// 通过 VITE_SCREEN_DATA_MODE=real 切真实数据；缺省 mock 保证 CI/typecheck/离线开发确定性。
// 后续 S3b/S3c 其它屏照抄本模块 + data/session.ts 即可复用同一套「开关 / 会话注入 / 失败降级」。
export type ScreenDataMode = 'mock' | 'real'

function resolveMode(): ScreenDataMode {
  // import.meta.env 在测试/SSR 下可能缺省，容错读取。
  const raw = (import.meta.env?.VITE_SCREEN_DATA_MODE ?? '') as string
  return raw.trim().toLowerCase() === 'real' ? 'real' : 'mock'
}

/** 当前数据模式（构建期固定）。 */
export const SCREEN_DATA_MODE: ScreenDataMode = resolveMode()

/** 是否走真实 business-console facade（否则 mock 演示数据流）。 */
export const IS_REAL_DATA: boolean = SCREEN_DATA_MODE === 'real'
