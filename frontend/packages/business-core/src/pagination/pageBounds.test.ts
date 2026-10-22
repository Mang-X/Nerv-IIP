import { describe, expect, it } from 'vitest'

import { lastPageForTotal } from './pageBounds'

describe('lastPageForTotal', () => {
  it.each([
    { total: 0, pageSize: 200, expected: 1 },
    { total: 200, pageSize: 200, expected: 1 },
    { total: 201, pageSize: 200, expected: 2 },
  ])('把 $total 条数据换算为 $expected 页', ({ total, pageSize, expected }) => {
    expect(lastPageForTotal(total, pageSize)).toBe(expected)
  })
})
