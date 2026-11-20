export type ScreenDataMode = 'mock' | 'real'

export function resolveMode(raw: string | undefined): ScreenDataMode {
  return raw?.trim().toLowerCase() === 'mock' ? 'mock' : 'real'
}

export const SCREEN_DATA_MODE = resolveMode(import.meta.env.VITE_SCREEN_DATA_MODE)
export const IS_REAL_DATA = SCREEN_DATA_MODE === 'real'
