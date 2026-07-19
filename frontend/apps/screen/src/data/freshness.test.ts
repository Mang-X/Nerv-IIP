import { describe, expect, it } from 'vitest'
import { formatScreenFreshness } from './freshness'

describe('formatScreenFreshness', () => {
  it('fresh data exposes realtime state and its last update time', () => {
    const lastUpdated = new Date(2026, 6, 19, 12, 34, 56).getTime()

    expect(formatScreenFreshness(false, lastUpdated)).toEqual({
      tone: 'live',
      text: '实时 · 最后更新 12:34:56',
    })
  })

  it('stale data keeps the last update time and says that the snapshot is retained', () => {
    const lastUpdated = new Date(2026, 6, 19, 12, 34, 56).getTime()

    expect(formatScreenFreshness(true, lastUpdated)).toEqual({
      tone: 'stale',
      text: '数据滞留 · 最后更新 12:34:56',
    })
  })

  it('waits explicitly before the first successful update', () => {
    expect(formatScreenFreshness(false, undefined)).toEqual({
      tone: 'wait',
      text: '等待首次更新',
    })
  })
})
