import { describe, expect, it } from 'vitest'

import { formatWorkScopeKey, parseWorkScopeKey } from './workScopeKey'

describe('formatWorkScopeKey', () => {
  it('压成 kind:id', () => {
    expect(formatWorkScopeKey('self', 'me')).toBe('self:me')
    expect(formatWorkScopeKey('work-pool', 'WC-CNC')).toBe('work-pool:WC-CNC')
  })
})

describe('parseWorkScopeKey', () => {
  it('按第一个冒号切分', () => {
    expect(parseWorkScopeKey('site:SITE-001')).toEqual({ kind: 'site', id: 'SITE-001' })
  })

  // id 含冒号的复合标识不能被截断，否则会解析成一个越权/不存在的范围。
  it('id 内含冒号时保留完整 id', () => {
    expect(parseWorkScopeKey('site:SITE-001:LINE-1')).toEqual({
      kind: 'site',
      id: 'SITE-001:LINE-1',
    })
  })

  it('空值 / 无冒号 / kind 为空 / id 为空一律判无效', () => {
    expect(parseWorkScopeKey(undefined)).toBeUndefined()
    expect(parseWorkScopeKey('')).toBeUndefined()
    expect(parseWorkScopeKey('self')).toBeUndefined()
    expect(parseWorkScopeKey(':SITE-001')).toBeUndefined()
    expect(parseWorkScopeKey('site:')).toBeUndefined()
    expect(parseWorkScopeKey(':')).toBeUndefined()
  })

  it('编解码可往返', () => {
    const key = formatWorkScopeKey('work-pool', 'WC-CNC')
    expect(parseWorkScopeKey(key)).toEqual({ kind: 'work-pool', id: 'WC-CNC' })
  })
})
