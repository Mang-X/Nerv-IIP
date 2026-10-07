import { flushPromises } from '@vue/test-utils'
import { effectScope, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useWorkingScheduleDraft } from './useWorkingScheduleDraft'
import { useSchedulingDraftPersistence } from './useSchedulingDraftPersistence'
import type { SchedulingWorkingDraft, SchedulingWorkingDraftState } from '@nerv-iip/api-client'

const server = vi.hoisted(() => ({
  drafts: new Map<string, SchedulingWorkingDraft>(),
  saveBarrier: undefined as Promise<void> | undefined,
  saveFailure: false,
}))
const plan = {
  planId: 'plan-001',
  status: 'generated' as const,
  assignments: [
    {
      assignmentId: 'task-1',
      orderId: 'WO-1',
      operationId: 'OP-10',
      operationSequence: 10,
      resourceId: 'RES-1',
      workCenterId: 'WC-1',
      startUtc: '2026-10-07T08:00:00Z',
      endUtc: '2026-10-07T09:00:00Z',
    },
  ],
}
vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsoleSchedulingWorkingDrafts: async () => ({
    data: { success: true, data: [...server.drafts.values()] },
  }),
  getBusinessConsoleSchedulingPlan: async () => ({ data: { success: true, data: plan } }),
  saveBusinessConsoleSchedulingWorkingDraft: async (options: {
    path: { planId: string }
    body: { state: SchedulingWorkingDraftState }
  }) => {
    await server.saveBarrier
    if (server.saveFailure) return { data: { success: false, message: '保存未完成' } }
    const result = {
      planId: options.path.planId,
      savedAtUtc: '2026-10-07T09:00:00Z',
      state: options.body.state,
    }
    server.drafts.set(options.path.planId, result)
    return { data: { success: true, data: result } }
  },
  clearBusinessConsoleSchedulingWorkingDraft: async (options: { path: { planId: string } }) => {
    server.drafts.delete(options.path.planId)
    return { data: { success: true } }
  },
}))
vi.mock('@/utils/notify', () => ({ notifyOperationFailure: vi.fn() }))

beforeEach(() => {
  server.drafts.clear()
  server.saveBarrier = undefined
  server.saveFailure = false
})
function setup() {
  const scope = effectScope()
  const draft = useWorkingScheduleDraft()
  const baseline = shallowRef<typeof plan>()
  const persistence = scope.run(() =>
    useSchedulingDraftPersistence({
      draft,
      baseline,
      context: () => ({ organizationId: 'ORG', environmentId: 'ENV' }),
      userId: () => 'user-1',
      canManage: () => true,
    }),
  )!
  return { scope, draft, persistence, baseline }
}
describe('scheduling draft persistence', () => {
  it('waits for the server before reporting saved, and clears after an outstanding save', async () => {
    const { scope, draft, persistence } = setup()
    await flushPromises()
    let completeSave!: () => void
    server.saveBarrier = new Promise((resolve) => {
      completeSave = resolve
    })
    draft.loadPlan(plan)
    draft.updateTask('task-1', { resourceId: 'RES-2' })
    await flushPromises()
    expect(persistence.status.value).toBe('saving')
    const clearing = persistence.clear()
    completeSave()
    await clearing
    await flushPromises()
    expect(server.drafts.size).toBe(0)
    expect(draft.model.value).toBeUndefined()
    expect(persistence.status.value).toBe('empty')
    scope.stop()
  })
  it('reports failed saves rather than success and allows an explicit retry', async () => {
    const { scope, draft, persistence } = setup()
    await flushPromises()
    server.saveFailure = true
    draft.loadPlan(plan)
    await flushPromises()
    expect(persistence.status.value).toBe('error')
    expect(server.drafts.size).toBe(0)
    server.saveFailure = false
    await persistence.save()
    expect(persistence.status.value).toBe('saved')
    expect(server.drafts.get('plan-001')?.state?.tasks?.[0]?.resourceId).toBe('RES-1')
    scope.stop()
  })
  it('restores saved state without overwriting it when candidate orders arrive later', async () => {
    server.drafts.set('plan-001', {
      planId: 'plan-001',
      savedAtUtc: '2026-10-07T09:00:00Z',
      state: {
        contractVersion: 1,
        orders: [{ workOrderId: 'WO-1', priority: 90, isRush: true, included: true }],
        tasks: [
          {
            taskId: 'task-1',
            orderId: 'WO-1',
            operationId: 'OP-10',
            resourceId: 'RES-2',
            workCenterId: 'WC-1',
            startUtc: '2026-10-07T10:00:00Z',
            endUtc: '2026-10-07T11:00:00Z',
            locked: true,
          },
        ],
        pendingOperations: [],
      },
    })
    const { scope, draft, persistence, baseline } = setup()
    await flushPromises()
    draft.setOrders([{ workOrderId: 'WO-1', priority: 100 }])
    expect(persistence.status.value).toBe('saved')
    expect(draft.includedOrders.value[0]).toMatchObject({ priority: 90, isRush: true })
    expect(draft.lockedAssignments.value[0]?.resourceId).toBe('RES-2')
    expect(baseline.value?.assignments[0]?.resourceId).toBe('RES-1')
    expect(draft.canUndo.value).toBe(false)
    scope.stop()
  })
})

it('keeps other plans when clearing the selected draft and reopens the selected plan empty', async () => {
  const state: SchedulingWorkingDraftState = {
    contractVersion: 1,
    orders: [],
    tasks: [],
    pendingOperations: [],
  }
  server.drafts.set('plan-001', { planId: 'plan-001', savedAtUtc: '2026-10-07T10:00:00Z', state })
  server.drafts.set('plan-002', { planId: 'plan-002', savedAtUtc: '2026-10-07T09:00:00Z', state })
  const { scope, persistence } = setup()
  await flushPromises()
  await persistence.clear()
  expect(server.drafts.has('plan-001')).toBe(false)
  expect(server.drafts.has('plan-002')).toBe(true)
  scope.stop()
  const reopenedScope = effectScope()
  const reopened = useWorkingScheduleDraft()
  const persistenceAgain = reopenedScope.run(() =>
    useSchedulingDraftPersistence({
      draft: reopened,
      baseline: shallowRef(),
      context: () => ({ organizationId: 'ORG', environmentId: 'ENV' }),
      userId: () => 'user-1',
      canManage: () => true,
      selectedPlanId: () => 'plan-001',
    }),
  )!
  await flushPromises()
  expect(reopened.model.value).toBeUndefined()
  expect(persistenceAgain.status.value).toBe('empty')
  expect(persistenceAgain.savedDrafts.value.map((item) => item.planId)).toEqual(['plan-002'])
  reopenedScope.stop()
})
