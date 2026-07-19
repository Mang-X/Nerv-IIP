import { describe, expect, it } from 'vitest'
import { LINE_CARD_HEIGHT, LINE_OVERSCAN, LINE_ROW_GAP, LINE_ROW_ITEM_HEIGHT } from './line-layout'

describe('line selector virtual layout', () => {
  it('derives the virtual item height from rendered geometry and keeps two-row overscan', () => {
    expect(LINE_ROW_ITEM_HEIGHT).toBe(LINE_CARD_HEIGHT + LINE_ROW_GAP)
    expect(LINE_OVERSCAN).toBeGreaterThanOrEqual(2)
  })
})
