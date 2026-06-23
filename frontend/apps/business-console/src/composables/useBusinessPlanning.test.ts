import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  createOrUpdateBusinessConsolePlanningDemandMutationOptions,
  getBusinessConsolePlanningMrpPeggingQueryOptions,
  listBusinessConsolePlanningDemandsQueryOptions,
  listBusinessConsolePlanningMrpRunsQueryOptions,
  listBusinessConsolePlanningSuggestionsQueryOptions,
  runBusinessConsolePlanningMrpMutationOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessPlanning } from './useBusinessPlanning'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  createOrUpdateBusinessConsolePlanningDemandMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: vars.body })),
  })),
  getBusinessConsolePlanningMrpPeggingQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsolePlanningMrpPegging' }],
    query: vi.fn(),
  })),
  listBusinessConsolePlanningDemandsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsolePlanningDemands' }],
    query: vi.fn(),
  })),
  listBusinessConsolePlanningMrpRunsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsolePlanningMrpRuns' }],
    query: vi.fn(),
  })),
  listBusinessConsolePlanningSuggestionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsolePlanningSuggestions' }],
    query: vi.fn(),
  })),
  runBusinessConsolePlanningMrpMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { runId: 'run-1', suggestionCount: 2, vars } })),
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

describe('business planning composable', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
  })

  it('loads demands, MRP runs, suggestions, and pegging with the current business context', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-002', environmentId: 'prod' })
    coladaState.queryDataById.set('listBusinessConsolePlanningDemands', {
      success: true,
      data: { items: [{ demandSourceId: 'demand-1', skuCode: 'FG-SHOCK', quantity: 120 }] },
    })
    coladaState.queryDataById.set('listBusinessConsolePlanningMrpRuns', {
      success: true,
      data: { items: [{ runId: 'run-1', suggestionCount: 2, inventorySnapshotSource: 'inventory-http:2' }] },
    })
    coladaState.queryDataById.set('listBusinessConsolePlanningSuggestions', {
      success: true,
      data: { items: [{ suggestionId: 'suggestion-1', suggestionType: 'planned-work-order', status: 'Open' }] },
    })
    coladaState.queryDataById.set('getBusinessConsolePlanningMrpPegging', {
      success: true,
      data: { items: [{ suggestionId: 'suggestion-1', demandSourceReference: 'SO-1001' }] },
    })

    const { demands, mrpRuns, pegging, runSelection, suggestions } = useBusinessPlanning()

    expect(listBusinessConsolePlanningDemandsQueryOptions).toHaveBeenCalledWith({
      query: { organizationId: 'org-002', environmentId: 'prod' },
    })
    expect(listBusinessConsolePlanningMrpRunsQueryOptions).toHaveBeenCalledWith({
      query: { organizationId: 'org-002', environmentId: 'prod' },
    })
    expect(listBusinessConsolePlanningSuggestionsQueryOptions).toHaveBeenCalledWith({
      query: { organizationId: 'org-002', environmentId: 'prod', status: 'open' },
    })
    expect(getBusinessConsolePlanningMrpPeggingQueryOptions).toHaveBeenCalledWith({
      path: { runId: '' },
      query: { organizationId: 'org-002', environmentId: 'prod' },
    })
    expect(coladaState.queryOptionsById.get('getBusinessConsolePlanningMrpPegging')?.enabled).toBe(false)
    expect(runSelection.runId).toBe('')
    expect(demands.value).toHaveLength(1)
    expect(mrpRuns.value[0]?.inventorySnapshotSource).toBe('inventory-http:2')
    expect(suggestions.value[0]?.suggestionType).toBe('planned-work-order')
    expect(pegging.value[0]?.demandSourceReference).toBe('SO-1001')
  })

  it('starts with a blank demand form instead of demo production values', () => {
    const { demandForm } = useBusinessPlanning()

    expect(demandForm.sourceReference).toBe('')
    expect(demandForm.skuCode).toBe('')
    expect(demandForm.uomCode).toBe('')
    expect(demandForm.siteCode).toBe('')
    expect(demandForm.quantity).toBe(0)
  })

  it('submits demand and MRP run payloads through generated mutations', async () => {
    const { createOrUpdateDemand, demandForm, runMrp, runRequest } = useBusinessPlanning()

    demandForm.sourceReference = 'SO-2026-001'
    demandForm.skuCode = 'FG-SHOCK'
    demandForm.quantity = 80
    await createOrUpdateDemand()

    runRequest.horizonStart = '2026-06-01'
    runRequest.horizonEnd = '2026-06-30'
    await runMrp()

    expect(createOrUpdateBusinessConsolePlanningDemandMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(createOrUpdateBusinessConsolePlanningDemandMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        sourceReference: 'SO-2026-001',
        skuCode: 'FG-SHOCK',
        quantity: 80,
      }),
    })
    expect(runBusinessConsolePlanningMrpMutationOptions).toHaveBeenCalled()
    expect(vi.mocked(runBusinessConsolePlanningMrpMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        body: expect.objectContaining({
          horizonStart: '2026-06-01',
          horizonEnd: '2026-06-30',
        }),
      })
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({ predicate: expect.any(Function) })
  })
})
