import { describe, expect, it } from 'vitest'

import {
  describeScheduleInvalidationReason,
  isScheduleInvalidated,
  resolveScheduleStatus,
  scheduleInvalidationHint,
} from './useScheduleInvalidation'

describe('useScheduleInvalidation', () => {
  it('marks a schedule-invalidated task regardless of scheduling', () => {
    const display = resolveScheduleStatus({ status: 'scheduleInvalidated', scheduledAtUtc: null })
    expect(display.key).toBe('invalidated')
    expect(display.label).toBe('已失效')
    expect(display.tone).toBe('warning')
    expect(isScheduleInvalidated('scheduleInvalidated')).toBe(true)
  })

  it('treats a queued task that was never scheduled as 未排程', () => {
    const display = resolveScheduleStatus({ status: 'queued', scheduledAtUtc: null })
    expect(display.key).toBe('unscheduled')
    expect(display.label).toBe('未排程')
    expect(display.tone).toBe('neutral')
  })

  it('does not treat a manually-dispatched-but-unscheduled task as 已排程', () => {
    // Manual dispatch writes assignedAtUtc but not scheduledAtUtc; the row only carries scheduledAtUtc,
    // so a queued task placed only by an operator stays 未排程 until a released APS plan schedules it.
    const display = resolveScheduleStatus({ status: 'queued', scheduledAtUtc: null })
    expect(display.key).toBe('unscheduled')
  })

  it('treats a task placed by a released schedule as 已排程', () => {
    const display = resolveScheduleStatus({
      status: 'queued',
      scheduledAtUtc: '2026-07-02T08:00:00Z',
    })
    expect(display.key).toBe('scheduled')
    expect(display.label).toBe('已排程')
    expect(display.tone).toBe('info')
  })

  it('treats an unscheduled in-progress task as 未排程 (no APS placement fact)', () => {
    expect(resolveScheduleStatus({ status: 'inProgress', scheduledAtUtc: null }).key).toBe(
      'unscheduled',
    )
    expect(
      resolveScheduleStatus({ status: 'inProgress', scheduledAtUtc: '2026-07-02T08:00:00Z' }).key,
    ).toBe('scheduled')
  })

  it('localizes known reason codes and falls back for unknown/empty ones', () => {
    expect(describeScheduleInvalidationReason('equipmentUnavailable')).toBe('设备不可用')
    expect(describeScheduleInvalidationReason('materialReadinessChanged')).toBe('物料齐套变化')
    expect(describeScheduleInvalidationReason('workOrderReleased')).toBe('工单已下达')
    expect(describeScheduleInvalidationReason('somethingElse')).toBe('somethingElse')
    expect(describeScheduleInvalidationReason(null)).toBe('排程前提已变化')
  })

  it('builds an actionable hint that includes the reason when present', () => {
    expect(scheduleInvalidationHint('equipmentUnavailable')).toBe(
      '排程已失效：设备不可用，需重新排程。',
    )
    expect(scheduleInvalidationHint(null)).toBe('排程已失效，需重新排程。')
  })
})
