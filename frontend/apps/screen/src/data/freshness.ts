export type ScreenFreshnessTone = 'live' | 'stale' | 'wait'

export interface ScreenFreshness {
  tone: ScreenFreshnessTone
  text: string
}

function clockOf(timestamp: number): string {
  const date = new Date(timestamp)
  const pad = (value: number) => String(value).padStart(2, '0')
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
}

/** Stable footer copy for one data source; stale data remains visible but is named as retained. */
export function formatScreenFreshness(
  isStale: boolean,
  lastUpdated: number | undefined,
): ScreenFreshness {
  if (!lastUpdated) return { tone: 'wait', text: '等待首次更新' }
  const updateText = `最后更新 ${clockOf(lastUpdated)}`
  return {
    tone: isStale ? 'stale' : 'live',
    text: `${isStale ? '数据滞留' : '实时'} · ${updateText}`,
  }
}
