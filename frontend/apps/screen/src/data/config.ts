export type ScreenDataMode = 'mock' | 'real'

export function resolveMode(raw: string | undefined): ScreenDataMode {
  const mode = raw?.trim().toLowerCase()
  return mode === 'mock' || mode === 'demo' ? 'mock' : 'real'
}

export const SCREEN_DATA_MODE = resolveMode(import.meta.env.VITE_SCREEN_DATA_MODE)
export const IS_REAL_DATA = SCREEN_DATA_MODE === 'real'

export function requireDemoMode(): void {
  if (IS_REAL_DATA) throw new Error('当前指标暂无真实来源')
}
