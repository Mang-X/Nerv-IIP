import { describe, expect, it } from 'vitest'

import { formatDate, formatDateTime } from './format'

describe('formatDateTime', () => {
  it('空值返回「无」', () => {
    expect(formatDateTime(undefined)).toBe('无')
    expect(formatDateTime(null)).toBe('无')
    expect(formatDateTime('')).toBe('无')
  })

  it('非日期串原样返回（如本地乐观行的「本次录入」）', () => {
    expect(formatDateTime('本次录入')).toBe('本次录入')
    expect(formatDateTime('not-a-date')).toBe('not-a-date')
  })

  it('把过长的 ISO 时间格式化成本地「YYYY-MM-DD HH:mm」', () => {
    // 用本地时间字符串（无 Z），解析与格式化都在本地时区，结果与时区无关。
    expect(formatDateTime('2026-06-08T13:01:33.4122550')).toBe('2026-06-08 13:01')
    expect(formatDateTime('2026-01-05T09:07:00')).toBe('2026-01-05 09:07')
  })

  it('月/日/时/分补零', () => {
    expect(formatDateTime('2026-03-02T04:05:00')).toBe('2026-03-02 04:05')
  })
})

describe('formatDate', () => {
  it('只返回「YYYY-MM-DD」', () => {
    expect(formatDate('2026-06-08T13:01:33')).toBe('2026-06-08')
  })
  it('空值与非日期', () => {
    expect(formatDate(null)).toBe('无')
    expect(formatDate('本次录入')).toBe('本次录入')
  })
})
