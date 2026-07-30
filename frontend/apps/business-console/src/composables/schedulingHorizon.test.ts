import { describe, expect, it } from 'vitest'
import {
  createSchedulingHorizonInput,
  DEFAULT_SCHEDULING_HORIZON_DAYS,
  describeSchedulingHorizon,
  fromLocalInputValue,
  MAX_SCHEDULING_HORIZON_DAYS,
  resolveSchedulingHorizon,
  toLocalInputValue,
  type SchedulingHorizonInput,
} from './schedulingHorizon'

const NOW = new Date('2026-07-30T09:37:42.123')

describe('排程窗口（MAN-694 / #1262）', () => {
  it('默认窗口沿用改造前的「现在起 7 天」，并把起点对齐到整点', () => {
    const input = createSchedulingHorizonInput(NOW)
    expect(input.mode).toBe('preset')
    expect(input.days).toBe(DEFAULT_SCHEDULING_HORIZON_DAYS)

    const resolved = resolveSchedulingHorizon(input, NOW)
    expect(resolved.ok).toBe(true)
    if (!resolved.ok) return
    const start = new Date(resolved.horizonStartUtc)
    const end = new Date(resolved.horizonEndUtc)
    expect(start.getMinutes()).toBe(0)
    expect(start.getSeconds()).toBe(0)
    expect(start.getMilliseconds()).toBe(0)
    expect((end.getTime() - start.getTime()) / 86_400_000).toBe(7)
  })

  it('快捷天数不再写死 7 天：换成 1 天窗口就只排一天', () => {
    const resolved = resolveSchedulingHorizon(
      { ...createSchedulingHorizonInput(NOW), days: 1 },
      NOW,
    )
    expect(resolved.ok).toBe(true)
    if (!resolved.ok) return
    const span =
      (new Date(resolved.horizonEndUtc).getTime() - new Date(resolved.horizonStartUtc).getTime()) /
      86_400_000
    expect(span).toBe(1)
  })

  it('自定义起止时间按用户填的值发给后端', () => {
    const input: SchedulingHorizonInput = {
      mode: 'custom',
      days: 7,
      startLocal: '2026-08-01T08:00',
      endLocal: '2026-08-03T20:00',
    }
    const resolved = resolveSchedulingHorizon(input, NOW)
    expect(resolved.ok).toBe(true)
    if (!resolved.ok) return
    expect(resolved.horizonStartUtc).toBe(new Date('2026-08-01T08:00').toISOString())
    expect(resolved.horizonEndUtc).toBe(new Date('2026-08-03T20:00').toISOString())
  })

  it('起止倒置 / 缺值 / 跨度过长一律给出可直接渲染的中文原因，而不是抛异常', () => {
    const base = { mode: 'custom', days: 7 } as const
    expect(
      resolveSchedulingHorizon({ ...base, startLocal: '2026-08-05T08:00', endLocal: '' }, NOW),
    ).toEqual({ ok: false, message: '请填写完整的排程窗口起止时间。' })
    expect(
      resolveSchedulingHorizon(
        { ...base, startLocal: '2026-08-05T08:00', endLocal: '2026-08-05T08:00' },
        NOW,
      ),
    ).toEqual({ ok: false, message: '排程窗口结束时间必须晚于开始时间。' })
    const tooLong = resolveSchedulingHorizon(
      { ...base, startLocal: '2026-01-01T00:00', endLocal: '2027-01-01T00:00' },
      NOW,
    )
    expect(tooLong).toEqual({
      ok: false,
      message: `排程窗口最长 ${MAX_SCHEDULING_HORIZON_DAYS} 天。`,
    })
  })

  it('preset 天数非法（0 / NaN / 超上限）同样落到失败态', () => {
    const base = createSchedulingHorizonInput(NOW)
    expect(resolveSchedulingHorizon({ ...base, days: 0 }, NOW).ok).toBe(false)
    expect(resolveSchedulingHorizon({ ...base, days: Number.NaN }, NOW).ok).toBe(false)
    expect(
      resolveSchedulingHorizon({ ...base, days: MAX_SCHEDULING_HORIZON_DAYS + 1 }, NOW).ok,
    ).toBe(false)
  })

  it('datetime-local 值往返不丢本地时区（不要偷偷按 UTC 解析）', () => {
    const local = toLocalInputValue(new Date('2026-08-01T08:30'))
    expect(local).toBe('2026-08-01T08:30')
    expect(fromLocalInputValue(local)?.getHours()).toBe(8)
    expect(fromLocalInputValue('')).toBeUndefined()
    expect(fromLocalInputValue('不是时间')).toBeUndefined()
  })

  it('失败时的说明文案就是错误原因本身，界面不必再写一遍', () => {
    const failed = resolveSchedulingHorizon(
      { mode: 'custom', days: 7, startLocal: '', endLocal: '' },
      NOW,
    )
    expect(describeSchedulingHorizon(failed)).toBe('请填写完整的排程窗口起止时间。')
  })
})
