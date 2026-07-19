import { describe, expect, it } from 'vitest'
import { LINE_OVERSCAN, LINE_ROW_ITEM_HEIGHT } from './line-layout'

describe('line selector virtual layout', () => {
  it('keeps the virtual item height equal to the rendered row and uses two-row overscan', () => {
    expect(LINE_ROW_ITEM_HEIGHT).toBe(258 + 16)
    expect(LINE_OVERSCAN).toBe(2)
  })
})
