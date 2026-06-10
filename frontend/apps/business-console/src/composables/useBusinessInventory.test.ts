import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  confirmBusinessConsoleInventoryCountAdjustmentMutationOptions,
  createBusinessConsoleInventoryCountTaskMutationOptions,
  getBusinessConsoleInventoryAvailabilityQueryOptions,
  postBusinessConsoleInventoryMovementMutationOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  useInventoryAvailability,
  useInventoryCounts,
  useInventoryMovement,
} from './useBusinessInventory'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryFactoriesById: new Map<string, () => unknown>(),
  queryDataById: new Map<string, unknown>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  confirmBusinessConsoleInventoryCountAdjustmentMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars,
    })),
  })),
  createBusinessConsoleInventoryCountTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  getBusinessConsoleInventoryAvailabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleInventoryAvailability' }],
    query: vi.fn(),
  })),
  postBusinessConsoleInventoryMovementMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
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
    coladaState.queryFactoriesById.set(id, optionsFactory)

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

describe('business inventory composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryFactoriesById.clear()
    coladaState.queryDataById.clear()
  })

  it('loads availability with default editable filters', () => {
    coladaState.queryDataById.set('getBusinessConsoleInventoryAvailability', {
      success: true,
      data: {
        skuCode: 'SKU-001',
        availableQuantity: 8,
        items: [{ locationCode: 'A-01', availableQuantity: 8 }],
      },
    })

    const { availability, availabilityLines } = useInventoryAvailability()

    expect(getBusinessConsoleInventoryAvailabilityQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skuCode: '',
        uomCode: '',
        siteCode: '',
        qualityStatus: 'available',
        ownerType: 'owned',
      },
    })
    expect(availability.value?.availableQuantity).toBe(8)
    expect(availabilityLines.value).toEqual([{ locationCode: 'A-01', availableQuantity: 8 }])
  })

  it('defaults availability and lines safely for unsuccessful envelopes', () => {
    coladaState.queryDataById.set('getBusinessConsoleInventoryAvailability', {
      success: false,
    })

    const { availability, availabilityLines } = useInventoryAvailability()

    expect(availability.value).toBeUndefined()
    expect(availabilityLines.value).toEqual([])
  })

  it('uses the latest business context store values for availability queries', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-inv-a', environmentId: 'env-inv-a' })

    const { filters } = useInventoryAvailability()
    filters.skuCode = 'SKU-CTX'
    filters.uomCode = 'EA'
    filters.siteCode = 'S2'

    context.patchContext({ organizationId: 'org-inv-b', environmentId: 'env-inv-b' })
    coladaState.queryFactoriesById.get('getBusinessConsoleInventoryAvailability')?.()

    expect(getBusinessConsoleInventoryAvailabilityQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-inv-b',
        environmentId: 'env-inv-b',
        skuCode: 'SKU-CTX',
        uomCode: 'EA',
        siteCode: 'S2',
        qualityStatus: 'available',
        ownerType: 'owned',
      },
    })
  })

  it('suppresses availability requests until required scope is selected', () => {
    const { filters } = useInventoryAvailability()

    expect(filters.skuCode).toBe('')
    expect(filters.siteCode).toBe('')

    const options = coladaState.queryFactoriesById.get('getBusinessConsoleInventoryAvailability')?.()

    expect(options).toMatchObject({ enabled: false })
  })

  it('submits inventory movements with the provided body', async () => {
    const { postMovement } = useInventoryMovement()

    await postMovement({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      movementType: 'receipt',
      sourceService: 'business-console',
      sourceDocumentId: 'doc-1',
      idempotencyKey: 'idem-1',
      skuCode: 'SKU-001',
      uomCode: 'EA',
      siteCode: 'S1',
      locationCode: 'A-01',
      qualityStatus: 'available',
      ownerType: 'owned',
      quantity: 5,
    })

    expect(postBusinessConsoleInventoryMovementMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(postBusinessConsoleInventoryMovementMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        idempotencyKey: 'idem-1',
        quantity: 5,
      }),
    })
  })

  it('submits count task creation and adjustment confirmation', async () => {
    const { confirmAdjustment, createCountTask } = useInventoryCounts()

    await createCountTask({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      countTaskCode: 'COUNT-1',
      skuCode: 'SKU-001',
      uomCode: 'EA',
      siteCode: 'S1',
      qualityStatus: 'available',
      ownerType: 'owned',
    })

    expect(coladaState.invalidateQueries).not.toHaveBeenCalled()

    await confirmAdjustment('count-1', {
      countedQuantity: 9,
      idempotencyKey: 'adjust-1',
    })

    expect(createBusinessConsoleInventoryCountTaskMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(createBusinessConsoleInventoryCountTaskMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        countTaskCode: 'COUNT-1',
      }),
    })
    expect(confirmBusinessConsoleInventoryCountAdjustmentMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(confirmBusinessConsoleInventoryCountAdjustmentMutationOptions).mock.results[0]
        ?.value.mutation,
    ).toHaveBeenCalledWith({
      path: {
        countTaskId: 'count-1',
      },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
      body: {
        countedQuantity: 9,
        idempotencyKey: 'adjust-1',
      },
    })
    expect(coladaState.invalidateQueries).toHaveBeenCalledWith({
      predicate: expect.any(Function),
    })
  })
})
