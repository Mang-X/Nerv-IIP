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
  it('recomputes feedback for move, resize, table edit, undo/redo, pending/restore and new plan (#4043)', () => {
    const draft = useWorkingScheduleDraft()
    const snapshot = {
      ...plan,
      calendars: [
        {
          calendarId: 'CAL-1',
          resourceIds: ['RES-1'],
          workCenterIds: ['WC-1'],
          shiftWindows: [
            { startUtc: '2026-07-24T08:00:00Z', endUtc: '2026-07-24T18:00:00Z', shiftCode: '白班' },
          ],
        },
      ],
      validationContext: {
        horizonStartUtc: '2026-07-24T00:00:00Z',
        horizonEndUtc: '2026-07-25T00:00:00Z',
        resources: [
          {
            resourceId: 'RES-1',
            workCenterId: 'WC-1',
            calendarId: 'CAL-1',
            capacityUnits: 1,
            utilizationRate: 1,
          },
          {
            resourceId: 'RES-2',
            workCenterId: 'WC-2',
            calendarId: 'CAL-1',
            capacityUnits: 1,
            utilizationRate: 1,
          },
        ],
        operations: plan.assignments.map((assignment) => ({
          orderId: assignment.orderId,
          operationId: assignment.operationId,
          predecessorOperationIds: assignment.operationId === 'OP-20' ? ['OP-10'] : [],
          dueUtc: '2026-07-24T10:00:00Z',
          durationMinutes: 60,
          setupMinutes: 0,
          isFixed: false,
        })),
        fixedReservations: [],
      },
    }
    draft.loadPlan(snapshot)
    const kinds = () =>
      draft.feedback.value!.tasks['assignment-002']!.issues.map((issue) => issue.kind)
    expect(kinds()).toEqual([])
    draft.moveTask({
      taskId: 'assignment-002',
      operationId: 'OP-20',
      resourceId: 'RES-1',
      startUtc: '2026-07-24T08:00:00Z',
      endUtc: '2026-07-24T09:00:00Z',
      kind: 'move',
    })
    expect(kinds()).toContain('predecessor')
    expect(kinds()).toContain('capacity')
    draft.undo()
    expect(kinds()).toEqual([])
    draft.redo()
    expect(kinds()).toContain('predecessor')
    draft.undo()
    draft.moveTask({
      taskId: 'assignment-002',
      operationId: 'OP-20',
      resourceId: 'RES-1',
      startUtc: '2026-07-24T09:00:00Z',
      endUtc: '2026-07-24T19:00:00Z',
      kind: 'resize',
    })
    expect(kinds()).toContain('calendar')
    expect(draft.feedback.value!.tasks['assignment-002']!.due?.status).toBe('late')
    draft.updateTask('assignment-002', { endUtc: '2026-07-24T10:00:00Z' })
    expect(kinds()).toEqual([])
    expect(draft.feedback.value!.tasks['assignment-002']!.due?.status).toBe('onTime')
    draft.moveTaskToPending('assignment-001')
    expect(kinds()).toContain('predecessorUnscheduled')
    expect(draft.model.value?.links).toHaveLength(0)
    draft.restorePendingTask('assignment-001')
    expect(kinds()).toEqual([])
    draft.undo()
    expect(kinds()).toContain('predecessorUnscheduled')
    draft.redo()
    expect(kinds()).toEqual([])
    draft.updateTask('assignment-002', { resourceId: 'RES-2' })
    expect(
      draft.model.value?.tasks.find((task) => task.id === 'assignment-002')?.workCenterId,
    ).toBe('WC-2')
    draft.loadPlan({ ...plan, planId: 'plan-002' })
    expect(kinds()).toEqual(['unknown'])
    expect(draft.canUndo.value).toBe(false)
  })
  it('keeps actual segments when locking a multi-segment draft (#4004)', () => {
    const draft = useWorkingScheduleDraft()
    const segments = [
      { startUtc: '2026-07-24T08:00:00Z', endUtc: '2026-07-24T09:00:00Z' },
      { startUtc: '2026-07-25T08:00:00Z', endUtc: '2026-07-25T09:00:00Z' },
    ]
    draft.loadPlan({
      ...plan,
      assignments: [{ ...plan.assignments[0]!, endUtc: segments[1]!.endUtc, segments }],
    })
    draft.setLocked('assignment-001', true)
    expect(draft.lockedAssignments.value[0]?.segments).toEqual(segments)
    draft.undo()
    expect(draft.model.value?.tasks.find((task) => task.id === 'assignment-001')?.segments).toEqual(
      segments,
    )
  })

  it('updates a single actual segment together with edited start/end (#4004)', () => {
    const draft = useWorkingScheduleDraft()
    const assignment = plan.assignments[0]!
    draft.loadPlan({
      ...plan,
      assignments: [
        { ...assignment, segments: [{ startUtc: assignment.startUtc, endUtc: assignment.endUtc }] },
      ],
    })
    draft.updateTask(assignment.assignmentId, { startUtc: '2026-07-24T08:30:00Z' })
    draft.setLocked(assignment.assignmentId, true)
    expect(draft.lockedAssignments.value[0]?.segments).toEqual([
      { startUtc: '2026-07-24T08:30:00Z', endUtc: assignment.endUtc },
    ])
  })

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
