import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  getBusinessConsoleSchedulingPlanQueryOptions,
  listBusinessConsoleSchedulingPlansQueryOptions,
  releaseBusinessConsoleSchedulingPlanMutationOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessScheduling } from './useBusinessScheduling'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleSchedulingPlanQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleSchedulingPlan' }],
    query: vi.fn(),
  })),
  listBusinessConsoleSchedulingPlansQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleSchedulingPlans' }],
    query: vi.fn(),
  })),
  releaseBusinessConsoleSchedulingPlanMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { planId: vars.path.planId, status: 'released' } })),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
  })),
}))

describe('business scheduling composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
  })

  it('loads APS plan summaries and gates detail until a plan is selected', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-002', environmentId: 'prod' })
    coladaState.queryDataById.set('listBusinessConsoleSchedulingPlans', {
      success: true,
      data: [{
        planId: 'plan-001',
        status: 'generated',
        assignmentCount: 8,
        conflictCount: 1,
        unscheduledOperationCount: 2,
      }],
    })

    const { detailSelection, planDetail, plans } = useBusinessScheduling()

    expect(listBusinessConsoleSchedulingPlansQueryOptions).toHaveBeenCalledWith({
      query: { organizationId: 'org-002', environmentId: 'prod', pageIndex: 1, pageSize: 20 },
    })
    expect(getBusinessConsoleSchedulingPlanQueryOptions).toHaveBeenCalledWith({
      path: { planId: '' },
      query: { organizationId: 'org-002', environmentId: 'prod' },
    })
    expect(coladaState.queryOptionsById.get('getBusinessConsoleSchedulingPlan')?.enabled).toBe(false)
    expect(detailSelection.planId).toBe('')
    expect(plans.value[0]?.planId).toBe('plan-001')
    expect(planDetail.value).toBeUndefined()
  })

  it('releases a selected plan through the generated facade and invalidates scheduling reads', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    const { detailSelection, releasePlan } = useBusinessScheduling()
    detailSelection.planId = 'plan-001'

    await releasePlan('plan-001')

    expect(releaseBusinessConsoleSchedulingPlanMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(releaseBusinessConsoleSchedulingPlanMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      path: { planId: 'plan-001' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
    })
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({ predicate: expect.any(Function) })
  })
})
