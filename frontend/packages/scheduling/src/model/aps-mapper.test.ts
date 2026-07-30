import { describe, expect, it } from 'vitest'
import { samplePlan, samplePlanWithCalendar } from './fixtures'
import { toLockedAssignments, toModel } from './aps-mapper'
import { conflictReasonLabel } from './labels'

describe('toModel', () => {
  it('maps assignments to operation tasks with stable ids and grouping parents', () => {
    const m = toModel(samplePlan)
    const op = m.tasks.find((t) => t.id === 'a1')!
    expect(op.type).toBe('operation')
    expect(op.orderId).toBe('WO-001')
    expect(op.parentId).toBe('order:WO-001')
    expect(op.locked).toBe(false)
    expect(m.tasks.some((t) => t.id === 'order:WO-001' && t.type === 'order')).toBe(true)
  })

  it('derives finish_to_start links from operationSequence within an order', () => {
    const m = toModel(samplePlan)
    expect(m.links).toEqual([{ id: 'a1->a2', source: 'a1', target: 'a2', type: 'finish_to_start' }])
  })

  it('flags conflicts onto their tasks and carries taskId', () => {
    const m = toModel(samplePlan)
    const op20 = m.tasks.find((t) => t.operationId === 'op-20')!
    expect(op20.hasConflict).toBe(true)
    expect(op20.conflictReason).toBe('capacity')
    expect(m.conflicts[0].taskId).toBe('a2')
  })

  it('maps loads, unscheduled, changes and horizon', () => {
    const m = toModel(samplePlan)
    expect(m.loads[0].utilization).toBe(0.25)
    expect(m.unscheduled[0].reason).toBe('material')
    expect(m.changes[0].changeType).toBe('moved')
    expect(m.changes[0].taskId).toBe('a2')
    expect(m.horizon.startUtc).toBe('2026-06-10T08:00:00.000Z')
    expect(m.horizon.endUtc).toBe('2026-06-10T12:00:00.000Z')
    expect(m.meta).toEqual({
      planId: 'plan-1',
      status: 'generated',
      algorithmVersion: 'heuristic-1',
    })
  })

  it('keeps the current tooling conflict in business language', () => {
    const m = toModel({
      ...samplePlan,
      conflicts: [
        {
          conflictId: 'tooling-conflict',
          reasonCode: 'tooling',
          severity: 'error',
          orderId: 'WO-001',
          operationId: 'op-10',
          message: '所需工装不可用',
        },
      ],
    })

    expect(conflictReasonLabel[m.conflicts[0]!.reason]).toBe('工装不可用')
  })
})

describe('toModel — 工作日历与资源时间块', () => {
  it('把计划带出的班次窗口映射成模型日历', () => {
    const m = toModel(samplePlanWithCalendar)
    expect(m.calendars).toHaveLength(1)
    expect(m.calendars![0].calendarId).toBe('CAL-MAIN')
    expect(m.calendars![0].resourceIds).toEqual(['WC-001', 'WC-002'])
    expect(m.calendars![0].shiftWindows.map((w) => w.shiftCode)).toEqual([
      'early-shift',
      'middle-shift',
      'early-shift',
    ])
  })

  it('把不可用窗口映射成不可拖拽的资源时间块任务(带中文语义名)', () => {
    const m = toModel(samplePlanWithCalendar)
    const blocks = m.tasks.filter((t) => t.blockKind)
    expect(blocks.map((b) => b.blockKind)).toEqual(['changeover', 'maintenance'])
    expect(blocks.map((b) => b.text)).toEqual(['换型', '设备维护'])
    // 泳道键与工序一致(按工作中心),否则块会另起一条孤立泳道。
    expect(blocks.map((b) => b.dimensions?.workCenter?.id)).toEqual(['WC-001', 'WC-002'])
    expect(blocks.every((b) => b.locked)).toBe(true)
  })

  it('没有日历/不可用窗口时不编造:calendars 为空、任务里没有块', () => {
    const m = toModel(samplePlan)
    expect(m.calendars).toBeUndefined()
    expect(m.tasks.some((t) => t.blockKind)).toBe(false)
  })

  it('未知的块类型码值按停机处理,不丢窗口', () => {
    const m = toModel({
      ...samplePlanWithCalendar,
      blockWindows: [
        {
          resourceId: 'WC-001',
          workCenterId: 'WC-001',
          startUtc: '2026-06-10T10:00:00.000Z',
          endUtc: '2026-06-10T11:00:00.000Z',
          reasonCode: '未来才有的码值',
          kind: 'somethingNew' as never,
        },
      ],
    })
    expect(m.tasks.filter((t) => t.blockKind).map((t) => t.blockKind)).toEqual(['downtime'])
  })
})

describe('toLockedAssignments', () => {
  it('emits only locked operation tasks as assignment contracts', () => {
    const m = toModel(samplePlan)
    const op = m.tasks.find((t) => t.id === 'a1')!
    op.locked = true
    op.startUtc = '2026-06-10T09:00:00.000Z'
    const out = toLockedAssignments(m)
    expect(out.map((x) => x.assignmentId).sort()).toEqual(['a1', 'a2'])
    expect(out.find((x) => x.assignmentId === 'a1')!.startUtc).toBe('2026-06-10T09:00:00.000Z')
    expect(out.some((x) => (x.orderId ?? '').startsWith('order:'))).toBe(false)
  })

  it('资源时间块不当成锁定工序回传(它不是工序,只是不可拖拽)', () => {
    const out = toLockedAssignments(toModel(samplePlanWithCalendar))
    expect(out.map((x) => x.assignmentId)).toEqual(['a2'])
  })
})
