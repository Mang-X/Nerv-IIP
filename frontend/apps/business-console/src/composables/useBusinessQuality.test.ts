import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  closeBusinessConsoleQualityNcrMutationOptions,
  createBusinessConsoleQualityInspectionRecordMutationOptions,
  listBusinessConsoleQualityInspectionPlansQueryOptions,
  listBusinessConsoleQualityNcrsQueryOptions,
  submitBusinessConsoleQualityNcrDispositionMutationOptions,
} from '@nerv-iip/api-client'
import { useQualityInspectionPlans, useQualityNcrs } from './useBusinessQuality'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryDataById: new Map<string, unknown>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  closeBusinessConsoleQualityNcrMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars,
    })),
  })),
  createBusinessConsoleQualityInspectionRecordMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  listBusinessConsoleQualityInspectionPlansQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleQualityInspectionPlans' }],
    query: vi.fn(),
  })),
  listBusinessConsoleQualityNcrsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleQualityNcrs' }],
    query: vi.fn(),
  })),
  submitBusinessConsoleQualityNcrDispositionMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars,
    })),
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

describe('business quality composables', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryDataById.clear()
  })

  it('lists inspection plans with default context and safe items', () => {
    coladaState.queryDataById.set('listBusinessConsoleQualityInspectionPlans', {
      success: true,
      data: {
        total: 34,
        items: [
          {
            id: 'plan-1',
            code: 'PLAN-001',
          },
        ],
      },
    })

    const { inspectionPlans, inspectionPlansTotal } = useQualityInspectionPlans()

    expect(listBusinessConsoleQualityInspectionPlansQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
      },
    })
    expect(inspectionPlansTotal.value).toBe(34)
    expect(inspectionPlans.value).toEqual([
      {
        id: 'plan-1',
        code: 'PLAN-001',
      },
    ])
  })

  it('exposes empty inspection plans and NCRs for unsuccessful envelopes', () => {
    coladaState.queryDataById.set('listBusinessConsoleQualityInspectionPlans', {
      success: false,
    })
    coladaState.queryDataById.set('listBusinessConsoleQualityNcrs', {
      success: false,
    })

    const { inspectionPlans } = useQualityInspectionPlans()
    const { ncrs } = useQualityNcrs()

    expect(inspectionPlans.value).toEqual([])
    expect(ncrs.value).toEqual([])
  })

  it('lists NCRs with a take limit and invalidates after actions', async () => {
    coladaState.queryDataById.set('listBusinessConsoleQualityNcrs', {
      success: true,
      data: {
        total: 56,
        items: [
          {
            id: 'ncr-1',
            code: 'NCR-001',
          },
        ],
      },
    })

    const { closeNcr, ncrs, ncrsTotal, submitDisposition } = useQualityNcrs()

    expect(listBusinessConsoleQualityNcrsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
      },
    })
    expect(ncrsTotal.value).toBe(56)
    expect(ncrs.value).toEqual([
      {
        id: 'ncr-1',
        code: 'NCR-001',
      },
    ])

    await submitDisposition('ncr-1', {
      dispositionType: 'rework',
      dispositionApprovalChainId: 'chain-1',
    })
    await closeNcr('ncr-1', {
      reworkWorkOrderId: 'wo-1',
    })

    expect(submitBusinessConsoleQualityNcrDispositionMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(submitBusinessConsoleQualityNcrDispositionMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      path: {
        ncrId: 'ncr-1',
      },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
      body: {
        dispositionType: 'rework',
        dispositionApprovalChainId: 'chain-1',
      },
    })
    expect(closeBusinessConsoleQualityNcrMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(closeBusinessConsoleQualityNcrMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      path: {
        ncrId: 'ncr-1',
      },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
      body: {
        reworkWorkOrderId: 'wo-1',
      },
    })
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(2)
  })

  it('submits inspection records through the generated mutation option', async () => {
    const { createInspectionRecord } = useQualityInspectionPlans()

    await createInspectionRecord({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      sourceType: 'receipt',
      sourceService: 'business-console',
      sourceDocumentId: 'doc-1',
      skuCode: 'SKU-001',
      inspectedQuantity: 5,
      resultLines: [],
    })

    expect(createBusinessConsoleQualityInspectionRecordMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(createBusinessConsoleQualityInspectionRecordMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        sourceDocumentId: 'doc-1',
      }),
    })
  })
})
