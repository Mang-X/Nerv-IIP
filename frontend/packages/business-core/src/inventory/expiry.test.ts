import { describe, expect, it } from 'vitest'
import {
  EXPIRY_CRITICAL_THRESHOLD_DAYS,
  EXPIRY_NEAR_THRESHOLD_DAYS,
  expiryDaysUntil,
  expiryTone,
  expiryToneFromDate,
  expiryToneLabel,
  isNearOrExpired,
} from './expiry'

describe('expiry 三色口径', () => {
  const asOf = '2026-07-15'

  it('阈值常量与口径一致（90/30）', () => {
    expect(EXPIRY_NEAR_THRESHOLD_DAYS).toBe(90)
    expect(EXPIRY_CRITICAL_THRESHOLD_DAYS).toBe(30)
  })

  it('expiryDaysUntil 按整天差（UTC 零点）计算', () => {
    expect(expiryDaysUntil('2026-07-15', asOf)).toBe(0)
    expect(expiryDaysUntil('2026-07-16', asOf)).toBe(1)
    expect(expiryDaysUntil('2026-07-14', asOf)).toBe(-1)
    expect(expiryDaysUntil('2026-10-13', asOf)).toBe(90)
  })

  it('含时间的 ISO 串按日粒度比较，不受时区影响', () => {
    expect(expiryDaysUntil('2026-07-16T23:00:00Z', asOf)).toBe(1)
  })

  it('非法/空效期返回 null', () => {
    expect(expiryDaysUntil('', asOf)).toBeNull()
    expect(expiryDaysUntil(null, asOf)).toBeNull()
    expect(expiryDaysUntil('2026-13-40', asOf)).toBeNull()
  })

  it('expiryTone 分段：>90 绿 / 30–90 黄 / <30 红 / <0 已过期', () => {
    expect(expiryTone(120)).toBe('fresh')
    expect(expiryTone(91)).toBe('fresh')
    expect(expiryTone(90)).toBe('near') // 90 含在临期
    expect(expiryTone(30)).toBe('near') // 30 含在临期
    expect(expiryTone(29)).toBe('critical')
    expect(expiryTone(0)).toBe('critical')
    expect(expiryTone(-1)).toBe('expired')
  })

  it('expiryToneFromDate 串接日期→tone；未知效期返回 null', () => {
    expect(expiryToneFromDate('2026-12-31', asOf)).toBe('fresh')
    expect(expiryToneFromDate('2026-09-15', asOf)).toBe('near') // 约 62 天
    expect(expiryToneFromDate('2026-08-01', asOf)).toBe('critical') // 约 17 天
    expect(expiryToneFromDate('2026-07-20', asOf)).toBe('critical')
    expect(expiryToneFromDate('2026-07-01', asOf)).toBe('expired')
    expect(expiryToneFromDate(undefined, asOf)).toBeNull()
  })

  it('isNearOrExpired 覆盖黄/红/已过期（收货黄色提示判据）', () => {
    expect(isNearOrExpired('fresh')).toBe(false)
    expect(isNearOrExpired('near')).toBe(true)
    expect(isNearOrExpired('critical')).toBe(true)
    expect(isNearOrExpired('expired')).toBe(true)
    expect(isNearOrExpired(null)).toBe(false)
  })

  it('expiryToneLabel 中文标签', () => {
    expect(expiryToneLabel('fresh')).toBe('正常')
    expect(expiryToneLabel('near')).toBe('临期')
    expect(expiryToneLabel('critical')).toBe('临近过期')
    expect(expiryToneLabel('expired')).toBe('已过期')
    expect(expiryToneLabel(null)).toBe('效期未知')
  })
})
