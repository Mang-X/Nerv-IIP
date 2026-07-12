import { describe, expect, it } from 'vitest'

import {
  describeScheduleInvalidationReason,
  isScheduleInvalidated,
  resolveScheduleStatus,
  scheduleInvalidationHint,
} from './useScheduleInvalidation'

describe('useScheduleInvalidation', () => {
  it('marks a schedule-invalidated task regardless of assignment', () => {
    const display = resolveScheduleStatus({ status: 'scheduleInvalidated', assignedAtUtc: null })
    expect(display.key).toBe('invalidated')
    expect(display.label).toBe('已失效')
    expect(display.tone).toBe('warning')
    expect(isScheduleInvalidated('scheduleInvalidated')).toBe(true)
  })

  it('treats a queued task without an assignment time as 未排程', () => {
    const display = resolveScheduleStatus({ status: 'queued', assignedAtUtc: null })
    expect(display.key).toBe('unscheduled')
    expect(display.label).toBe('未排程')
    expect(display.tone).toBe('neutral')
  })

  it('treats a queued task that already has a schedule assignment as 已排程', () => {
    const display = resolveScheduleStatus({
      status: 'queued',
      assignedAtUtc: '2026-07-02T08:00:00Z',
    })
    expect(display.key).toBe('scheduled')
    expect(display.label).toBe('已排程')
    expect(display.tone).toBe('info')
  })

  it('treats in-progress tasks as 已排程 even without an assignment time', () => {
    expect(resolveScheduleStatus({ status: 'inProgress', assignedAtUtc: null }).key).toBe(
      'scheduled',
    )
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
