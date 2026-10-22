import { describe, expect, it } from 'vitest'
import { defaultProductionStatisticsWindow } from './productionStatisticsWindow'

describe('production statistics default window', () => {
  it('covers seven complete local calendar dates including today', () => {
    const now = new Date(2026, 7, 31, 12, 34, 56)

    expect(defaultProductionStatisticsWindow(now)).toEqual({
      startUtc: new Date(2026, 7, 25).toISOString(),
      endUtc: new Date(2026, 8, 1).toISOString(),
    })
  })
})
