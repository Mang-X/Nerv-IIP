import { describe, expect, it } from 'vitest'
import { samplePlan } from './fixtures'
import { toLockedAssignments, toModel } from './aps-mapper'

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
    expect(m.links).toEqual([
      { id: 'a1->a2', source: 'a1', target: 'a2', type: 'finish_to_start' },
    ])
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
    expect(m.meta).toEqual({ planId: 'plan-1', status: 'generated', algorithmVersion: 'heuristic-1' })
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
})
