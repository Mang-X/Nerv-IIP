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

  it('creates week-specific tick labels for week zoom', () => {
    const scale = createTimeScale({
      start: '2026-05-01T00:00:00.000Z',
      end: '2026-05-22T00:00:00.000Z',
      width: 840,
      zoom: 'week',
    })

    expect(scale.ticks.map((tick) => tick.label)).toEqual([
      'W18',
      'W19',
      'W20',
      'W21',
    ])
  })

  it('creates month-specific tick labels for month zoom', () => {
    const scale = createTimeScale({
      start: '2026-05-01T00:00:00.000Z',
      end: '2026-08-01T00:00:00.000Z',
      width: 840,
      zoom: 'month',
    })

    expect(scale.ticks.map((tick) => tick.label)).toEqual([
      'May 2026',
      'May 2026',
      'Jun 2026',
      'Jul 2026',
    ])
  })
})
