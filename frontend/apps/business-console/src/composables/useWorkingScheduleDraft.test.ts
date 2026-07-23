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
    {
      assignmentId: 'assignment-002',
      orderId: 'WO-001',
      operationId: 'OP-20',
      operationSequence: 20,
      resourceId: 'RES-1',
      workCenterId: 'WC-1',
      startUtc: '2026-07-24T09:00:00Z',
      endUtc: '2026-07-24T10:00:00Z',
      isLocked: false,
    },
    {
      assignmentId: 'assignment-003',
      orderId: 'WO-003',
      operationId: 'OP-10',
      operationSequence: 10,
      resourceId: 'RES-2',
      workCenterId: 'WC-2',
      startUtc: '2026-07-24T08:00:00Z',
      endUtc: '2026-07-24T09:00:00Z',
      isLocked: false,
    },
  ],
  unscheduledOperations: [
    {
      orderId: 'WO-002',
      operationId: 'OP-20',
      reasonCode: 'capacity' as const,
      message: '瓶颈资源产能不足',
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

  it('keeps backend-unscheduled, invalidated, and manually removed operations in one pending pool', () => {
    const draft = useWorkingScheduleDraft()
    draft.loadPlan(plan, {
      isInvalidated: true,
      affectedWorkOrderIds: ['WO-001'],
      affectedOperationIds: ['OP-10'],
      reasonCode: 'equipmentUnavailable',
    })

    expect(draft.pendingOperations.value).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          orderId: 'WO-002',
          operationId: 'OP-20',
          source: 'unscheduled',
        }),
        expect.objectContaining({
          orderId: 'WO-001',
          operationId: 'OP-10',
          source: 'invalidated',
        }),
      ]),
    )
    expect(
      draft.pendingOperations.value.filter((item) => item.source === 'invalidated'),
    ).toHaveLength(1)

    draft.moveTaskToPending('assignment-001')
    expect(draft.model.value?.tasks.some((task) => task.id === 'assignment-001')).toBe(false)
    expect(draft.pendingOperations.value).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          taskId: 'assignment-001',
          source: 'removed',
          canRestore: true,
        }),
      ]),
    )

    draft.restorePendingTask('assignment-001')
    expect(draft.model.value?.tasks.some((task) => task.id === 'assignment-001')).toBe(true)
    expect(draft.model.value?.links).toHaveLength(1)
    draft.undo()
    expect(draft.model.value?.tasks.some((task) => task.id === 'assignment-001')).toBe(false)
  })

  it('keeps invalidation provenance when an affected operation is also unscheduled', () => {
    const draft = useWorkingScheduleDraft()
    draft.loadPlan(plan, {
      isInvalidated: true,
      affectedWorkOrderIds: ['WO-002'],
      affectedOperationIds: ['OP-20'],
      reasonCode: 'equipmentUnavailable',
    })

    expect(
      draft.pendingOperations.value.find(
        (item) => item.orderId === 'WO-002' && item.operationId === 'OP-20',
      ),
    ).toMatchObject({
      source: 'invalidated',
      reasonCode: 'equipmentUnavailable',
      message: expect.stringContaining('瓶颈资源产能不足'),
    })
  })

  it('restores baseline dependency links only after both pending operations return', () => {
    const draft = useWorkingScheduleDraft()
    draft.loadPlan(plan)

    draft.moveTaskToPending('assignment-001')
    draft.moveTaskToPending('assignment-002')
    draft.restorePendingTask('assignment-001')
    expect(draft.model.value?.links).toHaveLength(0)

    draft.restorePendingTask('assignment-002')
    expect(draft.model.value?.links).toHaveLength(1)
  })

  it('removes an empty order lane and restores it with its pending operation', () => {
    const draft = useWorkingScheduleDraft()
    draft.loadPlan(plan)

    draft.moveTaskToPending('assignment-003')
    expect(draft.model.value?.tasks.some((task) => task.id === 'order:WO-003')).toBe(false)

    draft.restorePendingTask('assignment-003')
    expect(draft.model.value?.tasks.some((task) => task.id === 'order:WO-003')).toBe(true)
  })

  it('blocks locked edits and can lock every modified operation before repreview', () => {
    const draft = useWorkingScheduleDraft()
    draft.loadPlan(plan)

    draft.updateTask('assignment-001', { resourceId: 'RES-2' })
    expect(draft.modifiedUnlockedTaskIds.value).toEqual(['assignment-001'])

    draft.lockModifiedTasks()
    expect(draft.modifiedUnlockedTaskIds.value).toEqual([])
    expect(draft.lockedAssignments.value).toHaveLength(1)

    draft.updateTask('assignment-001', { resourceId: 'RES-3' })
    expect(draft.model.value?.tasks.find((task) => task.id === 'assignment-001')?.resourceId).toBe(
      'RES-2',
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
