import { describe, expect, expectTypeOf, it } from 'vitest'
import {
  createBusinessConsoleSchedulingWorkbenchPlan,
  getBusinessConsoleSchedulingPlan,
  createBusinessConsoleSchedulingPlanRevision,
  type BusinessConsoleSchedulePlan,
  type SchedulePlanFreezeContextContract,
  type SchedulePlanFreezeWorkCenterWindowContract,
  type SchedulePlanFrozenAssignmentContract,
  type SchedulePlanFreezeReasonContract,
} from '@nerv-iip/api-client'

// PublicContract: #4122 and ADR 0032 §3; original assignment and precise reasons survive transport.
const reason: SchedulePlanFreezeReasonContract = 'manualLock'
const window: SchedulePlanFreezeWorkCenterWindowContract = {
  workCenterId: 'wc-override',
  endUtc: '2026-10-07T08:00:00Z',
}
const frozen: SchedulePlanFrozenAssignmentContract = {
  assignment: {
    assignmentId: 'a-1',
    orderId: 'order-001',
    operationId: 'op-1',
    operationSequence: 1,
    resourceId: 'original-resource',
    workCenterId: 'wc-001',
    startUtc: '2026-10-07T09:00:00Z',
    endUtc: '2026-10-07T12:00:00Z',
    isLocked: true,
    explanationCode: 'locked',
    segments: [
      { startUtc: '2026-10-07T09:00:00Z', endUtc: '2026-10-07T10:00:00Z' },
      { startUtc: '2026-10-07T11:00:00Z', endUtc: '2026-10-07T12:00:00Z' },
    ],
  },
  reasons: ['started', reason, 'stableWindow'],
}
const context: SchedulePlanFreezeContextContract = {
  asOfUtc: '2026-10-07T08:00:00Z',
  defaultWindowEndUtc: '2026-10-07T10:00:00Z',
  workCenterWindows: [window],
  assignments: [frozen],
}
describe('Scheduling freeze context stable consumption', () => {
  it.each([context, null])(
    'preserves freeze context %j from create, detail and revision SDK responses',
    async (freezeContext) => {
      const plan: BusinessConsoleSchedulePlan = { planId: 'plan-001', freezeContext }
      const fetch: typeof globalThis.fetch = async (input) => {
        const request = input as Request
        return Response.json({
          data: request.url.endsWith('/revisions') ? { candidate: plan } : plan,
        })
      }
      const options = { baseUrl: 'http://gateway.local', fetch, throwOnError: true as const }
      const created = await createBusinessConsoleSchedulingWorkbenchPlan({
        ...options,
        body: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          horizonStartUtc: '2026-10-07T08:00:00Z',
          horizonEndUtc: '2026-10-08T08:00:00Z',
          orders: [{ workOrderId: 'order-001', priority: 1, isRush: true }],
        },
      })
      const detail = await getBusinessConsoleSchedulingPlan({
        ...options,
        path: { planId: 'plan-001' },
        query: { organizationId: 'org-001', environmentId: 'env-dev' },
      })
      const revision = await createBusinessConsoleSchedulingPlanRevision({
        ...options,
        path: { planId: 'plan-001' },
        body: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          includedOrderIds: ['order-001'],
          lockedAssignments: [],
        },
      })
      for (const value of [
        created.data.data?.freezeContext,
        detail.data.data?.freezeContext,
        revision.data.data?.candidate?.freezeContext,
      ]) {
        expectTypeOf(value).toEqualTypeOf<SchedulePlanFreezeContextContract | null | undefined>()
        expect(value).toEqual(freezeContext)
      }
    },
  )
})
