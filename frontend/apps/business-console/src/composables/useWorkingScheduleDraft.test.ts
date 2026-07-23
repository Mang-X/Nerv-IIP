import { describe, expect, it } from 'vitest'
import { useWorkingScheduleDraft } from './useWorkingScheduleDraft'

const plan = {
  planId: 'plan-001',
  algorithmVersion: 'aps-lite-v1',
  status: 'generated' as const,
  assignments: [
    {
      assignmentId: 'assignment-001',
      orderId: 'WO-001',
      operationId: 'OP-10',
      operationSequence: 10,
      resourceId: 'RES-1',
      workCenterId: 'WC-1',
      startUtc: '2026-07-24T08:00:00Z',
      endUtc: '2026-07-24T09:00:00Z',
      isLocked: false,
    },
  ],
}

describe('useWorkingScheduleDraft', () => {
  it('keeps drag, table edit, lock and undo in one draft history', () => {
    const draft = useWorkingScheduleDraft()
    draft.setOrders([{ workOrderId: 'WO-001', priority: 10 }])
    draft.loadPlan(plan)

    draft.moveTask({
      taskId: 'assignment-001',
      operationId: 'OP-10',
      resourceId: 'RES-2',
      startUtc: '2026-07-24T10:00:00Z',
      endUtc: '2026-07-24T11:00:00Z',
      kind: 'reassign',
    })
    draft.updateTask('assignment-001', { startUtc: '2026-07-24T10:30:00Z' })
    draft.setLocked('assignment-001', true)

    expect(draft.model.value?.tasks.find((task) => task.id === 'assignment-001')).toMatchObject({
      resourceId: 'RES-2',
      startUtc: '2026-07-24T10:30:00Z',
      locked: true,
    })
    expect(draft.lockedAssignments.value).toHaveLength(1)
    draft.undo()
    expect(draft.model.value?.tasks.find((task) => task.id === 'assignment-001')?.locked).toBe(
      false,
    )
  })

  it('selects 100 orders in one mutation and serializes priorities and rush facts', () => {
    const draft = useWorkingScheduleDraft()
    const candidates = Array.from({ length: 100 }, (_, index) => ({
      workOrderId: `WO-${index + 1}`,
      priority: index,
    }))
    draft.setOrders(candidates)
    draft.setIncluded(
      candidates.map((candidate) => candidate.workOrderId),
      true,
    )
    draft.updateOrder('WO-1', { isRush: true, priority: 0 })

    expect(draft.includedOrders.value).toHaveLength(100)
    expect(draft.includedOrders.value[0]).toMatchObject({ isRush: true, priority: 0 })
  })

  it('rejects all mutations when the draft is read-only', () => {
    const draft = useWorkingScheduleDraft(true)
    draft.setOrders([{ workOrderId: 'WO-001' }])
    expect(() => draft.setIncluded(['WO-001'], true)).toThrow(/read-only/)
  })
})
