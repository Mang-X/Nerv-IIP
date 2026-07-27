import { describe, expect, it } from 'vitest'
import type { MesLifecycleActionKey } from './useMesTaskSemantics'
import {
  hasBlockingReasons,
  isSettledTask,
  resolveDispatchAffordance,
  resolveDispatchState,
  resolveExecutionState,
  resolveLifecycleActions,
  resolveScheduleState,
} from './useMesTaskSemantics'

function enabledActions(status: string): MesLifecycleActionKey[] {
  return resolveLifecycleActions({ status })
    .filter((a) => a.enabled)
    .map((a) => a.key)
}

describe('resolveExecutionState', () => {
  it('speaks shop-floor Chinese for every backend lifecycle status', () => {
    expect(resolveExecutionState('queued').label).toBe('待开工')
    expect(resolveExecutionState('inProgress').label).toBe('加工中')
    expect(resolveExecutionState('started').label).toBe('加工中')
    expect(resolveExecutionState('paused').label).toBe('已暂停')
    expect(resolveExecutionState('completed').label).toBe('已完工')
    expect(resolveExecutionState('scheduleInvalidated').label).toBe('待重排')
  })

  it('is case-insensitive over the raw domain casing', () => {
    expect(resolveExecutionState('SCHEDULEINVALIDATED').label).toBe('待重排')
    expect(resolveExecutionState('InProgress').label).toBe('加工中')
  })

  it('falls back to a neutral unknown label rather than echoing the raw code', () => {
    expect(resolveExecutionState('somethingNew').label).toBe('未知状态')
    expect(resolveExecutionState(null).label).toBe('未知状态')
  })
})

describe('resolveScheduleState', () => {
  it('never reports a finished operation as 未排程', () => {
    const finished = resolveScheduleState({ status: 'completed', scheduledAtUtc: null })
    expect(finished.label).toBe('已完工，不适用')
    expect(finished.label).not.toContain('未排程')
  })

  it('separates 已排程 / 未排程 / 人工派工 for live operations', () => {
    expect(resolveScheduleState({ status: 'queued', scheduledAtUtc: '2026-07-27T02:00:00Z' }).label).toBe(
      '已排程',
    )
    expect(resolveScheduleState({ status: 'queued' }).label).toBe('未排程')
    expect(resolveScheduleState({ status: 'queued', assignedUserId: 'u-1' }).label).toBe(
      '人工派工，未排产',
    )
  })

  it('lets schedule invalidation win over everything else', () => {
    expect(
      resolveScheduleState({ status: 'scheduleInvalidated', scheduledAtUtc: '2026-07-27T02:00:00Z' })
        .label,
    ).toBe('排程已失效')
  })
})

describe('resolveDispatchState', () => {
  it('shows the worker name when the operation is dispatched', () => {
    expect(resolveDispatchState({ status: 'inProgress', assignedUserId: 'u-1', assignedUserName: '陈立国' }).label).toBe(
      '已派 陈立国',
    )
  })

  it('says 已派工 when the id is present but the name was not backfilled', () => {
    expect(resolveDispatchState({ status: 'inProgress', assignedUserId: 'u-1' }).label).toBe('已派工')
  })

  it('never reports a finished operation as 待派工', () => {
    const finished = resolveDispatchState({ status: 'completed' })
    expect(finished.label).toBe('完工未记录工人')
    expect(finished.label).not.toContain('待派工')
  })

  it('reports 待派工 only for live unassigned operations', () => {
    expect(resolveDispatchState({ status: 'queued' }).label).toBe('待派工')
  })
})

describe('resolveDispatchAffordance', () => {
  it('offers a verb, and keeps the reason out of the button label', () => {
    const blocked = resolveDispatchAffordance({
      status: 'queued',
      blockingReasons: ['QUALITY_HOLD_ACTIVE'],
    })
    expect(blocked.label).toBe('派工')
    expect(blocked.enabled).toBe(false)
    expect(blocked.blockedReason).toContain('阻塞')
  })

  it('disables dispatch for finished and invalidated operations with distinct reasons', () => {
    expect(resolveDispatchAffordance({ status: 'completed' }).blockedReason).toContain('已结束')
    expect(resolveDispatchAffordance({ status: 'scheduleInvalidated' }).blockedReason).toContain(
      '重新排程',
    )
  })

  it('switches to 改派 once somebody is already assigned', () => {
    const reassign = resolveDispatchAffordance({
      status: 'queued',
      assignedUserId: 'u-1',
      assignedUserName: '陈立国',
    })
    expect(reassign.label).toBe('改派（当前 陈立国）')
    expect(reassign.enabled).toBe(true)
  })
})

describe('resolveLifecycleActions', () => {
  // 对齐后端 OperationTask 聚合的状态机，避免点下去才被后端顶回来。
  it('only offers 开工 from the queued state', () => {
    expect(enabledActions('Queued')).toEqual(['start'])
  })

  it('offers 暂停 and 完工 while running, and nothing else', () => {
    expect(enabledActions('InProgress')).toEqual(['pause', 'complete'])
  })

  it('offers only 恢复加工 while paused', () => {
    expect(enabledActions('Paused')).toEqual(['resume'])
  })

  it('offers nothing for settled or invalidated operations', () => {
    expect(enabledActions('Completed')).toEqual([])
    expect(enabledActions('Cancelled')).toEqual([])
    expect(enabledActions('ScheduleInvalidated')).toEqual([])
  })

  it('explains why a disabled action is disabled, per state', () => {
    const settled = resolveLifecycleActions({ status: 'Completed' })
    expect(settled.every((a) => a.blockedReason?.includes('已结束'))).toBe(true)

    const invalidated = resolveLifecycleActions({ status: 'ScheduleInvalidated' })
    expect(invalidated.every((a) => a.blockedReason?.includes('重新排程'))).toBe(true)

    const queued = resolveLifecycleActions({ status: 'Queued' })
    expect(queued.find((a) => a.key === 'complete')?.blockedReason).toContain('先开工')
  })

  it('marks 完工 as needing confirmation', () => {
    expect(resolveLifecycleActions({ status: 'InProgress' }).find((a) => a.key === 'complete')?.confirm).toBe(
      true,
    )
  })
})

describe('isSettledTask / hasBlockingReasons', () => {
  it('treats completed, closed, cancelled and scrapped as settled', () => {
    for (const status of ['completed', 'closed', 'cancelled', 'scrapped']) {
      expect(isSettledTask(status)).toBe(true)
    }
    for (const status of ['queued', 'inProgress', 'paused', 'scheduleInvalidated']) {
      expect(isSettledTask(status)).toBe(false)
    }
  })

  it('reports blocking reasons only when the list is non-empty', () => {
    expect(hasBlockingReasons({ blockingReasons: [] })).toBe(false)
    expect(hasBlockingReasons({ blockingReasons: ['EQUIPMENT_UNAVAILABLE'] })).toBe(true)
  })
})
