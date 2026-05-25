import { describe, expect, it } from 'vitest'

import { createTimeScale } from '../time-scale/timeScale'

describe('createTimeScale', () => {
  it('maps ISO dates to stable pixels and back', () => {
    const scale = createTimeScale({
      start: '2026-05-01T00:00:00.000Z',
      end: '2026-05-11T00:00:00.000Z',
      width: 1000,
      zoom: 'day',
    })

    expect(scale.dateToX('2026-05-06T00:00:00.000Z')).toBe(500)
    expect(scale.xToDate(500).toISOString()).toBe('2026-05-06T00:00:00.000Z')
  })

  it('creates readable tick labels for week zoom', () => {
    const scale = createTimeScale({
      start: '2026-05-01T00:00:00.000Z',
      end: '2026-05-22T00:00:00.000Z',
      width: 840,
      zoom: 'week',
    })

    expect(scale.ticks.map((tick) => tick.label)).toEqual([
      'May 1',
      'May 8',
      'May 15',
      'May 22',
    ])
  })
})
