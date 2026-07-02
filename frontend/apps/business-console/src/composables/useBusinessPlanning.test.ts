import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  acceptBusinessConsolePlanningSuggestionMutationOptions,
  createBusinessConsolePlanningMpsBucketMutationOptions,
  createOrUpdateBusinessConsolePlanningDemandMutationOptions,
  getBusinessConsolePlanningMrpPeggingQueryOptions,
  listBusinessConsolePlanningMpsBucketsQueryOptions,
  listBusinessConsolePlanningDemandsQueryOptions,
  listBusinessConsolePlanningMrpRunsQueryOptions,
  listBusinessConsolePlanningSuggestionsQueryOptions,
  releaseBusinessConsolePlanningMpsBucketMutationOptions,
  reviewBusinessConsolePlanningMpsBucketMutationOptions,
  runBusinessConsolePlanningMrpMutationOptions,
  updateBusinessConsolePlanningMpsBucketMutationOptions,
} from '@nerv-iip/api-client'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessPlanning } from './useBusinessPlanning'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  acceptBusinessConsolePlanningSuggestionMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: {
        accepted: true,
        downstreamService: vars.body.downstreamService,
        downstreamDocumentType: vars.body.downstreamDocumentType,
        downstreamDocumentId: 'WO-20260701-001',
      },
    })),
  })),
  createBusinessConsolePlanningMpsBucketMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { mpsId: 'mps-created', ...vars.body, status: 'Draft' } })),
  })),
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
  listBusinessConsolePlanningMpsBucketsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsolePlanningMpsBuckets' }],
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
  updateBusinessConsolePlanningMpsBucketMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { mpsId: vars.path.mpsId, ...vars.body } })),
  })),
  reviewBusinessConsolePlanningMpsBucketMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { mpsId: vars.path.mpsId, status: 'Reviewed', reviewedBy: vars.body.reviewedBy } })),
  })),
  releaseBusinessConsolePlanningMpsBucketMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({ success: true, data: { mpsId: vars.path.mpsId, status: 'Released', releasedBy: vars.body.releasedBy } })),
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
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
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
      data: {
        items: [{
          runId: 'run-1',
          suggestionCount: 2,
          inventorySnapshotSource: 'inventory-http:2;scheduled-receipts:error',
          hasInputDegradation: true,
          inputDegradationSources: ['scheduled-receipts'],
          inputSources: ['mps', 'sales-order', 'forecast', 'safety-stock'],
          inputCoverageStart: '2026-06-01',
          inputCoverageEnd: '2026-06-30',
        }],
      },
    })
    coladaState.queryDataById.set('listBusinessConsolePlanningMpsBuckets', {
      success: true,
      data: {
        items: [{
          mpsId: 'mps-1',
          skuCode: 'FG-SHOCK',
          quantity: 120,
          status: 'Released',
          bucketDate: '2026-06-15',
        }],
      },
    })
    coladaState.queryDataById.set('listBusinessConsolePlanningSuggestions', {
      success: true,
      data: { items: [{ suggestionId: 'suggestion-1', suggestionType: 'planned-work-order', status: 'Open' }] },
    })
    coladaState.queryDataById.set('getBusinessConsolePlanningMrpPegging', {
      success: true,
      data: {
        items: [{
          suggestionId: 'suggestion-1',
          demandSourceReference: 'SO-1001',
          sourceType: 'sales',
          grossDemandQuantity: 10,
        }],
      },
    })

    const { demands, mpsBuckets, mrpRuns, pegging, runSelection, suggestions } = useBusinessPlanning()

    expect(listBusinessConsolePlanningMpsBucketsQueryOptions).toHaveBeenCalledWith({
      query: { organizationId: 'org-002', environmentId: 'prod', skuCode: undefined, siteCode: undefined, status: undefined },
    })
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
    expect(mpsBuckets.value[0]?.status).toBe('Released')
    expect(mrpRuns.value[0]?.inventorySnapshotSource).toBe('inventory-http:2;scheduled-receipts:error')
    expect(mrpRuns.value[0]?.hasInputDegradation).toBe(true)
    expect(mrpRuns.value[0]?.inputDegradationSources).toEqual(['scheduled-receipts'])
    expect(mrpRuns.value[0]?.inputSources).toEqual(['mps', 'sales-order', 'forecast', 'safety-stock'])
    expect(mrpRuns.value[0]?.inputCoverageStart).toBe('2026-06-01')
    expect(mrpRuns.value[0]?.inputCoverageEnd).toBe('2026-06-30')
    expect(suggestions.value[0]?.suggestionType).toBe('planned-work-order')
    expect(pegging.value[0]?.demandSourceReference).toBe('SO-1001')
    expect(pegging.value[0]?.sourceType).toBe('sales')
    expect(pegging.value[0]?.grossDemandQuantity).toBe(10)
  })

  it('creates, updates, reviews, and releases MPS buckets through generated mutations', async () => {
    const auth = useAuthStore()
    auth.$patch({
      principal: {
        principalId: 'user-planner-001',
        principalType: 'user',
        loginName: 'planner.li',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionCodes: [],
      },
    })
    const {
      createMpsBucket,
      mpsForm,
      releaseMpsBucket,
      reviewMpsBucket,
      updateMpsBucket,
    } = useBusinessPlanning()

    mpsForm.skuCode = 'FG-SHOCK'
    mpsForm.uomCode = 'pcs'
    mpsForm.siteCode = 'SITE-01'
    mpsForm.bucketDate = '2026-06-15'
    mpsForm.quantity = 120
    await createMpsBucket()
    mpsForm.quantity = 132
    await updateMpsBucket('mps-1')
    await reviewMpsBucket('mps-1')
    await releaseMpsBucket('mps-1')

    expect(createBusinessConsolePlanningMpsBucketMutationOptions).toHaveBeenCalled()
    expect(vi.mocked(createBusinessConsolePlanningMpsBucketMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        body: expect.objectContaining({
          skuCode: 'FG-SHOCK',
          bucketDate: '2026-06-15',
          quantity: 120,
        }),
      })
    expect(updateBusinessConsolePlanningMpsBucketMutationOptions).toHaveBeenCalled()
    expect(vi.mocked(updateBusinessConsolePlanningMpsBucketMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        path: { mpsId: 'mps-1' },
        body: expect.objectContaining({ quantity: 132 }),
      })
    expect(reviewBusinessConsolePlanningMpsBucketMutationOptions).toHaveBeenCalled()
    expect(vi.mocked(reviewBusinessConsolePlanningMpsBucketMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        path: { mpsId: 'mps-1' },
        query: { organizationId: 'org-001', environmentId: 'env-dev' },
        body: { reviewedBy: 'planner.li' },
      })
    expect(releaseBusinessConsolePlanningMpsBucketMutationOptions).toHaveBeenCalled()
    expect(vi.mocked(releaseBusinessConsolePlanningMpsBucketMutationOptions).mock.results[0]?.value.mutation)
      .toHaveBeenCalledWith({
        path: { mpsId: 'mps-1' },
        query: { organizationId: 'org-001', environmentId: 'env-dev' },
        body: { releasedBy: 'planner.li' },
      })
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({ predicate: expect.any(Function) })
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

  it('accepts a planning suggestion through generated mutation and refreshes planning queries', async () => {
    const { acceptSuggestion } = useBusinessPlanning()

    const result = await acceptSuggestion({
      suggestionId: 'suggestion-1',
      suggestionType: 'planned-work-order',
    })

    expect(acceptBusinessConsolePlanningSuggestionMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(acceptBusinessConsolePlanningSuggestionMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      path: { suggestionId: 'suggestion-1' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
      body: expect.objectContaining({
        downstreamService: 'BusinessMes',
        downstreamDocumentType: 'WorkOrder',
        downstreamDocumentId: null,
      }),
    })
    expect(result.data?.downstreamDocumentId).toBe('WO-20260701-001')
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({ predicate: expect.any(Function) })
  })
})
